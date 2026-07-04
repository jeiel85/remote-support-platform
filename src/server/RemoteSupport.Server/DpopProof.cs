using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RemoteSupport.Server;

internal readonly record struct ValidatedDpopProof(string Jti, DateTimeOffset IssuedAt);

/// <summary>Shared RFC 9449 DPoP proof validation used by both peer and device access tokens.</summary>
internal static class DpopProof
{
    public static ValidatedDpopProof Validate(string compact, string token, HttpRequest request,
        string expectedThumbprint, TimeSpan proofLifetime, DateTimeOffset now, Func<string, ControlPlaneException> unauthorized)
    {
        string[] parts = compact.Split('.');
        if (parts.Length != 3 || parts.Any(part => string.IsNullOrEmpty(part))) throw unauthorized("DPOP_PROOF_INVALID");
        JsonDocument? header = null;
        JsonDocument? payload = null;
        try
        {
            header = JsonDocument.Parse(ControlPlaneCrypto.Base64UrlDecode(parts[0]));
            payload = JsonDocument.Parse(ControlPlaneCrypto.Base64UrlDecode(parts[1]));
            JsonElement headerRoot = header.RootElement;
            JsonElement payloadRoot = payload.RootElement;
            RequireUniqueObject(headerRoot);
            RequireUniqueObject(payloadRoot);
            if (headerRoot.GetProperty("typ").GetString() != "dpop+jwt" ||
                headerRoot.GetProperty("alg").GetString() != "ES256" ||
                !headerRoot.TryGetProperty("jwk", out JsonElement jwk) ||
                !FixedEquals(ControlPlaneCrypto.Thumbprint(jwk), expectedThumbprint) ||
                !ControlPlaneCrypto.VerifyP256Jws(jwk, parts[0] + "." + parts[1], parts[2]))
                throw unauthorized("DPOP_PROOF_INVALID");

            string jti = payloadRoot.GetProperty("jti").GetString() ?? string.Empty;
            string htm = payloadRoot.GetProperty("htm").GetString() ?? string.Empty;
            string htu = payloadRoot.GetProperty("htu").GetString() ?? string.Empty;
            string ath = payloadRoot.GetProperty("ath").GetString() ?? string.Empty;
            long issuedAtSeconds = payloadRoot.GetProperty("iat").GetInt64();
            DateTimeOffset issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAtSeconds);
            string expectedHtu = NormalizeRequestUri(request);
            string expectedAth = ControlPlaneCrypto.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
            if (jti.Length is < 16 or > 128 || !jti.All(character => character is >= '!' and <= '~') ||
                !string.Equals(htm, request.Method, StringComparison.Ordinal) ||
                !string.Equals(NormalizeProofUri(htu), expectedHtu, StringComparison.Ordinal) ||
                !FixedEquals(ath, expectedAth) || issuedAt < now - proofLifetime ||
                issuedAt > now + TimeSpan.FromSeconds(30))
                throw unauthorized("DPOP_PROOF_INVALID");
            return new ValidatedDpopProof(jti, issuedAt);
        }
        catch (ControlPlaneException)
        {
            throw;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or KeyNotFoundException or
                                          InvalidOperationException or ArgumentException)
        {
            throw unauthorized("DPOP_PROOF_INVALID");
        }
        finally
        {
            header?.Dispose();
            payload?.Dispose();
        }
    }

    public static string RequireSingleHeader(Microsoft.Extensions.Primitives.StringValues values, string prefix,
        Func<string, ControlPlaneException> unauthorized)
    {
        if (values.Count != 1) throw unauthorized("DPOP_AUTHENTICATION_REQUIRED");
        string value = values[0] ?? string.Empty;
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || value.Length <= prefix.Length)
            throw unauthorized("DPOP_AUTHENTICATION_REQUIRED");
        return value[prefix.Length..];
    }

    public static void RequireUniqueObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            element.EnumerateObject().GroupBy(property => property.Name, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new FormatException("JSON object contains duplicate properties.");
    }

    private static string NormalizeRequestUri(HttpRequest request) => NormalizeProofUri(
        $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}");

    private static string NormalizeProofUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new FormatException("DPoP target URI was invalid.");
        return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
    }

    public static bool FixedEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}
