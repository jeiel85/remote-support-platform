using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace RemoteSupport.ManagedHost.Service;

public sealed record RebootGrant(string SessionId, byte[] EncryptedGrant, long ExpiresUtcUnixMs, byte[] GrantHash,
    string DeviceId, string OperatorId);

/// <summary>
/// Persists the Service's StoreRebootGrant payload (service_ipc.proto) across a machine
/// restart using machine-scoped DPAPI, so no reusable peer/device secret is ever written to
/// disk in plaintext (07-delivery/goals/goal-13-managed-host.md: "without persisting reusable
/// peer/device access tokens or peer private keys across reboot"). The grant itself is already
/// an opaque, server-issued encrypted blob; this store only protects it at rest locally and
/// enforces the expiry before returning it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RebootGrantStore(string statePath)
{
    private static readonly byte[] Entropy = "RSP-REBOOT-GRANT-V1"u8.ToArray();

    public void Save(RebootGrant grant)
    {
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(grant, ManagedHostJson.Options);
        byte[] protectedBytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.LocalMachine);
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        string temporary = statePath + ".new";
        File.WriteAllBytes(temporary, protectedBytes);
        File.Move(temporary, statePath, overwrite: true);
    }

    public RebootGrant? TryLoad(DateTimeOffset now)
    {
        if (!File.Exists(statePath)) return null;
        byte[] protectedBytes = File.ReadAllBytes(statePath);
        byte[] plaintext = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.LocalMachine);
        RebootGrant grant = JsonSerializer.Deserialize<RebootGrant>(plaintext, ManagedHostJson.Options)
            ?? throw new InvalidDataException("Stored reboot grant was invalid.");
        if (grant.ExpiresUtcUnixMs <= now.ToUnixTimeMilliseconds())
        {
            Clear();
            return null;
        }
        return grant;
    }

    public void Clear()
    {
        if (File.Exists(statePath)) File.Delete(statePath);
    }
}
