using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using NSec.Cryptography;

namespace RemoteSupport.Security;

public sealed class UpdateSecurityException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record UpdateArtifact(string Architecture, string PackageType, Uri Url, long Size, string Sha256,
    string AuthenticodeSignerThumbprint, string? MinimumOsBuild);
public sealed record VerifiedUpdateManifest(string Product, string Channel, string Version, long ReleaseSequence,
    long MinimumAllowedSequence, int RootVersion, DateTimeOffset ExpiresAt, UpdateArtifact Artifact);

public sealed class UpdateTrustStore
{
    private UpdateRoot root;

    private UpdateTrustStore(UpdateRoot root) => this.root = root;
    public int RootVersion => root.Version;

    public static UpdateTrustStore LoadBootstrap(ReadOnlySpan<byte> utf8, DateTimeOffset now)
    {
        ParsedSignedDocument parsed = Parse(utf8, "RSP-UPDATE-ROOT-V1");
        UpdateRoot candidate = UpdateRoot.Parse(parsed.Signed);
        candidate.Validate(now);
        VerifyThreshold(parsed, candidate.Keys, candidate.RootRole, "UPDATE_ROOT_INVALID");
        return new UpdateTrustStore(candidate);
    }

    public void Rotate(ReadOnlySpan<byte> utf8, DateTimeOffset now)
    {
        ParsedSignedDocument parsed = Parse(utf8, "RSP-UPDATE-ROOT-V1");
        UpdateRoot candidate = UpdateRoot.Parse(parsed.Signed);
        candidate.Validate(now);
        if (candidate.Version != root.Version + 1)
            throw Invalid("UPDATE_ROOT_SEQUENCE_REQUIRED", "Update roots must rotate sequentially.");
        VerifyThreshold(parsed, root.Keys, root.RootRole, "UPDATE_ROOT_INVALID");
        VerifyThreshold(parsed, candidate.Keys, candidate.RootRole, "UPDATE_ROOT_INVALID");
        root = candidate;
    }

    public VerifiedUpdateManifest VerifyManifest(ReadOnlySpan<byte> utf8, string product, string channel,
        string architecture, long installedSequence, string rolloutIdentity, DateTimeOffset now)
    {
        ParsedSignedDocument parsed = Parse(utf8, "RSP-UPDATE-MANIFEST-V1");
        VerifyThreshold(parsed, root.Keys, root.TargetsRole, "UPDATE_SIGNATURE_INVALID");
        JsonElement value = parsed.Signed;
        Exact(value, ["schemaVersion", "product", "channel", "version", "releaseSequence", "minimumAllowedSequence",
            "issuedAt", "expiresAt", "artifacts", "rootVersion"], ["rollout", "releaseNotesUrl"]);
        if (value.GetProperty("schemaVersion").GetInt32() != 1 || value.GetProperty("rootVersion").GetInt32() != root.Version ||
            value.GetProperty("product").GetString() != product || value.GetProperty("channel").GetString() != channel)
            throw Invalid("UPDATE_SIGNATURE_INVALID", "Update manifest binding does not match this product.");
        long sequence = value.GetProperty("releaseSequence").GetInt64();
        long minimum = value.GetProperty("minimumAllowedSequence").GetInt64();
        DateTimeOffset issued = value.GetProperty("issuedAt").GetDateTimeOffset();
        DateTimeOffset expires = value.GetProperty("expiresAt").GetDateTimeOffset();
        if (sequence <= installedSequence || sequence < minimum || issued > now + TimeSpan.FromMinutes(5) || expires <= now ||
            expires - issued > TimeSpan.FromDays(31))
            throw Invalid(sequence <= installedSequence ? "UPDATE_ROLLBACK_BLOCKED" : "UPDATE_METADATA_EXPIRED",
                "Update sequence or validity window is invalid.");
        if (value.TryGetProperty("rollout", out JsonElement rollout) && !Eligible(rollout, rolloutIdentity))
            throw Invalid("UPDATE_ROLLOUT_INELIGIBLE", "This installation is not in the staged rollout cohort.");
        UpdateArtifact[] matches = value.GetProperty("artifacts").EnumerateArray().Select(UpdateArtifactFrom)
            .Where(artifact => artifact.Architecture == architecture).ToArray();
        if (matches.Length != 1) throw Invalid("UPDATE_SIGNATURE_INVALID", "Manifest does not contain one artifact for this architecture.");
        return new VerifiedUpdateManifest(product, channel, value.GetProperty("version").GetString()!, sequence, minimum,
            root.Version, expires, matches[0]);
    }

