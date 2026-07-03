using System.Security.Cryptography;
using RemoteSupport.Application;
using RemoteSupport.Domain;

namespace RemoteSupport.UnitTests;

public sealed class FileTransferTests
{
    public static TheoryData<string> UnsafeNames => new()
    {
        "../escape.txt", "..\\escape.txt", "C:\\escape.txt", "file.txt:evil", "CON", "NUL.txt",
        "folder/file.txt", "folder\\file.txt", ".", "..", "bad\0name",
    };

    [Theory]
    [MemberData(nameof(UnsafeNames))]
    public void UnsafeOrReservedNamesAreRejected(string name)
    {
        DataFeatureException exception = Assert.Throws<DataFeatureException>(() => SafeReceivedFile.NormalizeDisplayName(name));
        Assert.Equal("FILE_PATH_INVALID", exception.Code.Value);
    }

    [Fact]
    public void UnicodeNameIsNormalizedWithoutAcceptingAPath()
    {
        Assert.Equal("Café.txt", SafeReceivedFile.NormalizeDisplayName("Cafe\u0301.txt"));
    }

    [Fact]
    public async Task TransferCompletesAtomicallyAndInvokesSafetyHooks()
    {
        using TemporaryDirectory directory = new();
        byte[] content = Enumerable.Range(0, 100_000).Select(value => (byte)(value % 251)).ToArray();
        FileTransferManifest manifest = Manifest(content, 32 * 1024);
        FakeSafety safety = new();
        SessionPermissionGate permissions = FilePermissions();
        await using IncomingFileTransferCoordinator receiver = new(Guid.NewGuid(), SessionPeerRole.Host, directory.Path,
            permissions, Policy(), safety: safety);
        FileTransferAcceptance acceptance = await receiver.AcceptAsync(manifest, 1, DateTimeOffset.UtcNow);
        Assert.DoesNotContain("..", Path.GetRelativePath(directory.Path, acceptance.DestinationPath), StringComparison.Ordinal);

        foreach (FileChunkData chunk in Chunks(manifest, content))
        {
            await receiver.ReceiveChunkAsync(chunk, 1, DateTimeOffset.UtcNow);
        }

        Assert.True(File.Exists(acceptance.DestinationPath));
        Assert.Equal(content, await File.ReadAllBytesAsync(acceptance.DestinationPath));
        Assert.Equal(1, safety.Inspected);
        Assert.Equal(1, safety.Marked);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.rsp-part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task FinalHashMismatchNeverCreatesDestinationFile()
    {
        using TemporaryDirectory directory = new();
        byte[] content = new byte[40_000];
        RandomNumberGenerator.Fill(content);
        FileTransferManifest manifest = new(Guid.NewGuid(), "document.txt", (ulong)content.Length, new byte[32], null,
            16 * 1024, 3, SessionPeerRole.Operator, SessionPeerRole.Host, DateTimeOffset.UtcNow);
        await using IncomingFileTransferCoordinator receiver = new(Guid.NewGuid(), SessionPeerRole.Host, directory.Path,
            FilePermissions(), Policy());
        FileTransferAcceptance acceptance = await receiver.AcceptAsync(manifest, 1, DateTimeOffset.UtcNow);

        DataFeatureException? failure = null;
        foreach (FileChunkData chunk in Chunks(manifest, content))
        {
            try { await receiver.ReceiveChunkAsync(chunk, 1, DateTimeOffset.UtcNow); }
            catch (DataFeatureException exception) { failure = exception; }
        }
        Assert.NotNull(failure);
        Assert.Equal("FILE_HASH_MISMATCH", failure.Code.Value);
        Assert.False(File.Exists(acceptance.DestinationPath));
    }

    [Fact]
    public async Task ResumeReusesOnlyVerifiedMatchingChunks()
    {
        using TemporaryDirectory directory = new();
        byte[] content = new byte[70_000];
        RandomNumberGenerator.Fill(content);
        FileTransferManifest manifest = Manifest(content, 16 * 1024);
        Guid sessionId = Guid.NewGuid();
        FileChunkData[] chunks = Chunks(manifest, content).ToArray();
        await using (IncomingFileTransferCoordinator first = new(sessionId, SessionPeerRole.Host, directory.Path,
            FilePermissions(), Policy()))
        {
            await first.AcceptAsync(manifest, 1, DateTimeOffset.UtcNow);
            await first.ReceiveChunkAsync(chunks[0], 1, DateTimeOffset.UtcNow);
        }

        await using IncomingFileTransferCoordinator resumed = new(sessionId, SessionPeerRole.Host, directory.Path,
            FilePermissions(), Policy());
        FileTransferAcceptance acceptance = await resumed.AcceptAsync(manifest, 1, DateTimeOffset.UtcNow);
        Assert.Contains(acceptance.AlreadyVerified, range => range.StartInclusive == 0 && range.EndExclusive == 1);
        foreach (FileChunkData chunk in chunks[1..]) await resumed.ReceiveChunkAsync(chunk, 1, DateTimeOffset.UtcNow);
        Assert.Equal(content, await File.ReadAllBytesAsync(acceptance.DestinationPath));
    }

    [Fact]
    public async Task PermissionRevocationStopsAnInFlightTransfer()
    {
        using TemporaryDirectory directory = new();
        byte[] content = new byte[50_000];
        FileTransferManifest manifest = Manifest(content, 16 * 1024);
        SessionPermissionGate permissions = FilePermissions();
        await using IncomingFileTransferCoordinator receiver = new(Guid.NewGuid(), SessionPeerRole.Host, directory.Path,
            permissions, Policy());
        await receiver.AcceptAsync(manifest, 1, DateTimeOffset.UtcNow);
        permissions.Replace(2, ScopeSet.Empty);

        DataFeatureException exception = await Assert.ThrowsAsync<DataFeatureException>(() =>
            receiver.ReceiveChunkAsync(Chunks(manifest, content).First(), 2, DateTimeOffset.UtcNow));
        Assert.Equal("FILE_POLICY_BLOCKED", exception.Code.Value);
    }

    [Fact]
    public async Task CancelAllStopsActiveTransferAndPreservesResumeStateByPolicy()
    {
        using TemporaryDirectory directory = new();
        byte[] content = new byte[50_000];
        RandomNumberGenerator.Fill(content);
        FileTransferManifest manifest = Manifest(content, 16 * 1024);
        await using IncomingFileTransferCoordinator receiver = new(Guid.NewGuid(), SessionPeerRole.Host, directory.Path,
            FilePermissions(), Policy());
        await receiver.AcceptAsync(manifest, 1, DateTimeOffset.UtcNow);
        FileChunkData[] chunks = Chunks(manifest, content).ToArray();
        await receiver.ReceiveChunkAsync(chunks[0], 1, DateTimeOffset.UtcNow);

        IReadOnlyList<Guid> cancelled = await receiver.CancelAllAsync("FILE_TRANSFER_CANCELLED", false, DateTimeOffset.UtcNow);

        Assert.Equal([manifest.TransferId], cancelled);
        Assert.NotEmpty(Directory.GetFiles(directory.Path, "*.rsp-part", SearchOption.AllDirectories));
        DataFeatureException exception = await Assert.ThrowsAsync<DataFeatureException>(() =>
            receiver.ReceiveChunkAsync(chunks[1], 1, DateTimeOffset.UtcNow));
        Assert.Equal("FILE_TRANSFER_CANCELLED", exception.Code.Value);
    }

    [Fact]
    public async Task SenderHonorsBackpressureBeforeEachBoundedChunk()
    {
        using TemporaryDirectory directory = new();
        string source = Path.Combine(directory.Path, "source.bin");
        byte[] content = new byte[90_000];
        RandomNumberGenerator.Fill(content);
        await File.WriteAllBytesAsync(source, content);
        FileTransferManifest manifest = Manifest(content, 16 * 1024);
        CountingBackpressure pressure = new();
        FileChunkReader reader = new(manifest, source, pressure, FilePermissions());
        int count = 0;
        await foreach (FileChunkData chunk in reader.ReadAsync())
        {
            Assert.True(chunk.Data.Length <= 16 * 1024);
            count++;
        }
        Assert.Equal(count, pressure.WaitCount);
        Assert.Equal(checked((int)manifest.ProposedChunkCount), count);
    }

    private static FileTransferManifest Manifest(byte[] content, int chunkSize) => new(Guid.NewGuid(), "report.txt",
        (ulong)content.Length, SHA256.HashData(content), "text/plain", chunkSize,
        (ulong)(content.Length / chunkSize + (content.Length % chunkSize == 0 ? 0 : 1)),
        SessionPeerRole.Operator, SessionPeerRole.Host, DateTimeOffset.UtcNow);

    private static IEnumerable<FileChunkData> Chunks(FileTransferManifest manifest, byte[] content)
    {
        for (ulong index = 0; index < manifest.ProposedChunkCount; index++)
        {
            int offset = checked((int)(index * (ulong)manifest.ProposedChunkSize));
            int length = Math.Min(manifest.ProposedChunkSize, content.Length - offset);
            byte[] data = content.AsSpan(offset, length).ToArray();
            yield return new FileChunkData(manifest.TransferId, index, (ulong)offset, data, SHA256.HashData(data));
        }
    }

    private static SessionPermissionGate FilePermissions() => new(1,
        ScopeSet.From(CapabilityScope.TransferFileOperatorToHost));

    private static FileTransferPolicy Policy() => new()
    {
        MaximumFileBytes = 10 * 1024 * 1024,
        RequiredFreeSpaceReserveBytes = 0,
        BlockedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe" },
    };

    private sealed class FakeSafety : IReceivedFileSafety
    {
        public int Inspected { get; private set; }
        public int Marked { get; private set; }
        public ValueTask InspectAsync(string temporaryPath, string normalizedName, CancellationToken cancellationToken)
        { Inspected++; return ValueTask.CompletedTask; }
        public ValueTask MarkExternalAsync(string completedPath, Uri source, CancellationToken cancellationToken)
        { Marked++; return ValueTask.CompletedTask; }
    }

    private sealed class CountingBackpressure : IFileSendBackpressure
    {
        public int WaitCount { get; private set; }
        public ValueTask WaitForCapacityAsync(int nextChunkBytes, CancellationToken cancellationToken)
        { WaitCount++; return ValueTask.CompletedTask; }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rsp-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
