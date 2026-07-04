using System.Text.Json;

namespace RemoteSupport.ManagedHost.Service;

/// <summary>Client-side mirrors of the exact canonical byte layouts the server verifies
/// signatures against (GovernanceService.CreateDeviceCredentialProofBytes and
/// AttendedSessionService.ManagedHostDecisionProofBytes). Field order does not matter because
/// JsonCanonicalization sorts object keys, but field names and presence must match exactly.</summary>
public static class ManagedHostSignedPayloads
{
    public static byte[] DeviceCredentialProof(string canonicalizationVersion, Guid deviceId, Guid challengeId,
        string purpose, string nonce, string? newKeyThumbprint = null)
    {
        JsonElement payload = newKeyThumbprint is null
            ? JsonSerializer.SerializeToElement(new { deviceId, challengeId, purpose, nonce })
            : JsonSerializer.SerializeToElement(new { deviceId, challengeId, purpose, nonce, newKeyThumbprint });
        return JsonCanonicalization.DomainSeparated(canonicalizationVersion, JsonCanonicalization.Canonicalize(payload));
    }

    public static byte[] ManagedHostDecisionProof(Guid sessionId, bool approved, IReadOnlyList<string> grantedScopes,
        string consentNonce, string hostEphemeralKeyThumbprint)
    {
        JsonElement payload = JsonSerializer.SerializeToElement(new
        {
            approved,
            consentNonce,
            grantedScopes = grantedScopes.Order(StringComparer.Ordinal).ToArray(),
            hostEphemeralKeyThumbprint,
            sessionId = sessionId.ToString("D"),
        });
        return JsonCanonicalization.DomainSeparated("RSP-MANAGED-HOST-DECISION-V1", JsonCanonicalization.Canonicalize(payload));
    }
}