    private static bool Eligible(JsonElement rollout, string identity)
    {
        Exact(rollout, ["percentage", "seedVersion"], []);
        int percentage = rollout.GetProperty("percentage").GetInt32();
        int seed = rollout.GetProperty("seedVersion").GetInt32();
        if (percentage is < 0 or > 100 || seed < 1 || string.IsNullOrWhiteSpace(identity)) return false;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"RSP-ROLLOUT-V1\0{seed}\0{identity}"));
        uint bucket = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(hash);
        return bucket % 100 < percentage;
    }

    private static UpdateArtifact UpdateArtifactFrom(JsonElement value)
    {
        Exact(value, ["architecture", "packageType", "url", "size", "sha256", "authenticodeSignerThumbprint"],
            ["minimumOsBuild"]);
        Uri url = new(value.GetProperty("url").GetString()!, UriKind.Absolute);
        string hash = value.GetProperty("sha256").GetString()!;
        string signer = value.GetProperty("authenticodeSignerThumbprint").GetString()!;
        long size = value.GetProperty("size").GetInt64();
        if (url.Scheme != Uri.UriSchemeHttps || size <= 0 || hash.Length != 64 || !hash.All(Uri.IsHexDigit) ||
            signer.Length is < 40 or > 128 || !signer.All(Uri.IsHexDigit))
            throw Invalid("UPDATE_SIGNATURE_INVALID", "Update artifact metadata is invalid.");
        return new UpdateArtifact(value.GetProperty("architecture").GetString()!, value.GetProperty("packageType").GetString()!,
            url, size, hash.ToLowerInvariant(), signer.ToUpperInvariant(),
            value.TryGetProperty("minimumOsBuild", out JsonElement os) && os.ValueKind != JsonValueKind.Null ? os.GetString() : null);
    }

    private static ParsedSignedDocument Parse(ReadOnlySpan<byte> utf8, string domain)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions
            { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 32 });
            RejectDuplicates(document.RootElement);
            Exact(document.RootElement, ["signed", "signatures"], []);
            JsonElement signed = document.RootElement.GetProperty("signed").Clone();
            byte[] canonical = Canonicalize(signed);
            byte[] prefix = Encoding.ASCII.GetBytes(domain + "\0");
            byte[] message = GC.AllocateUninitializedArray<byte>(prefix.Length + canonical.Length);
            prefix.CopyTo(message, 0);
            canonical.CopyTo(message, prefix.Length);
            List<UpdateSignature> signatures = document.RootElement.GetProperty("signatures").EnumerateArray().Select(signature =>
            {
                Exact(signature, ["keyId", "algorithm", "signature"], []);
                return new UpdateSignature(signature.GetProperty("keyId").GetString()!,
                    signature.GetProperty("algorithm").GetString()!, Decode(signature.GetProperty("signature").GetString()!));
            }).ToList();
            return new ParsedSignedDocument(signed, message, signatures);
        }
        catch (UpdateSecurityException) { throw; }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or ArgumentException)
        { throw new UpdateSecurityException("UPDATE_SIGNATURE_INVALID", $"Update metadata parsing failed: {exception.GetType().Name}."); }
    }

    private static void VerifyThreshold(ParsedSignedDocument document, IReadOnlyDictionary<string, byte[]> keys,
        UpdateRole role, string errorCode)
    {
        SignatureAlgorithm algorithm = SignatureAlgorithm.Ed25519;
        HashSet<string> valid = [];
        foreach (UpdateSignature signature in document.Signatures)
        {
            if (signature.Algorithm != "ed25519" || !role.KeyIds.Contains(signature.KeyId) ||
                !keys.TryGetValue(signature.KeyId, out byte[]? encoded) || !valid.Add(signature.KeyId)) continue;
            PublicKey key = PublicKey.Import(algorithm, encoded, KeyBlobFormat.RawPublicKey);
            if (!algorithm.Verify(key, document.Message, signature.Signature)) valid.Remove(signature.KeyId);
        }
        if (valid.Count < role.Threshold) throw Invalid(errorCode, "Update metadata signature threshold was not met.");
    }

    private static byte[] Canonicalize(JsonElement value)
    {
        ArrayBufferWriter<byte> output = new();
        using Utf8JsonWriter writer = new(output, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        WriteCanonical(writer, value);
        writer.Flush();
        return output.WrittenSpan.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                { writer.WritePropertyName(property.Name); WriteCanonical(writer, property.Value); }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray(); foreach (JsonElement item in value.EnumerateArray()) WriteCanonical(writer, item); writer.WriteEndArray();
                break;
            case JsonValueKind.String: writer.WriteStringValue(value.GetString()); break;
            case JsonValueKind.Number:
                string number = value.GetRawText();
                if (!long.TryParse(number, System.Globalization.NumberStyles.AllowLeadingSign,
                    System.Globalization.CultureInfo.InvariantCulture, out long integer)) throw new FormatException("Only canonical integers are allowed.");
                writer.WriteNumberValue(integer);
                break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            default: throw new FormatException("Unsupported JSON value.");
        }
    }

    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = [];
            foreach (JsonProperty property in value.EnumerateObject())
            { if (!names.Add(property.Name)) throw new FormatException("Duplicate JSON property."); RejectDuplicates(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (JsonElement item in value.EnumerateArray()) RejectDuplicates(item);
    }

    internal static void Exact(JsonElement value, IReadOnlyCollection<string> required, IReadOnlyCollection<string> optional)
    {
        HashSet<string> actual = value.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        if (required.Any(name => !actual.Contains(name)) || actual.Any(name => !required.Contains(name) && !optional.Contains(name)))
            throw new FormatException("JSON fields do not match the signed schema.");
    }

    internal static byte[] Decode(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
    private static UpdateSecurityException Invalid(string code, string message) => new(code, message);
    private sealed record ParsedSignedDocument(JsonElement Signed, byte[] Message, IReadOnlyList<UpdateSignature> Signatures);
    private sealed record UpdateSignature(string KeyId, string Algorithm, byte[] Signature);
}

internal sealed record UpdateRole(IReadOnlySet<string> KeyIds, int Threshold);
internal sealed record UpdateRoot(int Version, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt,
    IReadOnlyDictionary<string, byte[]> Keys, UpdateRole RootRole, UpdateRole TargetsRole)
{
    public static UpdateRoot Parse(JsonElement value)
    {
        UpdateTrustStore.Exact(value, ["schemaVersion", "rootVersion", "issuedAt", "expiresAt", "consistentSnapshot", "keys", "roles"], []);
        if (value.GetProperty("schemaVersion").GetInt32() != 1 || !value.GetProperty("consistentSnapshot").GetBoolean())
            throw new FormatException("Update root schema is invalid.");
        Dictionary<string, byte[]> keys = [];
        foreach (JsonProperty property in value.GetProperty("keys").EnumerateObject())
        {
            UpdateTrustStore.Exact(property.Value, ["algorithm", "publicKey"], []);
            if (property.Value.GetProperty("algorithm").GetString() != "ed25519") throw new FormatException("Update key algorithm is invalid.");
            keys.Add(property.Name, UpdateTrustStore.Decode(property.Value.GetProperty("publicKey").GetString()!));
        }
        JsonElement roles = value.GetProperty("roles");
        UpdateTrustStore.Exact(roles, ["root", "targets"], []);
        return new UpdateRoot(value.GetProperty("rootVersion").GetInt32(), value.GetProperty("issuedAt").GetDateTimeOffset(),
            value.GetProperty("expiresAt").GetDateTimeOffset(), keys, Role(roles.GetProperty("root")), Role(roles.GetProperty("targets")));
    }

    public void Validate(DateTimeOffset now)
    {
        if (Version < 1 || IssuedAt > now + TimeSpan.FromMinutes(5) || ExpiresAt <= now || Keys.Count < 2 ||
            RootRole.Threshold < 1 || TargetsRole.Threshold < 1 || RootRole.Threshold > RootRole.KeyIds.Count ||
            TargetsRole.Threshold > TargetsRole.KeyIds.Count || RootRole.KeyIds.Any(id => !Keys.ContainsKey(id)) ||
            TargetsRole.KeyIds.Any(id => !Keys.ContainsKey(id))) throw new UpdateSecurityException("UPDATE_ROOT_INVALID", "Update root is invalid or expired.");
    }

    private static UpdateRole Role(JsonElement value)
    {
        UpdateTrustStore.Exact(value, ["keyIds", "threshold"], []);
        HashSet<string> ids = value.GetProperty("keyIds").EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal);
        return new UpdateRole(ids, value.GetProperty("threshold").GetInt32());
    }
}
