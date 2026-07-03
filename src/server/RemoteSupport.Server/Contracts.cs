using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteSupport.Server;

public sealed record ClientCapabilities(uint ProtocolMajor, uint ProtocolMinor, IReadOnlyList<string> Features, IReadOnlyList<string>? Codecs);
public sealed record CreateAttendedSessionRequest(JsonElement HostEphemeralPublicKey, string ClientVersion,
    ClientCapabilities Capabilities, Guid? InstallationInstanceId, string? Locale);
public sealed record AttendedSessionCreated(Guid SessionId, string SupportCode, DateTimeOffset ExpiresAt,
    string HostBootstrapToken, Guid HostPeerId, string State, long StateVersion, string PendingEventsUrl);
public sealed record ResolveAttendedSessionRequest(string SupportCode, IReadOnlyList<string> RequestedScopes,
    JsonElement OperatorEphemeralPublicKey, string ClientVersion, ClientCapabilities? Capabilities);
public sealed record OperatorDisplay(string DisplayName, string TenantDisplayName, bool VerifiedTenant);
public sealed record ConsentRequest(Guid SessionId, Guid ConsentRequestId, OperatorDisplay Operator,
    IReadOnlyList<string> RequestedScopes, DateTimeOffset ExpiresAt, long StateVersion, string OperatorBootstrapToken);
public sealed record PendingConsent(Guid SessionId, Guid ConsentRequestId, OperatorDisplay Operator,
    IReadOnlyList<string> RequestedScopes, DateTimeOffset ExpiresAt, long StateVersion, string ConsentNonce,
    string HostEphemeralKeyThumbprint);
public sealed record DetachedProof(string Nonce, string KeyId, string Algorithm, string Signature);
public sealed record ConsentDecision(Guid ConsentRequestId, bool Approved, IReadOnlyList<string> GrantedScopes,
    string ConsentNonce, DetachedProof DecisionProof);
public sealed record SessionResponse(Guid Id, Guid? TenantId, string SessionType, string State, long StateVersion,
    long PermissionRevision, long TransportEpoch, IReadOnlyList<string> RequestedScopes,
    IReadOnlyList<string> GrantedScopes, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
public sealed record PeerAuthorizationChallenge(Guid ChallengeId, Guid SessionId, Guid PeerId, string Role,
    string Nonce, string KeyThumbprint, long TransportEpoch, DateTimeOffset ExpiresAt, string CanonicalizationVersion);
public sealed record ProofOfPossession(string Nonce, string Signature, JsonElement PublicKey, string Algorithm);
public sealed record PeerAuthorizationRequest(Guid ChallengeId, string Role, ProofOfPossession Proof);
public sealed record PeerAuthorization(Guid SessionId, Guid PeerId, string Role, string PeerToken,
    IReadOnlyList<string> GrantedScopes, long PermissionRevision, long TransportEpoch, DateTimeOffset ExpiresAt,
    Guid RemotePeerId, string RemoteRole, JsonElement RemoteEphemeralPublicKey, string RemoteKeyThumbprint,
    string AuthorizationContextSha256);
public sealed record SignalingTicket(string Ticket, string SignalingUrl, DateTimeOffset ExpiresAt,
    long TransportEpoch);
public sealed record IceServer(IReadOnlyList<string> Urls, string Username, string Credential);
public sealed record TurnCredentials(string Region, IReadOnlyList<IceServer> IceServers,
    DateTimeOffset ExpiresAt);
public sealed record SessionTerminationRequest(string ReasonCode);
public sealed record ScopeRevocationRequest(IReadOnlyList<string> RevokedScopes, string ReasonCode);
public sealed record TurnUsageReport(Guid EventId, string Username, string Region, string Transport,
    string NodeId, ulong BytesFromClient, ulong BytesToClient, DateTimeOffset StartedAt,
    DateTimeOffset EndedAt);
public sealed record TurnUsageAccepted(Guid EventId, Guid SessionId, Guid? TenantId, string Region,
    string Transport, ulong TotalBytes);
public sealed record ProblemContract(string Code, string Message, Guid CorrelationId, bool Retryable,
    int? RetryAfterSeconds = null);

[JsonSerializable(typeof(CreateAttendedSessionRequest))]
[JsonSerializable(typeof(AttendedSessionCreated))]
[JsonSerializable(typeof(ResolveAttendedSessionRequest))]
[JsonSerializable(typeof(ConsentRequest))]
[JsonSerializable(typeof(PendingConsent))]
[JsonSerializable(typeof(ConsentDecision))]
[JsonSerializable(typeof(SessionResponse))]
[JsonSerializable(typeof(PeerAuthorizationChallenge))]
[JsonSerializable(typeof(PeerAuthorizationRequest))]
[JsonSerializable(typeof(PeerAuthorization))]
[JsonSerializable(typeof(SignalingTicket))]
[JsonSerializable(typeof(TurnCredentials))]
[JsonSerializable(typeof(SessionTerminationRequest))]
[JsonSerializable(typeof(ScopeRevocationRequest))]
[JsonSerializable(typeof(TurnUsageReport))]
[JsonSerializable(typeof(TurnUsageAccepted))]
internal sealed partial class ControlPlaneJsonContext : JsonSerializerContext;
