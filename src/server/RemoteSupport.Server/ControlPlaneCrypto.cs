using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RemoteSupport.Server;

internal sealed class ControlPlaneCrypto
{
    private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private static readonly BigInteger P256HalfOrder = BigInteger.Parse(
        "7FFFFFFF800000007FFFFFFFFFFFFFFFDE737D56D38BCF4279DCE5617E3192A8",
        NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    private readonly byte[] lookupKey;
    private readonly byte[] tokenKey;

    public ControlPlaneCrypto(ControlPlaneOptions options)
    {
        lookupKey = options.GetLookupKey();
        tokenKey = options.GetTokenSigningKey();
    }

    public static string GenerateSupportCode()
    {
        Span<byte> random = stackalloc byte[8];
        RandomNumberGenerator.Fill(random);
        ulong value = 0;
        for (int index = 0; index < random.Length; index++)
        {
            value = (value << 8) | random[index];
        }
        return EncodeSupportCode(value);
    }

    public string DeriveSupportCode(string idempotencyKey, int counter)
    {
        byte[] derived = Derive("support-code", idempotencyKey, counter);
        ulong value = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(derived);
        return EncodeSupportCode(value);
    }

    public string DeriveSecret(string purpose, string idempotencyKey) =>
        Base64UrlEncode(Derive(purpose, idempotencyKey, 0));

    public Guid DeriveGuid(string purpose, string idempotencyKey)
    {
        byte[] bytes = Derive(purpose, idempotencyKey, 0)[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

    public static string CreateRequestHash(CreateAttendedSessionRequest request, string hostThumbprint)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("capabilities");
            writer.WriteStartObject();
            writer.WritePropertyName("codecs");
            writer.WriteStartArray();
            foreach (string codec in request.Capabilities.Codecs ?? []) writer.WriteStringValue(codec);
            writer.WriteEndArray();
            writer.WritePropertyName("features");
            writer.WriteStartArray();
            foreach (string feature in request.Capabilities.Features) writer.WriteStringValue(feature);
            writer.WriteEndArray();
            writer.WriteNumber("protocolMajor", request.Capabilities.ProtocolMajor);
            writer.WriteNumber("protocolMinor", request.Capabilities.ProtocolMinor);
            writer.WriteEndObject();
            writer.WriteString("clientVersion", request.ClientVersion);
            writer.WriteString("hostEphemeralKeyThumbprint", hostThumbprint);
            if (request.InstallationInstanceId is { } installation)
                writer.WriteString("installationInstanceId", installation);
            else
                writer.WriteNull("installationInstanceId");
            if (request.Locale is not null) writer.WriteString("locale", request.Locale);
            else writer.WriteNull("locale");
            writer.WriteEndObject();
        }
        return Convert.ToHexString(SHA256.HashData(
            DomainSeparated("RSP-IDEMPOTENCY-REQUEST-V1", buffer.WrittenSpan)));
    }

    private static string EncodeSupportCode(ulong value)
    {
        value &= (1UL << 50) - 1;
        Span<char> symbols = stackalloc char[11];
        for (int index = 10; index >= 0; index--)
        {
            if (index == 5)
            {
                symbols[index] = '-';
                continue;
            }
            symbols[index] = Crockford[(int)(value & 31)];
            value >>= 5;
        }
        return new string(symbols);
    }

    private byte[] Derive(string purpose, string idempotencyKey, int counter)
    {
        byte[] input = Encoding.UTF8.GetBytes($"RSP-IDEMPOTENCY-DERIVE-V1\0{purpose}\0{counter}\0{idempotencyKey}");
        return HMACSHA256.HashData(tokenKey, input);
    }

    public static bool TryNormalizeSupportCode(string? input, out string normalized)
    {
        normalized = (input ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length != 11 || normalized[5] != '-') return false;
        for (int index = 0; index < normalized.Length; index++)
        {
            if (index != 5 && !Crockford.Contains(normalized[index])) return false;
        }
        return true;
    }

    public string LookupHash(string value) => Convert.ToHexString(HMACSHA256.HashData(lookupKey, Encoding.UTF8.GetBytes(value)));

    public static string GenerateSecret(int bytes = 32) => Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));

    public static string Thumbprint(JsonElement jwk)
    {
        JwkParameters parameters = ParseJwk(jwk);
        string canonical = parameters.Y is null
            ? $"{{\"crv\":\"{parameters.Crv}\",\"kty\":\"{parameters.Kty}\",\"x\":\"{parameters.X}\"}}"
            : $"{{\"crv\":\"{parameters.Crv}\",\"kty\":\"{parameters.Kty}\",\"x\":\"{parameters.X}\",\"y\":\"{parameters.Y}\"}}";
        return Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static bool VerifyP256(JsonElement jwk, string algorithm, ReadOnlySpan<byte> signedBytes, string encodedSignature)
    {
        if (!string.Equals(algorithm, "ecdsa-p256-sha256-p1363", StringComparison.Ordinal)) return false;
        JwkParameters parameters;
        byte[] signature;
        try
        {
            parameters = ParseJwk(jwk);
            signature = Base64UrlDecode(encodedSignature);
        }
        catch (FormatException)
        {
            return false;
        }
        if (parameters.Kty != "EC" || parameters.Crv != "P-256" || parameters.Y is null || signature.Length != 64) return false;
        BigInteger s = new(signature.AsSpan(32, 32), isUnsigned: true, isBigEndian: true);
        if (s.IsZero || s > P256HalfOrder) return false;
        using ECDsa ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = Base64UrlDecode(parameters.X), Y = Base64UrlDecode(parameters.Y) },
        });
        return ecdsa.VerifyData(signedBytes, signature, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    public static bool VerifyP256Jws(JsonElement jwk, string signingInput, string encodedSignature)
    {
        JwkParameters parameters;
        byte[] signature;
        try
        {
            parameters = ParseJwk(jwk);
            signature = Base64UrlDecode(encodedSignature);
        }
        catch (Exception exception) when (exception is FormatException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
        if (parameters.Kty != "EC" || parameters.Crv != "P-256" || parameters.Y is null || signature.Length != 64)
            return false;
        using ECDsa ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = Base64UrlDecode(parameters.X), Y = Base64UrlDecode(parameters.Y) },
        });
        return ecdsa.VerifyData(Encoding.ASCII.GetBytes(signingInput), signature, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    public string SignalingRoutingKey(Guid sessionId)
    {
        byte[] input = Encoding.ASCII.GetBytes($"RSP-SIGNALING-SHARD-V1\0{sessionId:D}");
        return Base64UrlEncode(HMACSHA256.HashData(tokenKey, input))[..16];
    }

    public static byte[] ConsentBytes(SessionAggregate session, ConsentDecision decision)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("approved", decision.Approved);
            writer.WriteString("consentNonce", decision.ConsentNonce);
            writer.WriteString("consentRequestId", decision.ConsentRequestId.ToString("D"));
            writer.WriteString("expiresAt", session.Consent!.ExpiresAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
            writer.WritePropertyName("grantedScopes");
            writer.WriteStartArray();
            foreach (string scope in decision.GrantedScopes.Order(StringComparer.Ordinal)) writer.WriteStringValue(scope);
            writer.WriteEndArray();
            writer.WriteString("hostEphemeralKeyThumbprint", session.Host.KeyThumbprint);
            writer.WriteString("hostPeerId", session.Host.PeerId.ToString("D"));
            writer.WriteString("sessionId", session.Id.ToString("D"));
            writer.WriteNumber("stateVersion", session.StateVersion);
            writer.WriteEndObject();
        }
        return DomainSeparated("RSP-CONSENT-DECISION-V1", buffer.WrittenSpan);
    }

    public static byte[] PeerAuthorizationBytes(SessionAggregate session, ChallengeRecord challenge, PeerRecord peer, string nonce)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("challengeId", challenge.Id.ToString("D"));
            writer.WriteString("expiresAt", challenge.ExpiresAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
            writer.WriteString("keyThumbprint", peer.KeyThumbprint);
            writer.WriteString("nonce", nonce);
            writer.WriteString("peerId", peer.PeerId.ToString("D"));
            writer.WriteString("role", peer.Role);
            writer.WriteString("sessionId", session.Id.ToString("D"));
            writer.WriteNumber("transportEpoch", challenge.TransportEpoch);
            writer.WriteEndObject();
        }
        return DomainSeparated("RSP-PEER-AUTH-V1", buffer.WrittenSpan);
    }

    public static string AuthorizationContext(SessionAggregate session)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("grantedScopes");
            writer.WriteStartArray();
            foreach (string scope in session.GrantedScopes.Order(StringComparer.Ordinal)) writer.WriteStringValue(scope);
            writer.WriteEndArray();
            writer.WriteString("hostPeerId", session.Host.PeerId.ToString("D"));
            writer.WriteString("operatorPeerId", session.Operator!.PeerId.ToString("D"));
            writer.WriteNumber("permissionRevision", session.PermissionRevision);
            writer.WriteString("sessionId", session.Id.ToString("D"));
            writer.WriteNumber("transportEpoch", session.TransportEpoch);
            writer.WriteEndObject();
        }
        return Base64UrlEncode(SHA256.HashData(DomainSeparated("RSP-AUTHORIZATION-CONTEXT-V1", buffer.WrittenSpan)));
    }

    public string IssuePeerToken(SessionAggregate session, PeerRecord peer, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        ArrayBufferWriter<byte> payload = new();
        using (Utf8JsonWriter writer = new(payload))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("cnf");
            writer.WriteStartObject();
            writer.WriteString("jkt", peer.KeyThumbprint);
            writer.WriteEndObject();
            writer.WriteNumber("exp", expiresAt.ToUnixTimeSeconds());
            writer.WritePropertyName("grantedScopes");
            writer.WriteStartArray();
            foreach (string scope in session.GrantedScopes.Order(StringComparer.Ordinal)) writer.WriteStringValue(scope);
            writer.WriteEndArray();
            writer.WriteNumber("iat", issuedAt.ToUnixTimeSeconds());
            writer.WriteNumber("permissionRevision", session.PermissionRevision);
            writer.WriteString("peerId", peer.PeerId);
            writer.WriteString("role", peer.Role);
            writer.WriteString("sessionId", session.Id);
            writer.WriteString("sub", peer.PeerId);
            writer.WriteNumber("transportEpoch", session.TransportEpoch);
            writer.WriteEndObject();
        }
        string encodedPayload = Base64UrlEncode(payload.WrittenSpan);
        string signature = Base64UrlEncode(HMACSHA256.HashData(tokenKey, Encoding.ASCII.GetBytes(encodedPayload)));
        return encodedPayload + "." + signature;
    }

    public bool VerifyPeerToken(string token, DateTimeOffset now, out JsonDocument? payload)
    {
        payload = null;
        string[] parts = token.Split('.');
        if (parts.Length != 2) return false;
        byte[] expected = HMACSHA256.HashData(tokenKey, Encoding.ASCII.GetBytes(parts[0]));
        byte[] actual;
        try { actual = Base64UrlDecode(parts[1]); }
        catch (FormatException) { return false; }
        if (!CryptographicOperations.FixedTimeEquals(expected, actual)) return false;
        try
        {
            payload = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            return payload.RootElement.GetProperty("exp").GetInt64() > now.ToUnixTimeSeconds();
        }
        catch (JsonException)
        {
            payload?.Dispose();
            payload = null;
            return false;
        }
    }

    public static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Base64UrlDecode(string value)
    {
        string normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += new string('=', (4 - normalized.Length % 4) % 4);
        byte[] decoded = Convert.FromBase64String(normalized);
        if (!string.Equals(Base64UrlEncode(decoded), value, StringComparison.Ordinal)) throw new FormatException("Non-canonical base64url.");
        return decoded;
    }

    public static byte[] Canonicalize(JsonElement element)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = false });
        WriteCanonical(writer, element);
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException("Unsupported JSON value for canonicalization.");
        }
    }

    private static byte[] DomainSeparated(string domain, ReadOnlySpan<byte> payload)
    {
        byte[] prefix = Encoding.ASCII.GetBytes(domain);
        byte[] result = new byte[prefix.Length + 1 + payload.Length];
        prefix.CopyTo(result, 0);
        payload.CopyTo(result.AsSpan(prefix.Length + 1));
        return result;
    }

    private static JwkParameters ParseJwk(JsonElement jwk)
    {
        if (jwk.ValueKind != JsonValueKind.Object) throw new FormatException("JWK must be an object.");
        string kty = jwk.GetProperty("kty").GetString() ?? throw new FormatException("Missing kty.");
        string crv = jwk.GetProperty("crv").GetString() ?? throw new FormatException("Missing crv.");
        string x = jwk.GetProperty("x").GetString() ?? throw new FormatException("Missing x.");
        string? y = jwk.TryGetProperty("y", out JsonElement yProperty) ? yProperty.GetString() : null;
        int expectedProperties = y is null ? 3 : 4;
        if (jwk.EnumerateObject().Count() != expectedProperties ||
            (kty == "EC" && (crv != "P-256" || y is null)) ||
            (kty == "OKP" && (crv != "Ed25519" || y is not null)) ||
            (kty is not ("EC" or "OKP"))) throw new FormatException("Unsupported JWK.");
        if (Base64UrlDecode(x).Length != 32 || (y is not null && Base64UrlDecode(y).Length != 32)) throw new FormatException("Invalid JWK coordinate.");
        return new JwkParameters(kty, crv, x, y);
    }

    private sealed record JwkParameters(string Kty, string Crv, string X, string? Y);
}
