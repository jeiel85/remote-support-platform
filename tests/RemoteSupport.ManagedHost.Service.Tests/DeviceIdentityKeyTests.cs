using RemoteSupport.ManagedHost.Service;

namespace RemoteSupport.ManagedHost.Service.Tests;

public sealed class DeviceIdentityKeyTests
{
    [Fact]
    public void CreatedKeySignsVerifiableDataAndReportsAStableThumbprint()
    {
        string keyName = $"rsp-test-key-{Guid.NewGuid():N}";
        using CngDeviceIdentityKey key = CngDeviceIdentityKey.OpenOrCreate(keyName, machineScoped: false);
        try
        {
            byte[] data = "sign-me"u8.ToArray();
            string signature = key.Sign(data);
            Assert.False(string.IsNullOrEmpty(signature));
            Assert.Equal(CngDeviceIdentityKey.ComputeThumbprint(key.PublicJwk), key.Thumbprint);
            Assert.Equal("EC", key.PublicJwk.GetProperty("kty").GetString());
            Assert.Equal("P-256", key.PublicJwk.GetProperty("crv").GetString());
        }
        finally
        {
            key.Delete();
        }
    }

    [Fact]
    public void OpenOrCreateReusesThePersistedKeyOnASecondCall()
    {
        string keyName = $"rsp-test-key-{Guid.NewGuid():N}";
        using CngDeviceIdentityKey first = CngDeviceIdentityKey.OpenOrCreate(keyName, machineScoped: false);
        try
        {
            using CngDeviceIdentityKey second = CngDeviceIdentityKey.OpenOrCreate(keyName, machineScoped: false);
            Assert.Equal(first.Thumbprint, second.Thumbprint);
        }
        finally
        {
            first.Delete();
        }
    }

    [Fact]
    public void EphemeralKeysAreIndependentAndDeletable()
    {
        using CngDeviceIdentityKey first = CngDeviceIdentityKey.CreateEphemeral();
        using CngDeviceIdentityKey second = CngDeviceIdentityKey.CreateEphemeral();
        Assert.NotEqual(first.Thumbprint, second.Thumbprint);
        first.Delete();
        second.Delete();
    }
}
