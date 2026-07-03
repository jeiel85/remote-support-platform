using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using RemoteSupport.Domain;

namespace RemoteSupport.Application;

public sealed class FileTransferManifest
{
    public FileTransferManifest(Guid transferId, string displayName, ulong size, byte[] sha256, string? mimeHint,
        int proposedChunkSize, ulong proposedChunkCount, SessionPeerRole sourceRole, SessionPeerRole destinationRole,
        DateTimeOffset createdAt)
    {
        TransferId = transferId;
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        Size = size;
        Sha256 = sha256?.ToArray() ?? throw new ArgumentNullException(nameof(sha256));
        MimeHint = mimeHint;
        ProposedChunkSize = proposedChunkSize;
        ProposedChunkCount = proposedChunkCount;
        SourceRole = sourceRole;
        DestinationRole = destinationRole;
        CreatedAt = createdAt;
    }

    public Guid TransferId { get; }
    public string DisplayName { get; }
    public ulong Size { get; }
    public byte[] Sha256 { get; }
    public string? MimeHint { get; }
    public int ProposedChunkSize { get; }
    public ulong ProposedChunkCount { get; }
    public SessionPeerRole SourceRole { get; }
    public SessionPeerRole DestinationRole { get; }
    public DateTimeOffset CreatedAt { get; }
}

public sealed record FileTransferPolicy
{
    public ulong MaximumFileBytes { get; init; } = 2UL * 1024 * 1024 * 1024;
    public int MinimumChunkBytes { get; init; } = 16 * 1024;
    public int MaximumChunkBytes { get; init; } = 1024 * 1024;
    public int MaximumConcurrentTransfers { get; init; } = 2;
    public ulong MaximumInFlightBytes { get; init; } = 4UL * 1024 * 1024;
    public long RequiredFreeSpaceReserveBytes { get; init; } = 256L * 1024 * 1024;
    public TimeSpan ResumeLifetime { get; init; } = TimeSpan.FromHours(24);
    public IReadOnlySet<string> BlockedExtensions { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".jse", ".wsf", ".msi", ".msp", ".scr", ".lnk",
    };

    public void Validate()
    {
        if (MaximumFileBytes == 0 || MinimumChunkBytes < 4096 || MaximumChunkBytes > 1024 * 1024 ||
            MinimumChunkBytes > MaximumChunkBytes || MaximumConcurrentTransfers is < 1 or > 16 ||
            MaximumInFlightBytes < (ulong)MaximumChunkBytes || RequiredFreeSpaceReserveBytes < 0 ||
            ResumeLifetime <= TimeSpan.Zero || ResumeLifetime > TimeSpan.FromDays(7))
            throw new ArgumentException("File transfer policy is outside product hard limits.");
    }
}

public sealed record FileChunkData(Guid TransferId, ulong ChunkIndex, ulong Offset, byte[] Data, byte[] Sha256);
public sealed record VerifiedChunkRange(ulong StartInclusive, ulong EndExclusive);
public sealed record FileTransferAcceptance(
    Guid TransferId,
    string NormalizedName,
    string DestinationPath,
    int ChunkSize,
    IReadOnlyList<VerifiedChunkRange> AlreadyVerified,
    ulong MaximumInFlightBytes,
    DateTimeOffset ResumeExpiresAt);
public sealed record FileTransferProgress(Guid TransferId, ulong ReceivedBytes, bool Complete, string? DestinationPath);

public sealed record DataFeatureAuditRecord(
    string EventType,
    Guid SessionId,
    Guid? TransferId,
    string Direction,
    string? NormalizedName,
    ulong? Size,
    string ResultCode,
    DateTimeOffset OccurredAt);

public interface IDataFeatureAuditSink
{
    ValueTask WriteAsync(DataFeatureAuditRecord record, CancellationToken cancellationToken);
}

