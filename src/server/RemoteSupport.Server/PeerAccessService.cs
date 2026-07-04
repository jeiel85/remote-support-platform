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
        string token = DpopProof.RequireSingleHeader(request.Headers.Authorization, "DPoP ", Unauthorized);
        string proof = DpopProof.RequireSingleHeader(request.Headers["DPoP"], string.Empty, Unauthorized);
        if (token.Length is < 32 or > 8192 || proof.Length is < 64 or > 8192 ||
            !crypto.VerifyPeerToken(token, clock.UtcNow, out JsonDocument? tokenDocument) || tokenDocument is null)
            throw Unauthorized("DPOP_TOKEN_INVALID");

        using (tokenDocument)
        {
            PeerAccessContext access = ReadAccess(tokenDocument.RootElement, expectedSessionId, clock.UtcNow);
            ValidatedDpopProof validated = DpopProof.Validate(proof, token, request, access.KeyThumbprint,
                options.DpopProofLifetime, clock.UtcNow, Unauthorized);
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
                    !DpopProof.FixedEquals(peer.KeyThumbprint, access.KeyThumbprint))
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

    private static PeerAccessContext ReadAccess(JsonElement payload, Guid expectedSessionId, DateTimeOffset now)
    {
        DpopProof.RequireUniqueObject(payload);
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

    private static ControlPlaneException Unauthorized(string code) =>
        new(401, code, "DPoP authentication failed.");
}
