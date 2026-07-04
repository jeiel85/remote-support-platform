using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

namespace RemoteSupport.ManagedHost.Service;

/// <summary>
/// A device identity key usable for enrollment proof, credential-refresh challenges and
/// managed-host decision signing. 05-security/identity-and-access.md requires devices to
/// prove key possession; 07-delivery/goals/goal-13-managed-host.md requires a non-exportable
/// device key where the platform supports one.
/// </summary>
public interface IDeviceIdentityKey : IDisposable
{
    JsonElement PublicJwk { get; }
    string Thumbprint { get; }
    string Sign(byte[] data);
}

/// <summary>
/// Windows CNG-backed P-256 key. Uses machine-scoped, non-exportable key storage so the
/// private key material never leaves the protected key store, per the Service's
/// "installation identity and device key access" responsibility in
/// 03-client/windows-service.md.
/// </summary>
public sealed class CngDeviceIdentityKey : IDeviceIdentityKey
{
    private static readonly BigInteger Order = BigInteger.Parse(
        "00FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551",
        System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
    private static readonly BigInteger HalfOrder = Order / 2;

    private readonly CngKey key;
    private readonly bool ownsKey;

    private CngDeviceIdentityKey(CngKey key, bool ownsKey)
    {
        this.key = key;
        this.ownsKey = ownsKey;
        using ECDsaCng ecdsa = new(key);
        ECParameters parameters = ecdsa.ExportParameters(includePrivateParameters: false);
        PublicJwk = JsonSerializer.SerializeToElement(new
        {
            kty = "EC",
            crv = "P-256",
            x = Base64UrlEncode(parameters.Q.X!),
            y = Base64UrlEncode(parameters.Q.Y!),
        });
        Thumbprint = ComputeThumbprint(PublicJwk);
    }

    public JsonElement PublicJwk { get; }
    public string Thumbprint { get; }

    /// <summary>Opens the persisted key, or creates a new non-exportable one if absent. Production
    /// callers (the elevated Service process) use the default machine-scoped storage; tests use
    /// user-scoped storage since creating a machine key requires administrative privileges.</summary>
    public static CngDeviceIdentityKey OpenOrCreate(string keyName, bool machineScoped = true)
    {
        CngKeyOpenOptions openOptions = machineScoped ? CngKeyOpenOptions.MachineKey : CngKeyOpenOptions.None;
        if (CngKey.Exists(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, openOptions))
        {
            return new CngDeviceIdentityKey(CngKey.Open(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider,
                openOptions), ownsKey: true);
        }
        CngKeyCreationParameters creationParameters = new()
        {
            Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
            KeyCreationOptions = machineScoped ? CngKeyCreationOptions.MachineKey : CngKeyCreationOptions.None,
            ExportPolicy = CngExportPolicies.None,
            KeyUsage = CngKeyUsages.Signing,
        };
        return new CngDeviceIdentityKey(CngKey.Create(CngAlgorithm.ECDsaP256, keyName, creationParameters), ownsKey: true);
    }

    /// <summary>Wraps an already-open key without taking ownership of its lifetime (used for rotation
    /// where a temporary new key is validated before being persisted under its final name).</summary>
    public static CngDeviceIdentityKey CreateEphemeral()
    {
        CngKeyCreationParameters creationParameters = new()
        {
            Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
            ExportPolicy = CngExportPolicies.None,
            KeyUsage = CngKeyUsages.Signing,
        };
        return new CngDeviceIdentityKey(CngKey.Create(CngAlgorithm.ECDsaP256, keyName: null, creationParameters), ownsKey: true);
    }

    public string Sign(byte[] data)
    {
        using ECDsaCng ecdsa = new(key);
        byte[] signature = ecdsa.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        BigInteger s = new(signature.AsSpan(32, 32), isUnsigned: true, isBigEndian: true);
        if (s > HalfOrder)
        {
            s = Order - s;
            byte[] normalized = s.ToByteArray(isUnsigned: true, isBigEndian: true);
            signature.AsSpan(32, 32).Clear();
            normalized.CopyTo(signature.AsSpan(64 - normalized.Length));
        }
        return Base64UrlEncode(signature);
    }

    /// <summary>Deletes the persisted key material. Used when retiring a rotated-out key
    /// or tearing down a test fixture.</summary>
    public void Delete() => key.Delete();

    public void Dispose()
    {
        if (ownsKey) key.Dispose();
    }

    public static string ComputeThumbprint(JsonElement jwk)
    {
        string x = jwk.GetProperty("x").GetString()!;
        string y = jwk.GetProperty("y").GetString()!;
        string canonical = $$"""{"crv":"P-256","kty":"EC","x":"{{x}}","y":"{{y}}"}""";
        return Base64UrlEncode(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)));
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