public sealed class NullDataFeatureAuditSink : IDataFeatureAuditSink
{
    public ValueTask WriteAsync(DataFeatureAuditRecord record, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

public interface IStorageSpaceProbe
{
    long GetAvailableBytes(string path);
}

public sealed class DriveStorageSpaceProbe : IStorageSpaceProbe
{
    public long GetAvailableBytes(string path)
    {
        string root = Path.GetPathRoot(Path.GetFullPath(path)) ?? throw new IOException("Destination has no volume root.");
        return new DriveInfo(root).AvailableFreeSpace;
    }
}

public interface IReceivedFileSafety
{
    ValueTask InspectAsync(string temporaryPath, string normalizedName, CancellationToken cancellationToken);
    ValueTask MarkExternalAsync(string completedPath, Uri source, CancellationToken cancellationToken);
}

public sealed class NoOpReceivedFileSafety : IReceivedFileSafety
{
    public ValueTask InspectAsync(string temporaryPath, string normalizedName, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask MarkExternalAsync(string completedPath, Uri source, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

public sealed class IncomingFileTransferCoordinator : IAsyncDisposable
{
    private readonly Guid sessionId;
    private readonly SessionPeerRole localRole;
    private readonly string destinationRoot;
    private readonly SessionPermissionGate permissions;
    private readonly FileTransferPolicy policy;
    private readonly IStorageSpaceProbe storage;
    private readonly IReceivedFileSafety safety;
    private readonly IDataFeatureAuditSink audit;
    private readonly Dictionary<Guid, IncomingFileTransfer> active = [];
    private readonly SemaphoreSlim gate = new(1, 1);

    public IncomingFileTransferCoordinator(Guid sessionId, SessionPeerRole localRole, string destinationRoot,
        SessionPermissionGate permissions, FileTransferPolicy policy, IStorageSpaceProbe? storage = null,
        IReceivedFileSafety? safety = null, IDataFeatureAuditSink? audit = null)
    {
        if (sessionId == Guid.Empty) throw new ArgumentException("Session ID is required.", nameof(sessionId));
        this.sessionId = sessionId;
        this.localRole = localRole;
        this.destinationRoot = Path.GetFullPath(destinationRoot ?? throw new ArgumentNullException(nameof(destinationRoot)));
        this.permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
        this.storage = storage ?? new DriveStorageSpaceProbe();
        this.safety = safety ?? new NoOpReceivedFileSafety();
        this.audit = audit ?? new NullDataFeatureAuditSink();
        policy.Validate();
    }

    public async Task<FileTransferAcceptance> AcceptAsync(FileTransferManifest manifest, ulong permissionRevision,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ValidateManifest(manifest, permissionRevision, now);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (active.TryGetValue(manifest.TransferId, out IncomingFileTransfer? existing)) return existing.Acceptance;
            if (active.Count >= policy.MaximumConcurrentTransfers)
                throw new DataFeatureException("FILE_POLICY_BLOCKED", "Concurrent transfer limit reached.");
            long available = storage.GetAvailableBytes(destinationRoot);
            if (manifest.Size > (ulong)Math.Max(0, available - policy.RequiredFreeSpaceReserveBytes))
                throw new DataFeatureException("FILE_DISK_SPACE_INSUFFICIENT", "Destination does not have enough free space.");

            string normalized = SafeReceivedFile.NormalizeDisplayName(manifest.DisplayName);
            string extension = Path.GetExtension(normalized);
            if (policy.BlockedExtensions.Contains(extension))
                throw new DataFeatureException("FILE_POLICY_BLOCKED", "File extension is blocked by policy.");
            IncomingFileTransfer transfer = await IncomingFileTransfer.OpenAsync(sessionId, destinationRoot, normalized,
                manifest, policy, safety, audit, now, cancellationToken).ConfigureAwait(false);
            active.Add(manifest.TransferId, transfer);
            await AuditAsync(manifest, normalized, "FILE_TRANSFER_ACCEPTED", now, cancellationToken).ConfigureAwait(false);
            return transfer.Acceptance;
        }
        catch (DataFeatureException exception)
        {
            await AuditAsync(manifest, null, exception.Code.Value, now, cancellationToken).ConfigureAwait(false);
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<FileTransferProgress> ReceiveChunkAsync(FileChunkData chunk, ulong permissionRevision,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!active.TryGetValue(chunk.TransferId, out IncomingFileTransfer? transfer))
                throw new DataFeatureException("FILE_TRANSFER_CANCELLED", "Transfer is not active.");
            permissions.Demand(SessionPermissionGate.FileScope(transfer.Manifest.SourceRole), permissionRevision, "FILE_POLICY_BLOCKED");
            FileTransferProgress progress = await transfer.ReceiveAsync(chunk, now, cancellationToken).ConfigureAwait(false);
            if (progress.Complete)
            {
                active.Remove(chunk.TransferId);
                await transfer.DisposeAsync().ConfigureAwait(false);
            }
            return progress;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task CancelAsync(Guid transferId, string reasonCode, bool deletePartial, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!active.Remove(transferId, out IncomingFileTransfer? transfer)) return;
            await transfer.CancelAsync(deletePartial, cancellationToken).ConfigureAwait(false);
            await AuditAsync(transfer.Manifest, transfer.Acceptance.NormalizedName, reasonCode, now, cancellationToken).ConfigureAwait(false);
            await transfer.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<Guid>> CancelAllAsync(string reasonCode, bool deletePartial, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Guid[] identifiers = active.Keys.ToArray();
            IncomingFileTransfer[] transfers = active.Values.ToArray();
            active.Clear();
            foreach (IncomingFileTransfer transfer in transfers)
            {
                await transfer.CancelAsync(deletePartial, cancellationToken).ConfigureAwait(false);
                await AuditAsync(transfer.Manifest, transfer.Acceptance.NormalizedName, reasonCode, now, cancellationToken).ConfigureAwait(false);
                await transfer.DisposeAsync().ConfigureAwait(false);
            }
            return identifiers;
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (IncomingFileTransfer transfer in active.Values) await transfer.DisposeAsync().ConfigureAwait(false);
            active.Clear();
        }
        finally
        {
            gate.Release();
            gate.Dispose();
        }
    }

    private void ValidateManifest(FileTransferManifest manifest, ulong revision, DateTimeOffset now)
    {
        if (manifest.SourceRole == localRole || manifest.DestinationRole != localRole || manifest.TransferId == Guid.Empty)
            throw new DataFeatureException("FILE_POLICY_BLOCKED", "File direction is invalid.");
        permissions.Demand(SessionPermissionGate.FileScope(manifest.SourceRole), revision, "FILE_POLICY_BLOCKED");
        if (manifest.Size == 0 || manifest.Size > policy.MaximumFileBytes || manifest.Sha256.Length != 32 ||
            manifest.ProposedChunkSize < policy.MinimumChunkBytes || manifest.ProposedChunkSize > policy.MaximumChunkBytes ||
            manifest.ProposedChunkCount != DivideRoundUp(manifest.Size, (ulong)manifest.ProposedChunkSize) ||
            manifest.CreatedAt > now + TimeSpan.FromMinutes(1) || manifest.CreatedAt < now - policy.ResumeLifetime ||
            (manifest.MimeHint?.Length ?? 0) > 255)
            throw new DataFeatureException("FILE_POLICY_BLOCKED", "File manifest is invalid or outside policy.");
    }

    private ValueTask AuditAsync(FileTransferManifest manifest, string? normalized, string result, DateTimeOffset now,
        CancellationToken cancellationToken) => audit.WriteAsync(new DataFeatureAuditRecord(
            "FILE_TRANSFER", sessionId, manifest.TransferId,
            manifest.SourceRole == SessionPeerRole.Host ? "HOST_TO_OPERATOR" : "OPERATOR_TO_HOST",
            normalized, manifest.Size, result, now), cancellationToken);

    private static ulong DivideRoundUp(ulong value, ulong divisor) => value / divisor + (value % divisor == 0 ? 0UL : 1UL);
}

internal sealed class IncomingFileTransfer : IAsyncDisposable
{
    private readonly Guid sessionId;
    private readonly string temporaryPath;
    private readonly string statePath;
    private readonly FileTransferPolicy policy;
    private readonly IReceivedFileSafety safety;
    private readonly IDataFeatureAuditSink audit;
    private readonly FileStream stream;
    private readonly bool[] verified;
    private readonly byte[][] chunkHashes;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private ulong receivedBytes;
    private bool completed;

    private IncomingFileTransfer(Guid sessionId, string temporaryPath, string statePath, FileTransferManifest manifest,
        FileTransferPolicy policy, IReceivedFileSafety safety, IDataFeatureAuditSink audit, FileStream stream,
        bool[] verified, byte[][] chunkHashes, ulong receivedBytes, FileTransferAcceptance acceptance)
    {
        this.sessionId = sessionId;
        this.temporaryPath = temporaryPath;
        this.statePath = statePath;
        Manifest = manifest;
        this.policy = policy;
        this.safety = safety;
        this.audit = audit;
        this.stream = stream;
        this.verified = verified;
        this.chunkHashes = chunkHashes;
        this.receivedBytes = receivedBytes;
        Acceptance = acceptance;
    }

    public FileTransferManifest Manifest { get; }
    public FileTransferAcceptance Acceptance { get; }

    public static async Task<IncomingFileTransfer> OpenAsync(Guid sessionId, string destinationRoot, string normalizedName,
        FileTransferManifest manifest, FileTransferPolicy policy, IReceivedFileSafety safety, IDataFeatureAuditSink audit,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        string sessionShard = Convert.ToHexString(SHA256.HashData(sessionId.ToByteArray()))[..16];
        string partialRoot = Path.Combine(destinationRoot, ".rsp-partials", sessionShard);
        Directory.CreateDirectory(partialRoot);
        string stem = manifest.TransferId.ToString("N");
        string temporaryPath = Path.Combine(partialRoot, stem + ".rsp-part");
        string statePath = Path.Combine(partialRoot, stem + ".rsp-resume.json");
        SafeReceivedFile.EnsureWithinRoot(destinationRoot, temporaryPath);
        string destinationPath;
        bool[] verified = new bool[checked((int)manifest.ProposedChunkCount)];
        byte[][] hashes = new byte[verified.Length][];
        ulong received = 0;
        DateTimeOffset expiresAt = now + policy.ResumeLifetime;

        if (File.Exists(statePath) && File.Exists(temporaryPath))
        {
            ResumeState? state = await ReadStateAsync(statePath, cancellationToken).ConfigureAwait(false);
            if (state is null || state.SessionId != sessionId || state.TransferId != manifest.TransferId ||
                state.Size != manifest.Size || state.ChunkSize != manifest.ProposedChunkSize ||
                state.ExpiresAt <= now || !FixedHash(ParseHash(state.FileSha256), manifest.Sha256) ||
                !string.Equals(state.NormalizedName, normalizedName, StringComparison.Ordinal) ||
                state.Verified.Count != state.ChunkHashes.Count)
            {
                throw new DataFeatureException("FILE_HASH_MISMATCH", "Stored resume state does not match the manifest.");
            }
            destinationPath = Path.Combine(destinationRoot, state.DestinationFileName);
            SafeReceivedFile.EnsureWithinRoot(destinationRoot, destinationPath);
            if (state.Verified.Any(index => index < 0 || index >= verified.Length))
                throw new DataFeatureException("FILE_HASH_MISMATCH", "Stored resume ranges are invalid.");
            await VerifyStoredChunksAsync(temporaryPath, manifest, state, verified, hashes, cancellationToken).ConfigureAwait(false);
            received = state.Verified.Aggregate(0UL, (sum, index) => checked(sum + ExpectedChunkLength(manifest, (ulong)index)));
            expiresAt = state.ExpiresAt;
        }
        else
        {
            destinationPath = SafeReceivedFile.ChooseDestination(destinationRoot, normalizedName);
        }

        FileStream stream = new(temporaryPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
            policy.MaximumChunkBytes, FileOptions.Asynchronous | FileOptions.RandomAccess);
        stream.SetLength(checked((long)manifest.Size));
        FileTransferAcceptance acceptance = new(manifest.TransferId, normalizedName, destinationPath,
            manifest.ProposedChunkSize, ToRanges(verified), policy.MaximumInFlightBytes, expiresAt);
        IncomingFileTransfer transfer = new(sessionId, temporaryPath, statePath, manifest, policy, safety, audit,
            stream, verified, hashes, received, acceptance);
        await transfer.SaveStateAsync(cancellationToken).ConfigureAwait(false);
        return transfer;
    }

    public async Task<FileTransferProgress> ReceiveAsync(FileChunkData chunk, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (completed) throw new DataFeatureException("FILE_TRANSFER_CANCELLED", "Transfer is already complete.");
            if (now > Acceptance.ResumeExpiresAt) throw new DataFeatureException("FILE_TRANSFER_CANCELLED", "Transfer resume grant expired.");
            if (chunk.TransferId != Manifest.TransferId || chunk.ChunkIndex >= (ulong)verified.Length ||
                chunk.Offset != checked(chunk.ChunkIndex * (ulong)Manifest.ProposedChunkSize) ||
                chunk.Data.Length != checked((int)ExpectedChunkLength(Manifest, chunk.ChunkIndex)) || chunk.Sha256.Length != 32)
                throw new DataFeatureException("FILE_HASH_MISMATCH", "File chunk bounds are invalid.");
            byte[] actualHash = SHA256.HashData(chunk.Data);
            if (!CryptographicOperations.FixedTimeEquals(actualHash, chunk.Sha256))
                throw new DataFeatureException("FILE_HASH_MISMATCH", "File chunk hash is invalid.");
            int index = checked((int)chunk.ChunkIndex);
            if (verified[index])
            {
                if (!FixedHash(chunkHashes[index], actualHash))
                    throw new DataFeatureException("FILE_HASH_MISMATCH", "A resumed chunk changed content.");
                return new FileTransferProgress(Manifest.TransferId, receivedBytes, false, null);
            }

            stream.Position = checked((long)chunk.Offset);
            await stream.WriteAsync(chunk.Data, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            verified[index] = true;
            chunkHashes[index] = actualHash;
            receivedBytes = checked(receivedBytes + (ulong)chunk.Data.Length);
            await SaveStateAsync(cancellationToken).ConfigureAwait(false);
            if (verified.Any(value => !value)) return new FileTransferProgress(Manifest.TransferId, receivedBytes, false, null);
            return await CompleteAsync(now, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async Task CancelAsync(bool deletePartial, CancellationToken cancellationToken)
    {
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Dispose();
            if (deletePartial)
            {
                File.Delete(temporaryPath);
                File.Delete(statePath);
            }
        }
        finally
        {
            writeGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        stream.Dispose();
        writeGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<FileTransferProgress> CompleteAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Position = 0;
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        if (!FixedHash(hash, Manifest.Sha256))
        {
            stream.Dispose();
            File.Delete(temporaryPath);
            File.Delete(statePath);
            throw new DataFeatureException("FILE_HASH_MISMATCH", "Final file hash does not match the manifest.");
        }
        await safety.InspectAsync(temporaryPath, Acceptance.NormalizedName, cancellationToken).ConfigureAwait(false);
        stream.Dispose();
        File.Move(temporaryPath, Acceptance.DestinationPath, overwrite: false);
        await safety.MarkExternalAsync(Acceptance.DestinationPath, new Uri("https://remote-support.invalid/session/" + sessionId.ToString("D")),
            cancellationToken).ConfigureAwait(false);
        File.Delete(statePath);
        completed = true;
        await audit.WriteAsync(new DataFeatureAuditRecord("FILE_TRANSFER", sessionId, Manifest.TransferId,
            Manifest.SourceRole == SessionPeerRole.Host ? "HOST_TO_OPERATOR" : "OPERATOR_TO_HOST",
            Acceptance.NormalizedName, Manifest.Size, "COMPLETED", now), cancellationToken).ConfigureAwait(false);
        return new FileTransferProgress(Manifest.TransferId, receivedBytes, true, Acceptance.DestinationPath);
    }

    private async Task SaveStateAsync(CancellationToken cancellationToken)
    {
        List<int> indices = [];
        List<string> hashes = [];
        for (int index = 0; index < verified.Length; index++)
        {
            if (!verified[index]) continue;
            indices.Add(index);
            hashes.Add(Convert.ToHexString(chunkHashes[index]));
        }
        ResumeState state = new(sessionId, Manifest.TransferId, Manifest.Size, Manifest.ProposedChunkSize,
            Convert.ToHexString(Manifest.Sha256), Acceptance.NormalizedName, Path.GetFileName(Acceptance.DestinationPath),
            Acceptance.ResumeExpiresAt, indices, hashes);
        string staging = statePath + ".new";
        await using (FileStream stateStream = new(staging, FileMode.Create, FileAccess.Write, FileShare.None, 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stateStream, state, cancellationToken: cancellationToken).ConfigureAwait(false);
            await stateStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        File.Move(staging, statePath, overwrite: true);
    }

    private static async Task<ResumeState?> ReadStateAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<ResumeState>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task VerifyStoredChunksAsync(string path, FileTransferManifest manifest, ResumeState state,
        bool[] verified, byte[][] hashes, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, manifest.ProposedChunkSize,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        for (int item = 0; item < state.Verified.Count; item++)
        {
            int index = state.Verified[item];
            byte[] expected;
            try { expected = Convert.FromHexString(state.ChunkHashes[item]); }
            catch (FormatException) { throw new DataFeatureException("FILE_HASH_MISMATCH", "Stored chunk hash is invalid."); }
            int length = checked((int)ExpectedChunkLength(manifest, (ulong)index));
            byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                stream.Position = checked((long)((ulong)index * (ulong)manifest.ProposedChunkSize));
                await stream.ReadExactlyAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                byte[] actual = SHA256.HashData(buffer.AsSpan(0, length));
                if (!FixedHash(actual, expected)) throw new DataFeatureException("FILE_HASH_MISMATCH", "Stored chunk was modified.");
                verified[index] = true;
                hashes[index] = expected;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(buffer.AsSpan(0, length));
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private static ulong ExpectedChunkLength(FileTransferManifest manifest, ulong index)
    {
        ulong offset = checked(index * (ulong)manifest.ProposedChunkSize);
        return Math.Min((ulong)manifest.ProposedChunkSize, manifest.Size - offset);
    }

    private static List<VerifiedChunkRange> ToRanges(bool[] verified)
    {
        List<VerifiedChunkRange> ranges = [];
        int index = 0;
        while (index < verified.Length)
        {
            if (!verified[index]) { index++; continue; }
            int start = index;
            while (index < verified.Length && verified[index]) index++;
            ranges.Add(new VerifiedChunkRange((ulong)start, (ulong)index));
        }
        return ranges;
    }

    private static bool FixedHash(byte[]? left, byte[]? right) => left is { Length: 32 } && right is { Length: 32 } &&
        CryptographicOperations.FixedTimeEquals(left, right);

    private static byte[]? ParseHash(string value)
    {
        try { return Convert.FromHexString(value); }
        catch (FormatException) { return null; }
    }

    private sealed record ResumeState(Guid SessionId, Guid TransferId, ulong Size, int ChunkSize,
        string FileSha256, string NormalizedName, string DestinationFileName, DateTimeOffset ExpiresAt,
        List<int> Verified, List<string> ChunkHashes);
}

public interface IFileSendBackpressure
{
    ValueTask WaitForCapacityAsync(int nextChunkBytes, CancellationToken cancellationToken);
}

public sealed class FileChunkReader(FileTransferManifest manifest, string sourcePath, IFileSendBackpressure backpressure,
    SessionPermissionGate permissions)
{
    public async IAsyncEnumerable<FileChunkData> ReadAsync(IReadOnlySet<ulong>? alreadyVerified = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using FileStream stream = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            manifest.ProposedChunkSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if ((ulong)stream.Length != manifest.Size) throw new DataFeatureException("FILE_HASH_MISMATCH", "Source file size changed.");
        byte[] buffer = ArrayPool<byte>.Shared.Rent(manifest.ProposedChunkSize);
        try
        {
            for (ulong index = 0; index < manifest.ProposedChunkCount; index++)
            {
                int length = checked((int)Math.Min((ulong)manifest.ProposedChunkSize,
                    manifest.Size - checked(index * (ulong)manifest.ProposedChunkSize)));
                await stream.ReadExactlyAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                if (alreadyVerified?.Contains(index) == true) continue;
                PermissionSnapshot snapshot = permissions.Current;
                permissions.Demand(SessionPermissionGate.FileScope(manifest.SourceRole), snapshot.Revision, "FILE_POLICY_BLOCKED");
                await backpressure.WaitForCapacityAsync(length, cancellationToken).ConfigureAwait(false);
                byte[] data = buffer.AsSpan(0, length).ToArray();
                yield return new FileChunkData(manifest.TransferId, index,
                    checked(index * (ulong)manifest.ProposedChunkSize), data, SHA256.HashData(data));
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
