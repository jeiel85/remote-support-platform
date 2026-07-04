using System.Text.Json;
using Npgsql;

namespace RemoteSupport.Server;

internal sealed class PostgresMigrationRunner(NpgsqlDataSource dataSource, IWebHostEnvironment environment) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        string migrationDirectory = Path.Combine(environment.ContentRootPath, "Migrations");
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (NpgsqlCommand bootstrap = new("""
            create table if not exists control_plane_schema_migrations (
                version text primary key,
                applied_at timestamptz not null
            )
            """, connection, transaction))
        {
            await bootstrap.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (string path in Directory.EnumerateFiles(migrationDirectory, "*.sql").Order(StringComparer.Ordinal))
        {
            string version = Path.GetFileName(path);
            await using NpgsqlCommand exists = new("select exists(select 1 from control_plane_schema_migrations where version = $1)", connection, transaction);
            exists.Parameters.AddWithValue(version);
            if ((bool)(await exists.ExecuteScalarAsync(cancellationToken) ?? false)) continue;
            string sql = await File.ReadAllTextAsync(path, cancellationToken);
            await using NpgsqlCommand migration = new(sql, connection, transaction) { CommandTimeout = 120 };
            await migration.ExecuteNonQueryAsync(cancellationToken);
            await using NpgsqlCommand record = new("insert into control_plane_schema_migrations(version, applied_at) values ($1, now())", connection, transaction);
            record.Parameters.AddWithValue(version);
            await record.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class PostgresAttendedSessionStore(NpgsqlDataSource dataSource) : IAttendedSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object gate = new();

    public T Execute<T>(Func<SessionCollection, T> action)
    {
        lock (gate)
        {
            using NpgsqlConnection connection = dataSource.OpenConnection();
            using NpgsqlTransaction transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
            using (NpgsqlCommand advisory = new("select pg_advisory_xact_lock(727783671)", connection, transaction)) advisory.ExecuteNonQuery();
            SessionCollection sessions = Load(connection, transaction);
            T result = action(sessions);
            Persist(connection, transaction, sessions);
            transaction.Commit();
            return result;
        }
    }

    public IReadOnlyCollection<SessionAggregate> Snapshot()
    {
        lock (gate)
        {
            using NpgsqlConnection connection = dataSource.OpenConnection();
            using NpgsqlTransaction transaction = connection.BeginTransaction();
            SessionCollection sessions = Load(connection, transaction);
            transaction.Commit();
            return sessions.ById.Values.ToArray();
        }
    }

    private static SessionCollection Load(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        SessionCollection sessions = new();
        using NpgsqlCommand command = new("select document::text from attended_session_aggregates order by id for update", connection, transaction);
        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            SessionAggregate aggregate = JsonSerializer.Deserialize<SessionAggregate>(reader.GetString(0), JsonOptions)
                ?? throw new InvalidOperationException("Stored attended-session aggregate was invalid.");
            sessions.ById.Add(aggregate.Id, aggregate);
            if (aggregate.CodeHash is not null) sessions.ByCodeHash.Add(aggregate.CodeHash, aggregate.Id);
        }
        return sessions;
    }

    private static void Persist(NpgsqlConnection connection, NpgsqlTransaction transaction, SessionCollection sessions)
    {
        foreach (SessionAggregate session in sessions.ById.Values)
        {
            string document = JsonSerializer.Serialize(session, JsonOptions);
            using NpgsqlCommand upsert = new("""
                insert into attended_session_aggregates(id, tenant_id, state, state_version, code_lookup_hash, expires_at, document, updated_at)
                values ($1, $2, $3, $4, decode($5, 'hex'), $6, $7::jsonb, now())
                on conflict (id) do update set tenant_id = excluded.tenant_id, state = excluded.state,
                    state_version = excluded.state_version, code_lookup_hash = excluded.code_lookup_hash,
                    expires_at = excluded.expires_at, document = excluded.document, updated_at = now()
                """, connection, transaction);
            upsert.Parameters.AddWithValue(session.Id);
            upsert.Parameters.AddWithValue((object?)session.TenantId ?? DBNull.Value);
            upsert.Parameters.AddWithValue(session.State);
            upsert.Parameters.AddWithValue(session.StateVersion);
            upsert.Parameters.AddWithValue((object?)session.CodeHash ?? DBNull.Value);
            upsert.Parameters.AddWithValue(session.ExpiresAt);
            upsert.Parameters.AddWithValue(document);
            upsert.ExecuteNonQuery();
            foreach (AuditRecord audit in session.AuditEvents)
            {
                using NpgsqlCommand insertAudit = new("""
                    insert into attended_audit_events(id, session_id, tenant_id, chain_sequence, action, outcome, actor_type, actor_id,
                        occurred_at, state_version, details, previous_hash, event_hash)
                    values ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11::jsonb,
                        case when $12 is null then null else decode($12, 'hex') end, decode($13, 'hex')) on conflict (id) do nothing
                    """, connection, transaction);
                insertAudit.Parameters.AddWithValue(audit.Id);
                insertAudit.Parameters.AddWithValue(session.Id);
                insertAudit.Parameters.AddWithValue((object?)session.TenantId ?? DBNull.Value);
                insertAudit.Parameters.AddWithValue(audit.Sequence);
                insertAudit.Parameters.AddWithValue(audit.Action);
                insertAudit.Parameters.AddWithValue(audit.Outcome);
                insertAudit.Parameters.AddWithValue(audit.ActorType);
                insertAudit.Parameters.AddWithValue((object?)audit.ActorId ?? DBNull.Value);
                insertAudit.Parameters.AddWithValue(audit.OccurredAt);
                insertAudit.Parameters.AddWithValue(audit.StateVersion);
                insertAudit.Parameters.AddWithValue(audit.Details.GetRawText());
                insertAudit.Parameters.AddWithValue((object?)audit.PreviousHash ?? DBNull.Value);
                insertAudit.Parameters.AddWithValue(audit.EventHash);
                insertAudit.ExecuteNonQuery();
            }
            foreach (OutboxRecord outbox in session.OutboxMessages)
            {
                using NpgsqlCommand insertOutbox = new("""
                    insert into outbox_messages(id, tenant_id, occurred_at, type, payload, attempts, next_attempt_at)
                    values ($1,$2,$3,$4,$5::jsonb,0,$3) on conflict (id) do nothing
                    """, connection, transaction);
                insertOutbox.Parameters.AddWithValue(outbox.Id);
                insertOutbox.Parameters.AddWithValue((object?)session.TenantId ?? DBNull.Value);
                insertOutbox.Parameters.AddWithValue(outbox.OccurredAt);
                insertOutbox.Parameters.AddWithValue(outbox.Type);
                insertOutbox.Parameters.AddWithValue(outbox.Payload.GetRawText());
                insertOutbox.ExecuteNonQuery();
            }
        }
    }
}
