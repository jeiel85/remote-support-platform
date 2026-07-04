using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;

namespace RemoteSupport.Security;

public sealed record UpdateSigningKeyPair(byte[] PrivateKey, byte[] PublicKey);

public static class UpdateMetadataSigner
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static UpdateSigningKeyPair GenerateKeyPair()
    {
        KeyCreationParameters parameters = new() { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
        using Key key = Key.Create(SignatureAlgorithm.Ed25519, parameters);
        return new UpdateSigningKeyPair(key.Export(KeyBlobFormat.RawPrivateKey),
            key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
    }

    public static byte[] Sign(ReadOnlySpan<byte> signedJson, string keyId, ReadOnlySpan<byte> privateKey,
        string documentType)
    {
        if (string.IsNullOrWhiteSpace(keyId) || keyId.Length > 128 || keyId.Any(character => character > 0x7f))
            throw new ArgumentException("Update key ID is invalid.", nameof(keyId));
        string domain = documentType switch
        {
            "root" => "RSP-UPDATE-ROOT-V1",
            "manifest" => "RSP-UPDATE-MANIFEST-V1",
            _ => throw new ArgumentException("Document type must be root or manifest.", nameof(documentType)),
        };
        using JsonDocument input = JsonDocument.Parse(signedJson.ToArray(), new JsonDocumentOptions
        { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 32 });
        JsonElement signed = input.RootElement.Clone();
        byte[] canonical = UpdateTrustStore.Canonicalize(signed);
        byte[] prefix = Encoding.ASCII.GetBytes(domain + "\0");
        byte[] message = [.. prefix, .. canonical];
        byte[] secret = privateKey.ToArray();
        try
        {
            using Key key = Key.Import(SignatureAlgorithm.Ed25519, secret, KeyBlobFormat.RawPrivateKey,
                new KeyCreationParameters { ExportPolicy = KeyExportPolicies.None });
            byte[] signature = SignatureAlgorithm.Ed25519.Sign(key, message);
            return JsonSerializer.SerializeToUtf8Bytes(new
            {
                signed,
                signatures = new[]
                {
                    new { keyId, algorithm = "ed25519", signature = Base64Url(signature) },
                },
            }, Json);
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    public static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] DecodeBase64Url(string value) => UpdateTrustStore.Decode(value);
}
