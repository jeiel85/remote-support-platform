using System.Collections.Concurrent;
using System.Text.Json;

namespace RemoteSupport.Server;

internal sealed class SessionAggregate
{
    public required Guid Id { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public string? CodeHash { get; init; }
    public PeerRecord? Host { get; set; }
    public string? IdempotencyHash { get; init; }
    public string? IdempotencyRequestHash { get; init; }
    public int IdempotencyDerivationCounter { get; init; }
    public string State { get; set; } = "WAITING_FOR_OPERATOR";
    public string SessionType { get; init; } = "ATTENDED";
    public Guid? DeviceId { get; init; }
    public string? PolicyDecisionHash { get; init; }
    public bool RequiresLocalConsent { get; init; }
    public long StateVersion { get; set; } = 1;
    public long PermissionRevision { get; set; }
    public long TransportEpoch { get; set; }
    public int CodeAttempts { get; set; }
    public Guid? TenantId { get; set; }
    public string[] RequestedScopes { get; set; } = [];
    public string[] GrantedScopes { get; set; } = [];
    public PeerRecord? Operator { get; set; }
    public ConsentRecord? Consent { get; set; }
    public Dictionary<string, BootstrapRecord> BootstrapCredentials { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<Guid, ChallengeRecord> Challenges { get; init; } = [];
    public Dictionary<string, DpopReplayRecord> DpopReplays { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, SignalingTicketRecord> SignalingTickets { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<Guid, long> SignalingSequences { get; init; } = [];
    public Dictionary<Guid, SignalingRateRecord> SignalingRates { get; init; } = [];
    public Dictionary<string, RelayCredentialRecord> RelayCredentials { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<Guid, RelayUsageRecord> RelayUsage { get; init; } = [];
    public List<AuditRecord> AuditEvents { get; init; } = [];
    public List<OutboxRecord> OutboxMessages { get; init; } = [];
}

internal sealed record PeerRecord(Guid PeerId, string Role, string PublicJwk, string KeyThumbprint,
    string? Subject, string? DisplayName, string? TenantDisplayName, bool VerifiedTenant);
internal sealed record BootstrapRecord(Guid PeerId, string Role, DateTimeOffset ExpiresAt, int MaximumUses)
{
    public int Uses { get; set; }
}
internal sealed record ConsentRecord(Guid Id, DateTimeOffset ExpiresAt)
{
    public string? NonceHash { get; set; }
    public bool NonceConsumed { get; set; }
}
internal sealed record ChallengeRecord(Guid Id, Guid PeerId, string Role, string NonceHash,
    DateTimeOffset ExpiresAt, long TransportEpoch)
{
    public bool Consumed { get; set; }
}
internal sealed record DpopReplayRecord(string Hash, DateTimeOffset ExpiresAt);
internal sealed record SignalingTicketRecord(Guid PeerId, string Role, string KeyThumbprint,
    string[] GrantedScopes, long PermissionRevision, long TransportEpoch, DateTimeOffset ExpiresAt)
{
    public bool Consumed { get; set; }
}
internal sealed record SignalingRateRecord(long TransportEpoch, DateTimeOffset MessageWindowStartedAt,
    DateTimeOffset IceWindowStartedAt)
{
    public int MessageCount { get; set; }
    public int IceWindowCount { get; set; }
    public int IceTotal { get; set; }
}
internal sealed record RelayCredentialRecord(Guid PeerId, string Role, string Region,
    DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);
internal sealed record RelayUsageRecord(Guid EventId, string OpaqueUserId, string Region, string Transport,
    string NodeId, ulong BytesFromClient, ulong BytesToClient, DateTimeOffset StartedAt, DateTimeOffset EndedAt);
internal sealed record AuditRecord(Guid Id, long Sequence, string Action, string Outcome, string ActorType, string? ActorId,
    DateTimeOffset OccurredAt, long StateVersion, JsonElement Details, string? PreviousHash, string EventHash);
internal sealed record OutboxRecord(Guid Id, string Type, DateTimeOffset OccurredAt, JsonElement Payload);

internal interface IAttendedSessionStore
{
    T Execute<T>(Func<SessionCollection, T> action);
    IReadOnlyCollection<SessionAggregate> Snapshot();
}

internal sealed class SessionCollection
{
    internal Dictionary<Guid, SessionAggregate> ById { get; } = [];
    internal Dictionary<string, Guid> ByCodeHash { get; } = new(StringComparer.Ordinal);
}

internal sealed class InMemoryAttendedSessionStore : IAttendedSessionStore
{
    private readonly object gate = new();
    private readonly SessionCollection sessions = new();

    public T Execute<T>(Func<SessionCollection, T> action)
    {
        lock (gate)
        {
            return action(sessions);
        }
    }

    public IReadOnlyCollection<SessionAggregate> Snapshot()
    {
        lock (gate)
        {
            return sessions.ById.Values.ToArray();
        }
    }
}
