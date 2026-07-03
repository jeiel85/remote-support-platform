using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using NSec.Cryptography;
using RemoteSupport.Security;

namespace RemoteSupport.UnitTests;

public sealed class UpdateMetadataVerifierTests
{
    [Fact]
    public void SignedRootAndManifestAreBoundToProductArchitectureAndSequence()
    {
        SignatureAlgorithm algorithm = SignatureAlgorithm.Ed25519;
        KeyCreationParameters creation = new() { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
        using Key rootKey = Key.Create(algorithm, creation);
        using Key targetKey = Key.Create(algorithm, creation);
        string rootId = "root-key-1";
        string targetId = "target-key-1";
        DateTimeOffset now = new(2026, 7, 2, 0, 0, 0, TimeSpan.Zero);
        JsonElement rootSigned = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1, rootVersion = 1, issuedAt = now.AddMinutes(-1), expiresAt = now.AddDays(30),
            consistentSnapshot = true,
            keys = new Dictionary<string, object>
            {
                [rootId] = new { algorithm = "ed25519", publicKey = Base64Url(rootKey.PublicKey.Export(KeyBlobFormat.RawPublicKey)) },
                [targetId] = new { algorithm = "ed25519", publicKey = Base64Url(targetKey.PublicKey.Export(KeyBlobFormat.RawPublicKey)) },
            },
            roles = new
            {
                root = new { keyIds = new[] { rootId }, threshold = 1 },
                targets = new { keyIds = new[] { targetId }, threshold = 1 },
            },
        });
        byte[] root = Document(rootSigned, rootId, rootKey, "RSP-UPDATE-ROOT-V1");
        UpdateTrustStore trust = UpdateTrustStore.LoadBootstrap(root, now);

        JsonElement manifestSigned = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1, product = "OPERATOR_CONSOLE", channel = "internal", version = "0.9.1",
            releaseSequence = 900002, minimumAllowedSequence = 900000, issuedAt = now.AddMinutes(-1), expiresAt = now.AddDays(2),
            rollout = new { percentage = 100, seedVersion = 1 },
            artifacts = new[]
            {
                new { architecture = "x64", packageType = "OPERATOR_INSTALLER", url = "https://updates.example/operator.exe",
                    size = 12345, sha256 = new string('a', 64), authenticodeSignerThumbprint = new string('B', 40), minimumOsBuild = "10.0.19045" },
            },
            rootVersion = 1,
        });
        byte[] manifest = Document(manifestSigned, targetId, targetKey, "RSP-UPDATE-MANIFEST-V1");
        VerifiedUpdateManifest verified = trust.VerifyManifest(manifest, "OPERATOR_CONSOLE", "internal", "x64",
            900001, "installation-1", now);
        Assert.Equal(900002, verified.ReleaseSequence);
        Assert.Equal("OPERATOR_INSTALLER", verified.Artifact.PackageType);
        Assert.Equal("UPDATE_ROLLBACK_BLOCKED", Assert.Throws<UpdateSecurityException>(() => trust.VerifyManifest(
            manifest, "OPERATOR_CONSOLE", "internal", "x64", 900002, "installation-1", now)).Code);

        byte[] tampered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(manifest).Replace("OPERATOR_CONSOLE", "PORTABLE_AGENT", StringComparison.Ordinal));
        Assert.Equal("UPDATE_SIGNATURE_INVALID", Assert.Throws<UpdateSecurityException>(() => trust.VerifyManifest(
            tampered, "PORTABLE_AGENT", "internal", "x64", 1, "installation-1", now)).Code);
    }

    private static byte[] Document(JsonElement signed, string keyId, Key key, string domain)
    {
        byte[] canonical = Canonical(signed);
        byte[] prefix = Encoding.ASCII.GetBytes(domain + "\0");
        byte[] message = [.. prefix, .. canonical];
        byte[] signature = SignatureAlgorithm.Ed25519.Sign(key, message);
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            signed,
            signatures = new[] { new { keyId, algorithm = "ed25519", signature = Base64Url(signature) } },
        });
    }

    private static byte[] Canonical(JsonElement value)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        Write(writer, value);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                { writer.WritePropertyName(property.Name); Write(writer, property.Value); }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                writer.WriteStartArray(); foreach (JsonElement item in value.EnumerateArray()) Write(writer, item); writer.WriteEndArray(); break;
            case JsonValueKind.String: writer.WriteStringValue(value.GetString()); break;
            case JsonValueKind.Number: writer.WriteNumberValue(value.GetInt64()); break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
        }
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
