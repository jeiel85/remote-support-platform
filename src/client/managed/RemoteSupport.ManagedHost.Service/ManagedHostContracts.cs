using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteSupport.ManagedHost.Service;

/// <summary>Client-side mirrors of src/server/RemoteSupport.Server/GovernanceContracts.cs and
/// Contracts.cs request/response shapes for the device credential and managed-session endpoints.
/// Field names must match; the server's default minimal-API JSON options camelCase them.</summary>
public sealed record DetachedProof(string Nonce, string KeyId, string Algorithm, string Signature);
public sealed record DeviceCredentialChallengeRequest(int KeyVersion, string Purpose);
public sealed record DeviceCredentialChallenge(Guid ChallengeId, string Nonce, DateTimeOffset ExpiresAt,
    string CanonicalizationVersion, string Purpose);
public sealed record DeviceCredentialExchangeRequest(Guid ChallengeId, int KeyVersion, DetachedProof Proof);
public sealed record DeviceCredentialResult(string DeviceCredential, DateTimeOffset ExpiresAt,
    long AuthorizationVersion, int KeyVersion);
public sealed record DeviceKeyRotationRequest(JsonElement NewPublicKey, DetachedProof CurrentKeyProof, Guid ChallengeId);
public sealed record DeviceKeyRotationResult(int KeyVersion, bool CredentialChallengeRequired);
public sealed record ClientCapabilities(uint ProtocolMajor, uint ProtocolMinor, IReadOnlyList<string> Features,
    IReadOnlyList<string>? Codecs);
public sealed record DeviceHeartbeat(string AppVersion, string OsVersion, string ServiceState,
    int InteractiveSessions, DateTimeOffset SentAt, ClientCapabilities? Capabilities = null);
public sealed record PendingOperatorDisplay(Guid UserId, string DisplayName, string TenantDisplayName);
public sealed record PendingManagedSessionRequest(Guid SessionId, string SessionType, PendingOperatorDisplay Operator,
    IReadOnlyList<string> RequestedScopes, string PolicyDecisionHash, string ConsentNonce, bool LocalConsentRequired,
    bool LocalNotificationRequired, DateTimeOffset ExpiresAt, long StateVersion);
public sealed record PagedManagedSessionRequests(IReadOnlyList<PendingManagedSessionRequest> Items,
    int? NextPollAfterSeconds = null);
public sealed record SessionResponse(Guid Id, Guid? TenantId, string SessionType, string State, long StateVersion,
    long PermissionRevision, long TransportEpoch, IReadOnlyList<string> RequestedScopes,
    IReadOnlyList<string> GrantedScopes, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
public sealed record ManagedHostDecisionRequest(bool Approved, IReadOnlyList<string> GrantedScopes,
    string ConsentNonce, JsonElement HostEphemeralPublicKey, DetachedProof DecisionProof);
public sealed record ManagedHostDecisionResult(SessionResponse Session, Guid HostPeerId, string? HostBootstrapToken);
public sealed record ProblemContract(string Code, string Message, Guid CorrelationId, bool Retryable,
    int? RetryAfterSeconds = null);

public static class ManagedHostJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
