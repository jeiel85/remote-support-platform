using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Primitives;

namespace RemoteSupport.Server;

internal sealed record PeerAccessContext(Guid SessionId, Guid PeerId, string Role, string KeyThumbprint,
    string[] GrantedScopes, long PermissionRevision, long TransportEpoch, DateTimeOffset TokenExpiresAt);

internal sealed class PeerAccessService(
    IAttendedSessionStore store,
    ControlPlaneCrypto crypto,
    ControlPlaneOptions options,
    ISystemClock clock)
{
    public PeerAccessContext Authenticate(HttpRequest request, Guid expectedSessionId)
    {
        string token = RequireSingleHeader(request.Headers.Authorization, "DPoP ");
        string proof = RequireSingleHeader(request.Headers["DPoP"], string.Empty);
        if (token.Length is < 32 or > 8192 || proof.Length is < 64 or > 8192 ||
            !crypto.VerifyPeerToken(token, clock.UtcNow, out JsonDocument? tokenDocument) || tokenDocument is null)
            throw Unauthorized("DPOP_TOKEN_INVALID");

        using (tokenDocument)
        {
            PeerAccessContext access = ReadAccess(tokenDocument.RootElement, expectedSessionId, clock.UtcNow);
            ValidatedProof validated = ValidateProof(proof, token, request, access.KeyThumbprint);
            return store.Execute(collection =>
            {
                if (!collection.ById.TryGetValue(expectedSessionId, out SessionAggregate? session) ||
                    session.State != "AUTHORIZED" || session.ExpiresAt <= clock.UtcNow ||
                    session.PermissionRevision != access.PermissionRevision ||
                    session.TransportEpoch != access.TransportEpoch ||
                    !session.GrantedScopes.SequenceEqual(access.GrantedScopes, StringComparer.Ordinal))
                    throw Unauthorized("DPOP_AUTHORIZATION_STALE");

                PeerRecord? peer = access.Role switch
                {
                    "HOST" => session.Host,
                    "OPERATOR" => session.Operator,
                    _ => null,
                };
                if (peer is null || peer.PeerId != access.PeerId ||
                    !FixedEquals(peer.KeyThumbprint, access.KeyThumbprint))
                    throw Unauthorized("DPOP_PEER_MISMATCH");

                foreach (string expired in session.DpopReplays
                    .Where(item => item.Value.ExpiresAt <= clock.UtcNow).Select(item => item.Key).ToArray())
                    session.DpopReplays.Remove(expired);
                if (session.DpopReplays.Count >= 2048)
                    throw new ControlPlaneException(429, "DPOP_REPLAY_WINDOW_FULL", "Proof replay capacity was exceeded.");
                string replayHash = crypto.LookupHash($"dpop\0{access.KeyThumbprint}\0{validated.Jti}");
                if (session.DpopReplays.ContainsKey(replayHash)) throw Unauthorized("DPOP_PROOF_REPLAYED");
                session.DpopReplays.Add(replayHash, new DpopReplayRecord(replayHash,
                    validated.IssuedAt + options.DpopProofLifetime + TimeSpan.FromSeconds(30)));
                return access;
            });
        }
    }

    private ValidatedProof ValidateProof(string compact, string token, HttpRequest request, string expectedThumbprint)
    {
        string[] parts = compact.Split('.');
        if (parts.Length != 3 || parts.Any(part => string.IsNullOrEmpty(part))) throw Unauthorized("DPOP_PROOF_INVALID");
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
                throw Unauthorized("DPOP_PROOF_INVALID");

            string jti = payloadRoot.GetProperty("jti").GetString() ?? string.Empty;
            string htm = payloadRoot.GetProperty("htm").GetString() ?? string.Empty;
            string htu = payloadRoot.GetProperty("htu").GetString() ?? string.Empty;
            string ath = payloadRoot.GetProperty("ath").GetString() ?? string.Empty;
            long issuedAtSeconds = payloadRoot.GetProperty("iat").GetInt64();
            DateTimeOffset issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAtSeconds);
            DateTimeOffset now = clock.UtcNow;
            string expectedHtu = NormalizeRequestUri(request);
            string expectedAth = ControlPlaneCrypto.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
            if (jti.Length is < 16 or > 128 || !jti.All(character => character is >= '!' and <= '~') ||
                !string.Equals(htm, request.Method, StringComparison.Ordinal) ||
                !string.Equals(NormalizeProofUri(htu), expectedHtu, StringComparison.Ordinal) ||
                !FixedEquals(ath, expectedAth) || issuedAt < now - options.DpopProofLifetime ||
                issuedAt > now + TimeSpan.FromSeconds(30))
                throw Unauthorized("DPOP_PROOF_INVALID");
            return new ValidatedProof(jti, issuedAt);
        }
        catch (ControlPlaneException)
        {
            throw;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or KeyNotFoundException or
                                          InvalidOperationException or ArgumentException)
        {
            throw Unauthorized("DPOP_PROOF_INVALID");
        }
        finally
        {
            header?.Dispose();
            payload?.Dispose();
        }
    }

    private static PeerAccessContext ReadAccess(JsonElement payload, Guid expectedSessionId, DateTimeOffset now)
    {
        RequireUniqueObject(payload);
        Guid sessionId = payload.GetProperty("sessionId").GetGuid();
        Guid peerId = payload.GetProperty("peerId").GetGuid();
        string role = payload.GetProperty("role").GetString() ?? string.Empty;
        long permissionRevision = payload.GetProperty("permissionRevision").GetInt64();
        long transportEpoch = payload.GetProperty("transportEpoch").GetInt64();
        long issuedAt = payload.GetProperty("iat").GetInt64();
        long expiresAt = payload.GetProperty("exp").GetInt64();
        string thumbprint = payload.GetProperty("cnf").GetProperty("jkt").GetString() ?? string.Empty;
        string[] scopes = payload.GetProperty("grantedScopes").EnumerateArray()
            .Select(element => element.GetString() ?? string.Empty).ToArray();
        if (sessionId != expectedSessionId || peerId == Guid.Empty || role is not ("HOST" or "OPERATOR") ||
            permissionRevision <= 0 || transportEpoch <= 0 || thumbprint.Length is < 32 or > 128 ||
            expiresAt <= now.ToUnixTimeSeconds() || issuedAt > now.ToUnixTimeSeconds() + 30 ||
            expiresAt - issuedAt > 15 * 60 || scopes.Length is < 1 or > 12 ||
            !scopes.SequenceEqual(scopes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw Unauthorized("DPOP_TOKEN_INVALID");
        return new PeerAccessContext(sessionId, peerId, role, thumbprint, scopes, permissionRevision,
            transportEpoch, DateTimeOffset.FromUnixTimeSeconds(expiresAt));
    }

    private static string RequireSingleHeader(StringValues values, string prefix)
    {
        if (values.Count != 1) throw Unauthorized("DPOP_AUTHENTICATION_REQUIRED");
        string value = values[0] ?? string.Empty;
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || value.Length <= prefix.Length)
            throw Unauthorized("DPOP_AUTHENTICATION_REQUIRED");
        return value[prefix.Length..];
    }

    private static void RequireUniqueObject(JsonElement element)
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

    private static bool FixedEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private static ControlPlaneException Unauthorized(string code) =>
        new(401, code, "DPoP authentication failed.");

    private sealed record ValidatedProof(string Jti, DateTimeOffset IssuedAt);
}
