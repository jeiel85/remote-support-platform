using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RemoteSupport.Infrastructure;

public sealed class EphemeralPeerIdentity : IDisposable
{
    private static readonly BigInteger P256Order = BigInteger.Parse(
        "00FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551",
        NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    private static readonly BigInteger P256HalfOrder = P256Order >> 1;
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private bool disposed;

    public string KeyThumbprint
    {
        get
        {
            byte[] canonical = JsonSerializer.SerializeToUtf8Bytes(PublicJwk);
            return Base64Url(SHA256.HashData(canonical));
        }
    }

    public PeerPublicJwk PublicJwk
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ECParameters parameters = key.ExportParameters(false);
            return new PeerPublicJwk("P-256", "EC", Base64Url(parameters.Q.X!), Base64Url(parameters.Q.Y!));
        }
    }

    internal NativePeerKeyMaterial ExportNativeKeyMaterial()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ECParameters parameters = key.ExportParameters(true);
        byte[] privateKey = Pad(parameters.D!, 32);
        byte[] publicKey = new byte[65];
        publicKey[0] = 0x04;
        Pad(parameters.Q.X!, 32).CopyTo(publicKey, 1);
        Pad(parameters.Q.Y!, 32).CopyTo(publicKey, 33);
        return new NativePeerKeyMaterial(privateKey, publicKey);
    }

    public string SignBase64Url(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        byte[] signature = key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        BigInteger s = new(signature.AsSpan(32), isUnsigned: true, isBigEndian: true);
        if (s > P256HalfOrder)
        {
            s = P256Order - s;
            byte[] normalized = s.ToByteArray(isUnsigned: true, isBigEndian: true);
            if (normalized.Length > 32) throw new CryptographicException("Failed to normalize P-256 signature.");
            signature.AsSpan(32).Clear();
            normalized.CopyTo(signature.AsSpan(64 - normalized.Length));
        }
        return Base64Url(signature);
    }

    public string CreateDpopProof(HttpMethod method, Uri target, string accessToken, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        if (!target.IsAbsoluteUri || !string.IsNullOrEmpty(target.Query) || !string.IsNullOrEmpty(target.Fragment) ||
            !string.IsNullOrEmpty(target.UserInfo)) throw new ArgumentException("DPoP target URI must be absolute and query-free.", nameof(target));
        string header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            alg = "ES256",
            typ = "dpop+jwt",
            jwk = PublicJwk,
        }));
        string payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            jti = Guid.CreateVersion7(now).ToString("D"),
            htm = method.Method.ToUpperInvariant(),
            htu = target.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped),
            iat = now.ToUnixTimeSeconds(),
            ath = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken))),
        }));
        string signingInput = header + "." + payload;
        return signingInput + "." + SignBase64Url(Encoding.ASCII.GetBytes(signingInput));
    }

    public static byte[] CreateConsentPayload(HostSessionCreated session, PendingConsentResponse consent, bool approved,
        IReadOnlyList<string> grantedScopes)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("approved", approved);
            writer.WriteString("consentNonce", consent.ConsentNonce);
            writer.WriteString("consentRequestId", consent.ConsentRequestId.ToString("D"));
            writer.WriteString("expiresAt", consent.ExpiresAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
            writer.WritePropertyName("grantedScopes");
            writer.WriteStartArray();
            foreach (string scope in grantedScopes.Order(StringComparer.Ordinal)) writer.WriteStringValue(scope);
            writer.WriteEndArray();
            writer.WriteString("hostEphemeralKeyThumbprint", consent.HostEphemeralKeyThumbprint);
            writer.WriteString("hostPeerId", session.HostPeerId.ToString("D"));
            writer.WriteString("sessionId", session.SessionId.ToString("D"));
            writer.WriteNumber("stateVersion", consent.StateVersion);
            writer.WriteEndObject();
        }
        return DomainSeparated("RSP-CONSENT-DECISION-V1", buffer.WrittenSpan);
    }

    public static byte[] CreatePeerAuthorizationPayload(PeerAuthorizationChallengeResponse challenge)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("challengeId", challenge.ChallengeId.ToString("D"));
            writer.WriteString("expiresAt", challenge.ExpiresAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
            writer.WriteString("keyThumbprint", challenge.KeyThumbprint);
            writer.WriteString("nonce", challenge.Nonce);
            writer.WriteString("peerId", challenge.PeerId.ToString("D"));
            writer.WriteString("role", challenge.Role);
            writer.WriteString("sessionId", challenge.SessionId.ToString("D"));
            writer.WriteNumber("transportEpoch", challenge.TransportEpoch);
            writer.WriteEndObject();
        }
        return DomainSeparated("RSP-PEER-AUTH-V1", buffer.WrittenSpan);
    }

    public void Dispose()
    {
        if (disposed) return;
        key.Dispose();
        disposed = true;
    }

    private static byte[] DomainSeparated(string domain, ReadOnlySpan<byte> payload)
    {
        byte[] prefix = Encoding.ASCII.GetBytes(domain + "\0");
        byte[] result = GC.AllocateUninitializedArray<byte>(prefix.Length + payload.Length);
        prefix.CopyTo(result, 0);
        payload.CopyTo(result.AsSpan(prefix.Length));
        return result;
    }

    internal static string Base64Url(ReadOnlySpan<byte> value) => Convert.ToBase64String(value)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static byte[] Base64UrlDecode(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }

    private static byte[] Pad(byte[] value, int length)
    {
        if (value.Length > length) throw new CryptographicException("P-256 key component is oversized.");
        byte[] result = new byte[length];
        value.CopyTo(result, length - value.Length);
        return result;
    }
}

internal sealed record NativePeerKeyMaterial(byte[] PrivateKey, byte[] PublicKey);

public sealed record PeerPublicJwk(
    [property: System.Text.Json.Serialization.JsonPropertyName("crv")] string Crv,
    [property: System.Text.Json.Serialization.JsonPropertyName("kty")] string Kty,
    [property: System.Text.Json.Serialization.JsonPropertyName("x")] string X,
    [property: System.Text.Json.Serialization.JsonPropertyName("y")] string Y);
