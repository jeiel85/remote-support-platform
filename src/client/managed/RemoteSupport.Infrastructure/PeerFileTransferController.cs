using System.Security.Cryptography;
using Google.Protobuf;
using RemoteSupport.Application;
using RemoteSupport.Protocol;
using RemoteSupport.Protocol.V1;
using System.Runtime.Versioning;

namespace RemoteSupport.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class PeerFileTransferController : IAsyncDisposable
{
    private const int ChunkSize = 128 * 1024;
    private readonly Guid sessionId;
    private readonly PeerDataSession protocol;
    private readonly NativeAttendedSession transport;
    private readonly FileTransferPolicy policy;
    private readonly IncomingFileTransferCoordinator receiver;
    private readonly Dictionary<Guid, OutgoingFile> outgoing = [];
    private readonly SemaphoreSlim gate = new(1, 1);

    public PeerFileTransferController(Guid sessionId, PeerDataSession protocol, NativeAttendedSession transport,
        string destinationRoot, FileTransferPolicy? policy = null, IReceivedFileSafety? safety = null,
        IDataFeatureAuditSink? audit = null)
    {
        this.sessionId = sessionId;
        this.protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.policy = policy ?? new FileTransferPolicy();
        receiver = new IncomingFileTransferCoordinator(sessionId, protocol.LocalRole, destinationRoot,
            protocol.PermissionGate, this.policy, safety: safety, audit: audit);
    }

    public Func<FileTransferAcceptance, Task<bool>>? ApprovalRequested { get; set; }
    public event Action<string>? StatusChanged;

    public async Task OfferAsync(string path, CancellationToken cancellationToken = default)
    {
        FileInfo file = new(path);
        if (!file.Exists || file.Length <= 0) throw new FileNotFoundException("Selected transfer file is missing or empty.", path);
        string name = SafeReceivedFile.NormalizeDisplayName(file.Name);
        SessionPeerRole source = protocol.LocalRole;
        PermissionSnapshot snapshot = protocol.PermissionGate.Current;
        protocol.PermissionGate.Demand(SessionPermissionGate.FileScope(source), snapshot.Revision, "FILE_POLICY_BLOCKED");
        if ((ulong)file.Length > policy.MaximumFileBytes || policy.BlockedExtensions.Contains(file.Extension))
            throw new DataFeatureException("FILE_POLICY_BLOCKED", "Selected file is blocked by transfer policy.");
        byte[] hash;
        await using (FileStream stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
            hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        FileTransferManifest manifest = new(Guid.CreateVersion7(), name, checked((ulong)file.Length), hash, null, ChunkSize,
            DivideRoundUp(checked((ulong)file.Length), ChunkSize), source,
            source == SessionPeerRole.Host ? SessionPeerRole.Operator : SessionPeerRole.Host, DateTimeOffset.UtcNow);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (outgoing.Count >= policy.MaximumConcurrentTransfers) throw new DataFeatureException("FILE_POLICY_BLOCKED", "Concurrent transfer limit reached.");
            outgoing.Add(manifest.TransferId, new OutgoingFile(path, manifest, new CancellationTokenSource()));
        }
        finally { gate.Release(); }
        transport.Send("rsp.file.control.v1", protocol.Encode(PeerChannel.FileControl, envelope => envelope.FileOffer = ToProtocol(manifest)));
        StatusChanged?.Invoke($"Waiting for receiver approval: {name}");
    }

