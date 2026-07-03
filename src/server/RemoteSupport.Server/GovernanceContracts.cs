using System.Text.Json;

namespace RemoteSupport.Server;

public sealed record CreateTenantRequest(string Name, string Slug, string DataRegion);
public sealed record TenantContract(Guid Id, string Name, string Slug, string Status, string PlanCode,
    string DataRegion, DateTimeOffset CreatedAt);
public sealed record TenantSettingsContract(long SettingsVersion, int RetentionDays,
    IReadOnlyList<string> AllowedFeatures, long FileSizeLimitBytes, bool RecordingEnabled = false);
public sealed record TenantSettingsPatch(int? RetentionDays, IReadOnlyList<string>? AllowedFeatures,
    long? FileSizeLimitBytes);
public sealed record MembershipContract(Guid UserId, string DisplayName, string? Email,
    IReadOnlyList<string> Roles, string Status, long PrivilegeVersion);
public sealed record MembershipPatch(IReadOnlyList<string>? Roles, string? Status);
public sealed record PagedMemberships(IReadOnlyList<MembershipContract> Items, string? NextCursor = null);
public sealed record InvitationRequest(string Email, IReadOnlyList<string> Roles, int? ExpiresInSeconds);
public sealed record InvitationContract(Guid Id, string Email, IReadOnlyList<string> Roles, string Status,
    DateTimeOffset ExpiresAt, DateTimeOffset CreatedAt, string? AcceptanceToken = null);
public sealed record PagedInvitations(IReadOnlyList<InvitationContract> Items, string? NextCursor = null);
public sealed record InvitationAcceptanceRequest(string InvitationToken);
public sealed record EnrollmentTokenRequest(int ExpiresInSeconds, Guid? DeviceGroupId, int? AllowedInstallations);
public sealed record EnrollmentTokenResult(string EnrollmentToken, DateTimeOffset ExpiresAt);
public sealed record DeviceInfo(string DisplayName, string OsVersion, string Architecture, string AppVersion);
public sealed record DeviceEnrollmentRequest(string EnrollmentToken, Guid InstallationId,
    JsonElement DevicePublicKey, DeviceInfo DeviceInfo, ProofOfPossession Proof);
public sealed record DeviceEnrollmentResult(Guid DeviceId, string DeviceCredential, int KeyVersion,
    int PolicyVersion);
public sealed record DeviceContract(Guid Id, string DisplayName, string Status, string AppVersion,
    string OsVersion, bool UnattendedEnabled, DateTimeOffset EnrolledAt, DateTimeOffset? LastSeenAt,
    long AuthorizationVersion);
public sealed record PagedDevices(IReadOnlyList<DeviceContract> Items, string? NextCursor = null);
public sealed record PolicyDocumentRequest(string Name, JsonElement Document, string? Description);
public sealed record PolicyContract(Guid Id, string Name, string Status, int? ActiveVersion,
    DateTimeOffset CreatedAt, long ResourceVersion);
public sealed record ActivatePolicyVersionRequest(int Version);
public sealed record PolicyEvaluationRequest(Guid? DeviceId, string SessionType,
    IReadOnlyList<string> RequestedScopes, int? RequestedDurationSeconds, bool LocalUserPresent);
public sealed record DeniedScopeContract(string Scope, string ReasonCode);
public sealed record PolicyDecisionContract(Guid DecisionId, Guid TenantId,
    IReadOnlyList<string> PolicyVersionIds, bool Allow, IReadOnlyList<string> GrantedScopes,
    IReadOnlyList<DeniedScopeContract> DeniedScopes, bool RequiresLocalConsent,
    bool RequiresStepUpMfa, IReadOnlyList<string> AcceptedMfaMethods,
    int MaxSessionDurationSeconds, long MaxFileBytes, IReadOnlyList<string> ExplanationCodes,
    DateTimeOffset EvaluatedAt, string InputHash);
public sealed record AuditEventContract(Guid Id, long ChainSequence, string Category, string Action,
    string Outcome, string ActorType, string? ActorId, string? TargetType, string? TargetId,
    DateTimeOffset OccurredAt, Guid CorrelationId, JsonElement Details, string? PreviousHash,
    string EventHash);
public sealed record PagedAuditEvents(IReadOnlyList<AuditEventContract> Items, string? NextCursor,
    bool ChainValid);
public sealed record AuditVerificationContract(bool Valid, long VerifiedEvents, long? FirstInvalidSequence,
    string? ErrorCode, DateTimeOffset VerifiedAt);
public sealed record DataExportRequest(string? Format);
public sealed record DataExportResult(Guid RequestId, string State, DateTimeOffset RequestedAt,
    DateTimeOffset? DownloadExpiresAt, string? DownloadUrl);
public sealed record TenantClosureRequest(string ConfirmationPhrase, string Reason);
public sealed record TenantClosureResult(Guid RequestId, string State, DateTimeOffset EffectiveAt,
    long StateVersion);
public sealed record SupportGrantRequest(Guid TenantId, string SupportSubject, string ReasonCode,
    int DurationMinutes, bool BreakGlass);
public sealed record SupportGrantContract(Guid Id, Guid TenantId, string SupportSubject, string ReasonCode,
    DateTimeOffset ExpiresAt, bool BreakGlass);
