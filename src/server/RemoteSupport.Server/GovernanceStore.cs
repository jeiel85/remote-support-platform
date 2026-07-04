using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;

namespace RemoteSupport.Server;

internal sealed class TenantAggregate
{
    public required TenantRecord Tenant { get; set; }
    public required TenantSettingsRecord Settings { get; set; }
    public Dictionary<Guid, MembershipRecord> Memberships { get; init; } = [];
    public Dictionary<Guid, InvitationRecord> Invitations { get; init; } = [];
    public Dictionary<Guid, DeviceRecord> Devices { get; init; } = [];
    public Dictionary<Guid, EnrollmentTokenRecord> EnrollmentTokens { get; init; } = [];
    public Dictionary<Guid, PolicyRecord> Policies { get; init; } = [];
    public Dictionary<Guid, PolicyDecisionRecord> PolicyDecisions { get; init; } = [];
    public Dictionary<Guid, DataExportRecord> DataExports { get; init; } = [];
    public Dictionary<Guid, ClosureRecord> ClosureRequests { get; init; } = [];
    public Dictionary<Guid, SupportGrantRecord> SupportGrants { get; init; } = [];
    public Dictionary<string, IdempotencyRecord> Idempotency { get; init; } = new(StringComparer.Ordinal);
    public AuditCheckpointRecord? AuditCheckpoint { get; set; }
    [JsonIgnore]
    public List<GovernanceAuditRecord> AuditEvents { get; set; } = [];
}

internal sealed record TenantRecord(Guid Id, string Name, string Slug, string Status, string PlanCode,
    string DataRegion, long AuthorizationVersion, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt);
internal sealed record TenantSettingsRecord(long Version, int RetentionDays, string[] AllowedFeatures,
    long FileSizeLimitBytes, bool RecordingEnabled, DateTimeOffset UpdatedAt);
internal sealed record MembershipRecord(Guid UserId, string ExternalSubject, string DisplayName, string? Email,
    string[] Roles, string Status, long PrivilegeVersion, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? RemovedAt);
internal sealed record InvitationRecord(Guid Id, string Email, string[] Roles, string TokenHash, string Status,
    Guid InvitedByUserId, DateTimeOffset ExpiresAt, DateTimeOffset CreatedAt, Guid? AcceptedByUserId,
    DateTimeOffset? AcceptedAt, DateTimeOffset? RevokedAt);