    public async Task HandleAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        switch (envelope.BodyCase)
        {
            case Envelope.BodyOneofCase.FileOffer:
                await HandleOfferAsync(envelope.FileOffer, envelope.PermissionRevision, cancellationToken).ConfigureAwait(false);
                break;
            case Envelope.BodyOneofCase.FileDecision:
                await HandleDecisionAsync(envelope.FileDecision, cancellationToken).ConfigureAwait(false);
                break;
            case Envelope.BodyOneofCase.FileChunk:
                await HandleChunkAsync(envelope.FileChunk, envelope.PermissionRevision, cancellationToken).ConfigureAwait(false);
                break;
            case Envelope.BodyOneofCase.FileCancel:
                await CancelLocalAsync(Guid.Parse(envelope.FileCancel.TransferId), envelope.FileCancel.ReasonCode,
                    envelope.FileCancel.DeletePartialFile, cancellationToken).ConfigureAwait(false);
                break;
            case Envelope.BodyOneofCase.FileAck:
                StatusChanged?.Invoke(envelope.FileAck.Complete ? "File transfer completed and hash verified." :
                    $"Received {envelope.FileAck.ReceivedBytes:N0} bytes.");
                break;
        }
    }

    private async Task HandleOfferAsync(FileOffer offer, ulong revision, CancellationToken cancellationToken)
    {
        FileTransferManifest manifest = FromProtocol(offer);
        try
        {
            FileTransferAcceptance acceptance = await receiver.AcceptAsync(manifest, revision, DateTimeOffset.UtcNow, cancellationToken)
                .ConfigureAwait(false);
            bool approved = ApprovalRequested is not null && await ApprovalRequested(acceptance).ConfigureAwait(false);
            if (!approved)
            {
                await receiver.CancelAsync(manifest.TransferId, "FILE_TRANSFER_CANCELLED", true, DateTimeOffset.UtcNow, cancellationToken)
                    .ConfigureAwait(false);
            }
            transport.Send("rsp.file.control.v1", protocol.Encode(PeerChannel.FileControl, envelope => envelope.FileDecision = new FileDecision
            {
                TransferId = manifest.TransferId.ToString("D"), Accepted = approved, ChunkSize = checked((uint)acceptance.ChunkSize),
                ReasonCode = approved ? string.Empty : "FILE_TRANSFER_CANCELLED", MaxInFlightBytes = acceptance.MaximumInFlightBytes,
                AlreadyVerified = { acceptance.AlreadyVerified.Select(range => new ChunkRange { StartInclusive = range.StartInclusive, EndExclusive = range.EndExclusive }) },
            }));
            StatusChanged?.Invoke(approved ? $"Receiving {acceptance.NormalizedName}" : "Incoming file was declined.");
        }
        catch (DataFeatureException exception)
        {
            transport.Send("rsp.file.control.v1", protocol.Encode(PeerChannel.FileControl, envelope => envelope.FileDecision = new FileDecision
            {
                TransferId = manifest.TransferId.ToString("D"), Accepted = false, ReasonCode = exception.Code.Value,
            }));
            throw;
        }
    }

    private async Task HandleDecisionAsync(FileDecision decision, CancellationToken cancellationToken)
    {
        Guid transferId = Guid.Parse(decision.TransferId);
        OutgoingFile? outgoingFile;
        HashSet<ulong> verified = decision.AlreadyVerified.SelectMany(range =>
            Enumerable.Range(checked((int)range.StartInclusive), checked((int)(range.EndExclusive - range.StartInclusive))).Select(value => (ulong)value)).ToHashSet();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!outgoing.TryGetValue(transferId, out outgoingFile)) return;
        }
        finally { gate.Release(); }
        if (!decision.Accepted)
        {
            await RemoveOutgoingAsync(transferId, cancellationToken).ConfigureAwait(false);
            StatusChanged?.Invoke("Receiver declined the file transfer.");
            return;
        }
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, outgoingFile.Cancellation.Token);
        try
        {
            FileChunkReader reader = new(outgoingFile.Manifest, outgoingFile.Path, new ImmediateBackpressure(), protocol.PermissionGate);
            await foreach (FileChunkData chunk in reader.ReadAsync(verified, linked.Token))
            {
                byte[] frame = protocol.Encode(PeerChannel.FileData, envelope => envelope.FileChunk = new FileChunk
                {
                    TransferId = chunk.TransferId.ToString("D"), ChunkIndex = chunk.ChunkIndex, Offset = chunk.Offset,
                    Data = ByteString.CopyFrom(chunk.Data), ChunkSha256 = ByteString.CopyFrom(chunk.Sha256),
                });
                while (true)
                {
                    try { transport.Send("rsp.file.data.v1", frame); break; }
                    catch (DataFeatureException exception) when (exception.Code.Value == "RATE_LIMITED")
                    { await Task.Delay(25, linked.Token).ConfigureAwait(false); }
                }
            }
        }
        catch (OperationCanceledException) when (outgoingFile.Cancellation.IsCancellationRequested)
        {
            StatusChanged?.Invoke("File transfer cancelled.");
        }
        finally { await RemoveOutgoingAsync(transferId, CancellationToken.None).ConfigureAwait(false); }
    }

    private async Task HandleChunkAsync(FileChunk chunk, ulong revision, CancellationToken cancellationToken)
    {
        FileTransferProgress progress = await receiver.ReceiveChunkAsync(new FileChunkData(Guid.Parse(chunk.TransferId),
            chunk.ChunkIndex, chunk.Offset, chunk.Data.ToByteArray(), chunk.ChunkSha256.ToByteArray()), revision,
            DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        transport.Send("rsp.file.control.v1", protocol.Encode(PeerChannel.FileControl, envelope => envelope.FileAck = new FileAck
        {
            TransferId = progress.TransferId.ToString("D"), Complete = progress.Complete, ReceivedBytes = progress.ReceivedBytes,
        }));
        StatusChanged?.Invoke(progress.Complete ? $"Received and verified: {Path.GetFileName(progress.DestinationPath)}" :
            $"Received {progress.ReceivedBytes:N0} bytes.");
    }

    public async ValueTask DisposeAsync()
    {
        try { await CancelAllAsync("SESSION_ENDED", false).ConfigureAwait(false); }
        catch (Exception exception) when (exception is InvalidOperationException or DataFeatureException or ObjectDisposedException) { }
        await receiver.DisposeAsync().ConfigureAwait(false);
    }

    public async Task CancelAllAsync(string reasonCode = "FILE_TRANSFER_CANCELLED", bool deletePartial = false,
        CancellationToken cancellationToken = default)
    {
        List<OutgoingFile> outgoingFiles;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            outgoingFiles = outgoing.Values.ToList();
            outgoing.Clear();
            foreach (OutgoingFile file in outgoingFiles) file.Cancellation.Cancel();
        }
        finally { gate.Release(); }
        IReadOnlyList<Guid> incomingIds = await receiver.CancelAllAsync(reasonCode, deletePartial,
            DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        foreach (Guid transferId in outgoingFiles.Select(file => file.Manifest.TransferId).Concat(incomingIds).Distinct())
        {
            transport.Send("rsp.file.control.v1", protocol.Encode(PeerChannel.FileControl, envelope => envelope.FileCancel = new FileCancel
            {
                TransferId = transferId.ToString("D"), ReasonCode = reasonCode, DeletePartialFile = deletePartial,
            }));
        }
        foreach (OutgoingFile file in outgoingFiles) file.Cancellation.Dispose();
        if (outgoingFiles.Count != 0 || incomingIds.Count != 0) StatusChanged?.Invoke("File transfer cancelled.");
    }

    private async Task CancelLocalAsync(Guid transferId, string reasonCode, bool deletePartial,
        CancellationToken cancellationToken)
    {
        OutgoingFile? outgoingFile = null;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (outgoing.Remove(transferId, out outgoingFile)) outgoingFile.Cancellation.Cancel();
        }
        finally { gate.Release(); }
        await receiver.CancelAsync(transferId, reasonCode, deletePartial, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);
        outgoingFile?.Cancellation.Dispose();
        StatusChanged?.Invoke("File transfer cancelled by peer.");
    }

    private async Task RemoveOutgoingAsync(Guid transferId, CancellationToken cancellationToken)
    {
        OutgoingFile? removed = null;
        try { await gate.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (ObjectDisposedException) { return; }
        try { outgoing.Remove(transferId, out removed); }
        finally { gate.Release(); }
        removed?.Cancellation.Dispose();
    }

    private static FileOffer ToProtocol(FileTransferManifest manifest) => new()
    {
        TransferId = manifest.TransferId.ToString("D"), DisplayName = manifest.DisplayName, Size = manifest.Size,
        Sha256 = ByteString.CopyFrom(manifest.Sha256), MimeHint = manifest.MimeHint ?? string.Empty,
        ProposedChunkSize = checked((uint)manifest.ProposedChunkSize), ProposedChunkCount = manifest.ProposedChunkCount,
        SourceRole = manifest.SourceRole == SessionPeerRole.Host ? PeerRole.Host : PeerRole.Operator,
        DestinationRole = manifest.DestinationRole == SessionPeerRole.Host ? PeerRole.Host : PeerRole.Operator,
        CreatedUtcUnixMs = manifest.CreatedAt.ToUnixTimeMilliseconds(),
    };

    private static FileTransferManifest FromProtocol(FileOffer offer) => new(Guid.Parse(offer.TransferId), offer.DisplayName,
        offer.Size, offer.Sha256.ToByteArray(), string.IsNullOrEmpty(offer.MimeHint) ? null : offer.MimeHint,
        checked((int)offer.ProposedChunkSize), offer.ProposedChunkCount,
        offer.SourceRole == PeerRole.Host ? SessionPeerRole.Host : SessionPeerRole.Operator,
        offer.DestinationRole == PeerRole.Host ? SessionPeerRole.Host : SessionPeerRole.Operator,
        DateTimeOffset.FromUnixTimeMilliseconds(offer.CreatedUtcUnixMs));

    private static ulong DivideRoundUp(ulong value, int divisor) => value / (ulong)divisor + (value % (ulong)divisor == 0 ? 0UL : 1UL);
    private sealed record OutgoingFile(string Path, FileTransferManifest Manifest, CancellationTokenSource Cancellation);
    private sealed class ImmediateBackpressure : IFileSendBackpressure
    { public ValueTask WaitForCapacityAsync(int nextChunkBytes, CancellationToken cancellationToken) => ValueTask.CompletedTask; }
}
