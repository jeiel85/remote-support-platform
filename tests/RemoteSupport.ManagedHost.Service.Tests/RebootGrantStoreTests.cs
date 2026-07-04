using RemoteSupport.ManagedHost.Service;

namespace RemoteSupport.ManagedHost.Service.Tests;

public sealed class RebootGrantStoreTests
{
    [Fact]
    public void SavedGrantRoundTripsAndIsNotStoredAsPlaintextOnDisk()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rsp-reboot-grant-{Guid.NewGuid():N}.bin");
        try
        {
            RebootGrantStore store = new(path);
            RebootGrant grant = new("session-1", [1, 2, 3, 4], DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds(),
                [5, 6, 7, 8], "device-1", "operator-1");
            store.Save(grant);

            byte[] onDisk = File.ReadAllBytes(path);
            string onDiskText = System.Text.Encoding.UTF8.GetString(onDisk);
            Assert.DoesNotContain("sessionId", onDiskText, StringComparison.Ordinal);
            Assert.DoesNotContain(grant.SessionId, onDiskText, StringComparison.Ordinal);
            RebootGrant? loaded = store.TryLoad(DateTimeOffset.UtcNow);
            Assert.NotNull(loaded);
            Assert.Equal(grant.SessionId, loaded!.SessionId);
            Assert.Equal(grant.EncryptedGrant, loaded.EncryptedGrant);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExpiredGrantIsClearedAndReturnsNull()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rsp-reboot-grant-{Guid.NewGuid():N}.bin");
        try
        {
            RebootGrantStore store = new(path);
            RebootGrant grant = new("session-2", [1], DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeMilliseconds(), [2], "device-2", "operator-2");
            store.Save(grant);
            Assert.Null(store.TryLoad(DateTimeOffset.UtcNow));
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MissingFileReturnsNull()
    {
        RebootGrantStore store = new(Path.Combine(Path.GetTempPath(), $"rsp-missing-{Guid.NewGuid():N}.bin"));
        Assert.Null(store.TryLoad(DateTimeOffset.UtcNow));
    }
}