internal sealed record DeviceRecord(Guid Id, Guid InstallationId, string DisplayName, string Architecture,
    string OsVersion, string AppVersion, string CredentialHash, string Status, long AuthorizationVersion,
    DateTimeOffset EnrolledAt, DateTimeOffset UpdatedAt, DateTimeOffset? LastSeenAt, DateTimeOffset? RevokedAt,
    string EnrollmentIdempotencyHash, int ActiveKeyVersion, Dictionary<int, DeviceKeyRecord> Keys)
{
    public string? ServiceState { get; init; }
    public int InteractiveSessions { get; init; }
    public bool UnattendedEnabled { get; init; }
    public Dictionary<Guid, DeviceCredentialChallengeRecord> CredentialChallenges { get; init; } = [];
    public Dictionary<string, DpopReplayRecord> DpopReplays { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<Guid, UnattendedEnrollmentRequestRecord> UnattendedEnrollmentRequests { get; init; } = [];
    public DeviceKeyRecord ActiveKey => Keys[ActiveKeyVersion];
}
internal sealed record DeviceKeyRecord(int Version, string PublicJwk, string KeyThumbprint, string Status,
    DateTimeOffset ValidFrom, DateTimeOffset? ValidUntil, DateTimeOffset? RevokedAt);
internal sealed record DeviceCredentialChallengeRecord(Guid Id, int KeyVersion, string Purpose, string NonceHash,
    DateTimeOffset ExpiresAt, DateTimeOffset? ConsumedAt);
internal sealed record UnattendedEnrollmentRequestRecord(Guid Id, string ConfirmationCodeHash, Guid RequestedByUserId,
    DateTimeOffset ExpiresAt, DateTimeOffset? ConfirmedAt, DateTimeOffset? RevokedAt);
internal sealed record EnrollmentTokenRecord(Guid Id, string TokenHash, Guid CreatedByUserId, int MaximumUses,
    int UseCount, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt, DateTimeOffset CreatedAt);
internal sealed record PolicyRecord(Guid Id, string Name, string? Description, string Status, int? ActiveVersion,
    long ResourceVersion, Guid CreatedByUserId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    Dictionary<int, PolicyVersionRecord> Versions);
internal sealed record PolicyVersionRecord(int Version, JsonElement Document, string DocumentHash,
    Guid CreatedByUserId, DateTimeOffset CreatedAt, DateTimeOffset? ActivatedAt);
internal sealed record PolicyDecisionRecord(Guid Id, Guid UserId, Guid? DeviceId, string InputHash,
    PolicyDecisionContract Decision, DateTimeOffset EvaluatedAt);
internal sealed record DataExportRecord(Guid Id, Guid RequestedByUserId, string Format, string State,
    DateTimeOffset RequestedAt, DateTimeOffset? CompletedAt, DateTimeOffset? DownloadExpiresAt,
    string? ObjectKey, string? ObjectSha256, DateTimeOffset? DownloadedAt, string? FailureCode);
internal sealed record ClosureRecord(Guid Id, Guid RequestedByUserId, string State, DateTimeOffset RequestedAt,
    DateTimeOffset EffectiveAt, DateTimeOffset ReauthenticatedAt, DateTimeOffset? CancelledAt,
    DateTimeOffset? CompletedAt, long StateVersion);
internal sealed record SupportGrantRecord(Guid Id, string SupportSubject, string ApproverSubject,
    string ReasonCode, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, bool BreakGlass);
internal sealed record IdempotencyRecord(string RequestHash, string ResourceType, Guid ResourceId);
internal sealed record AuditCheckpointRecord(long LastPurgedSequence, string LastPurgedHash,
    DateTimeOffset CreatedAt);
internal sealed record GovernanceAuditRecord(Guid Id, Guid TenantId, long Sequence, string Category,
    string Action, string Outcome, string ActorType, string? ActorId, string? TargetType, string? TargetId,
    Guid CorrelationId, DateTimeOffset OccurredAt, JsonElement Details, string? PreviousHash, string EventHash);

internal interface IGovernanceStore
{
    bool TryCreate(TenantAggregate aggregate);
    T Execute<T>(Guid tenantId, Func<TenantAggregate, T> action);
    TenantAggregate? Snapshot(Guid tenantId);
    Guid? FindTenantBySecret(string purpose, string secretHash);
    Guid? FindTenantByDeviceId(Guid deviceId);
    IReadOnlyList<Guid> ListTenantIdsForMaintenance();
}

internal sealed class InMemoryGovernanceStore : IGovernanceStore
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, TenantAggregate> tenants = [];

    public bool TryCreate(TenantAggregate aggregate)
    {
        lock (gate)
        {
            if (tenants.ContainsKey(aggregate.Tenant.Id) || tenants.Values.Any(value =>
                    string.Equals(value.Tenant.Slug, aggregate.Tenant.Slug, StringComparison.Ordinal))) return false;
            tenants.Add(aggregate.Tenant.Id, aggregate);
            return true;
        }
    }

    public T Execute<T>(Guid tenantId, Func<TenantAggregate, T> action)
    {
        lock (gate)
        {
            if (!tenants.TryGetValue(tenantId, out TenantAggregate? tenant))
                throw new ControlPlaneException(404, "RESOURCE_NOT_FOUND", "The resource was not found.");
            return action(tenant);
        }
    }

    public TenantAggregate? Snapshot(Guid tenantId)
    {
        lock (gate) return tenants.GetValueOrDefault(tenantId);
    }

    public Guid? FindTenantBySecret(string purpose, string secretHash)
    {
        lock (gate)
        {
            foreach ((Guid tenantId, TenantAggregate tenant) in tenants)
            {
                bool match = purpose switch
                {
                    "INVITATION" => tenant.Invitations.Values.Any(value =>
                        string.Equals(value.TokenHash, secretHash, StringComparison.Ordinal)),
                    "ENROLLMENT" => tenant.EnrollmentTokens.Values.Any(value =>
                        string.Equals(value.TokenHash, secretHash, StringComparison.Ordinal)),
                    _ => false,
                };
                if (match) return tenantId;
            }
            return null;
        }
    }

    public Guid? FindTenantByDeviceId(Guid deviceId)
    {
        lock (gate)
        {
            foreach ((Guid tenantId, TenantAggregate tenant) in tenants)
                if (tenant.Devices.ContainsKey(deviceId)) return tenantId;
            return null;
        }
    }

    public IReadOnlyList<Guid> ListTenantIdsForMaintenance()
    {
        lock (gate) return tenants.Keys.ToArray();
    }
}

