using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RemoteSupport.ManagedHost.Service;

/// <summary>Builds an RFC 9449 DPoP proof for a device access token, matching the
/// verification in src/server/RemoteSupport.Server/DpopProof.cs.</summary>
public static class DeviceDpopProof
{
    private static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Create(IDeviceIdentityKey key, string method, Uri target, string accessToken, DateTimeOffset issuedAt)
    {
        string encodedHeader = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            typ = "dpop+jwt",
            alg = "ES256",
            jwk = key.PublicJwk,
        }, ManagedHostJson.Options));
        string htu = target.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
        string encodedPayload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            jti = $"dpop-{Guid.NewGuid():N}",
            htm = method,
            htu,
            iat = issuedAt.ToUnixTimeSeconds(),
            ath = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken))),
        }, ManagedHostJson.Options));
        string input = encodedHeader + "." + encodedPayload;
        string signature = key.Sign(Encoding.ASCII.GetBytes(input));
        return input + "." + signature;
    }
}