internal sealed class PostgresGovernanceStore(NpgsqlDataSource dataSource) : IGovernanceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool TryCreate(TenantAggregate aggregate)
    {
        using NpgsqlConnection connection = dataSource.OpenConnection();
        using NpgsqlTransaction transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
        SetTenantContext(connection, transaction, aggregate.Tenant.Id);
        try
        {
            using NpgsqlCommand command = new("""
                insert into governance_tenant_aggregates(tenant_id, slug, status, authorization_version, document, updated_at)
                values ($1,$2,$3,$4,$5::jsonb,$6)
                """, connection, transaction);
            command.Parameters.AddWithValue(aggregate.Tenant.Id);
            command.Parameters.AddWithValue(aggregate.Tenant.Slug);
            command.Parameters.AddWithValue(aggregate.Tenant.Status);
            command.Parameters.AddWithValue(aggregate.Tenant.AuthorizationVersion);
            command.Parameters.AddWithValue(JsonSerializer.Serialize(aggregate, JsonOptions));
            command.Parameters.AddWithValue(aggregate.Tenant.UpdatedAt);
            command.ExecuteNonQuery();
            PersistAuditAndSecrets(connection, transaction, aggregate);
            transaction.Commit();
            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            transaction.Rollback();
            return false;
        }
    }

    public T Execute<T>(Guid tenantId, Func<TenantAggregate, T> action)
    {
        using NpgsqlConnection connection = dataSource.OpenConnection();
        using NpgsqlTransaction transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
        SetTenantContext(connection, transaction, tenantId);
        TenantAggregate aggregate = Load(connection, transaction, tenantId, true) ??
            throw new ControlPlaneException(404, "RESOURCE_NOT_FOUND", "The resource was not found.");
        T result = action(aggregate);
        using NpgsqlCommand update = new("""
            update governance_tenant_aggregates set status=$2, authorization_version=$3,
                document=$4::jsonb, updated_at=$5 where tenant_id=$1
            """, connection, transaction);
        update.Parameters.AddWithValue(tenantId);
        update.Parameters.AddWithValue(aggregate.Tenant.Status);
        update.Parameters.AddWithValue(aggregate.Tenant.AuthorizationVersion);
        update.Parameters.AddWithValue(JsonSerializer.Serialize(aggregate, JsonOptions));
        update.Parameters.AddWithValue(aggregate.Tenant.UpdatedAt);
        if (update.ExecuteNonQuery() != 1) throw new InvalidOperationException("Tenant persistence lost its tenant context.");
        PersistAuditAndSecrets(connection, transaction, aggregate);
        transaction.Commit();
        return result;
    }

    public TenantAggregate? Snapshot(Guid tenantId)
    {
        using NpgsqlConnection connection = dataSource.OpenConnection();
        using NpgsqlTransaction transaction = connection.BeginTransaction();
        SetTenantContext(connection, transaction, tenantId);
        TenantAggregate? aggregate = Load(connection, transaction, tenantId, false);
        transaction.Commit();
        return aggregate;
    }

    public Guid? FindTenantBySecret(string purpose, string secretHash)
    {
        using NpgsqlConnection connection = dataSource.OpenConnection();
        using NpgsqlCommand command = new("""
            select tenant_id from governance_secret_lookups
            where purpose=$1 and secret_hash=decode($2,'hex') and expires_at > now() and revoked_at is null
            """, connection);
        command.Parameters.AddWithValue(purpose);
        command.Parameters.AddWithValue(secretHash);
        return command.ExecuteScalar() as Guid?;
    }

    public Guid? FindTenantByDeviceId(Guid deviceId)
    {
        using NpgsqlConnection connection = dataSource.OpenConnection();
        using NpgsqlCommand command = new(
            "select tenant_id from governance_device_lookups where device_id=$1", connection);
        command.Parameters.AddWithValue(deviceId);
        return command.ExecuteScalar() as Guid?;
    }

    public IReadOnlyList<Guid> ListTenantIdsForMaintenance()
    {
        using NpgsqlConnection connection = dataSource.OpenConnection();
        using NpgsqlCommand command = new("select tenant_id from governance_tenant_aggregates order by tenant_id", connection);
        using NpgsqlDataReader reader = command.ExecuteReader();
        List<Guid> result = [];
        while (reader.Read()) result.Add(reader.GetGuid(0));
        return result;
    }

    private static TenantAggregate? Load(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid tenantId, bool forUpdate)
    {
        string suffix = forUpdate ? " for update" : string.Empty;
        using NpgsqlCommand command = new(
            "select document::text from governance_tenant_aggregates where tenant_id=$1" + suffix,
            connection, transaction);
        command.Parameters.AddWithValue(tenantId);
        string? document = command.ExecuteScalar() as string;
        if (document is null) return null;
        TenantAggregate aggregate = JsonSerializer.Deserialize<TenantAggregate>(document, JsonOptions) ??
            throw new InvalidOperationException("Stored governance aggregate was invalid.");
        aggregate.AuditEvents = LoadAudit(connection, transaction, tenantId);
        return aggregate;
    }

    private static List<GovernanceAuditRecord> LoadAudit(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid tenantId)
    {
        using NpgsqlCommand command = new("""
            select id, chain_sequence, category, action, outcome, actor_type, actor_id,
                target_type, target_id, correlation_id, occurred_at, details::text,
                encode(previous_hash,'hex'), encode(event_hash,'hex')
            from governance_audit_events where tenant_id=$1 order by chain_sequence
            """, connection, transaction);
        command.Parameters.AddWithValue(tenantId);
        using NpgsqlDataReader reader = command.ExecuteReader();
        List<GovernanceAuditRecord> result = [];
        while (reader.Read())
        {
            using JsonDocument details = JsonDocument.Parse(reader.GetString(11));
            result.Add(new GovernanceAuditRecord(reader.GetGuid(0), tenantId, reader.GetInt64(1),
                reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetGuid(9), reader.GetFieldValue<DateTimeOffset>(10),
                details.RootElement.Clone(), reader.IsDBNull(12) ? null : reader.GetString(12), reader.GetString(13)));
        }
        return result;
    }

    private static void PersistAuditAndSecrets(NpgsqlConnection connection, NpgsqlTransaction transaction,
        TenantAggregate aggregate)
    {
        foreach (GovernanceAuditRecord audit in aggregate.AuditEvents)
        {
            using NpgsqlCommand insert = new("""
                insert into governance_audit_events(id, tenant_id, chain_sequence, category, action, outcome,
                    actor_type, actor_id, target_type, target_id, correlation_id, occurred_at, details,
                    previous_hash, event_hash)
                values ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13::jsonb,
                    case when $14 is null then null else decode($14,'hex') end,decode($15,'hex'))
                on conflict (id) do nothing
                """, connection, transaction);
            insert.Parameters.AddWithValue(audit.Id);
            insert.Parameters.AddWithValue(audit.TenantId);
            insert.Parameters.AddWithValue(audit.Sequence);
            insert.Parameters.AddWithValue(audit.Category);
            insert.Parameters.AddWithValue(audit.Action);
            insert.Parameters.AddWithValue(audit.Outcome);
            insert.Parameters.AddWithValue(audit.ActorType);
            insert.Parameters.AddWithValue((object?)audit.ActorId ?? DBNull.Value);
            insert.Parameters.AddWithValue((object?)audit.TargetType ?? DBNull.Value);
            insert.Parameters.AddWithValue((object?)audit.TargetId ?? DBNull.Value);
            insert.Parameters.AddWithValue(audit.CorrelationId);
            insert.Parameters.AddWithValue(audit.OccurredAt);
            insert.Parameters.AddWithValue(audit.Details.GetRawText());
            insert.Parameters.AddWithValue((object?)audit.PreviousHash ?? DBNull.Value);
            insert.Parameters.AddWithValue(audit.EventHash);
            insert.ExecuteNonQuery();
        }

        using (NpgsqlCommand clear = new("delete from governance_secret_lookups where tenant_id=$1", connection, transaction))
        {
            clear.Parameters.AddWithValue(aggregate.Tenant.Id);
            clear.ExecuteNonQuery();
        }
        foreach (InvitationRecord invitation in aggregate.Invitations.Values)
            InsertSecret(connection, transaction, aggregate.Tenant.Id, "INVITATION", invitation.TokenHash,
                invitation.Id, invitation.ExpiresAt, invitation.RevokedAt ?? invitation.AcceptedAt);
        foreach (EnrollmentTokenRecord token in aggregate.EnrollmentTokens.Values)
            InsertSecret(connection, transaction, aggregate.Tenant.Id, "ENROLLMENT", token.TokenHash,
                token.Id, token.ExpiresAt, token.RevokedAt ?? (token.UseCount >= token.MaximumUses ? token.ExpiresAt : null));
        foreach (Guid deviceId in aggregate.Devices.Keys)
        {
            using NpgsqlCommand insertDevice = new("""
                insert into governance_device_lookups(device_id, tenant_id) values ($1,$2)
                on conflict (device_id) do nothing
                """, connection, transaction);
            insertDevice.Parameters.AddWithValue(deviceId);
            insertDevice.Parameters.AddWithValue(aggregate.Tenant.Id);
            insertDevice.ExecuteNonQuery();
        }
    }

    private static void InsertSecret(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId,
        string purpose, string hash, Guid resourceId, DateTimeOffset expiresAt, DateTimeOffset? revokedAt)
    {
        using NpgsqlCommand insert = new("""
            insert into governance_secret_lookups(purpose, secret_hash, tenant_id, resource_id, expires_at, revoked_at)
            values ($1,decode($2,'hex'),$3,$4,$5,$6)
            """, connection, transaction);
        insert.Parameters.AddWithValue(purpose);
        insert.Parameters.AddWithValue(hash);
        insert.Parameters.AddWithValue(tenantId);
        insert.Parameters.AddWithValue(resourceId);
        insert.Parameters.AddWithValue(expiresAt);
        insert.Parameters.AddWithValue((object?)revokedAt ?? DBNull.Value);
        insert.ExecuteNonQuery();
    }

    private static void SetTenantContext(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId)
    {
        using NpgsqlCommand command = new("select set_config('app.tenant_id',$1,true)", connection, transaction);
        command.Parameters.AddWithValue(tenantId.ToString("D"));
        command.ExecuteNonQuery();
    }
}
