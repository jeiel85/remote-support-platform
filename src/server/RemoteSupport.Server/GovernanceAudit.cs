using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RemoteSupport.Server;

internal static class GovernanceAudit
{
    public static GovernanceAuditRecord Append(TenantAggregate tenant, TenantActor actor, string category,
        string action, string outcome, string? targetType, string? targetId, object details,
        DateTimeOffset occurredAt, Guid? correlationId = null)
    {
        AuditVerificationContract verification = Verify(tenant, occurredAt);
        if (!verification.Valid)
            throw new ControlPlaneException(503, "AUDIT_CHAIN_INVALID", "The tenant audit chain failed verification.");
        long sequence = tenant.AuditEvents.Count == 0
            ? (tenant.AuditCheckpoint?.LastPurgedSequence ?? 0) + 1
            : tenant.AuditEvents[^1].Sequence + 1;
        string? previousHash = tenant.AuditEvents.Count == 0
            ? tenant.AuditCheckpoint?.LastPurgedHash
            : tenant.AuditEvents[^1].EventHash;
        JsonElement detailElement = JsonSerializer.SerializeToElement(details);
        Guid id = Guid.CreateVersion7(occurredAt);
        Guid correlation = correlationId ?? Guid.CreateVersion7(occurredAt);
        string hash = ComputeHash(id, tenant.Tenant.Id, sequence, category, action, outcome, actor.ActorType,
            actor.Subject, targetType, targetId, correlation, occurredAt, detailElement, previousHash);
        GovernanceAuditRecord record = new(id, tenant.Tenant.Id, sequence, category, action, outcome,
            actor.ActorType, actor.Subject, targetType, targetId, correlation, occurredAt,
            detailElement, previousHash, hash);
        tenant.AuditEvents.Add(record);
        return record;
    }

    public static AuditVerificationContract Verify(TenantAggregate tenant, DateTimeOffset verifiedAt)
    {
        long expectedSequence = (tenant.AuditCheckpoint?.LastPurgedSequence ?? 0) + 1;
        string? previousHash = tenant.AuditCheckpoint?.LastPurgedHash;
        foreach (GovernanceAuditRecord record in tenant.AuditEvents.OrderBy(value => value.Sequence))
        {
            if (record.Sequence != expectedSequence)
                return Invalid(expectedSequence, "AUDIT_SEQUENCE_GAP", verifiedAt);
            if (!string.Equals(record.PreviousHash, previousHash, StringComparison.Ordinal))
                return Invalid(record.Sequence, "AUDIT_PREVIOUS_HASH_MISMATCH", verifiedAt);
            string computed = ComputeHash(record.Id, record.TenantId, record.Sequence, record.Category,
                record.Action, record.Outcome, record.ActorType, record.ActorId, record.TargetType,
                record.TargetId, record.CorrelationId, record.OccurredAt, record.Details, record.PreviousHash);
            if (!FixedHex(computed, record.EventHash))
                return Invalid(record.Sequence, "AUDIT_EVENT_HASH_MISMATCH", verifiedAt);
            expectedSequence++;
            previousHash = record.EventHash;
        }
        return new AuditVerificationContract(true, expectedSequence - 1, null, null, verifiedAt);
    }

    public static string ExportJsonLines(TenantAggregate tenant)
    {
        AuditVerificationContract verification = Verify(tenant, DateTimeOffset.UtcNow);
        if (!verification.Valid)
            throw new ControlPlaneException(503, "AUDIT_CHAIN_INVALID", "The tenant audit chain failed verification.");
        StringBuilder builder = new();
        foreach (GovernanceAuditRecord record in tenant.AuditEvents.OrderBy(value => value.Sequence))
        {
            builder.Append(JsonSerializer.Serialize(ToContract(record)));
            builder.Append('\n');
        }
        return builder.ToString();
    }

    public static AuditEventContract ToContract(GovernanceAuditRecord record) => new(record.Id,
        record.Sequence, record.Category, record.Action, record.Outcome, record.ActorType, record.ActorId,
        record.TargetType, record.TargetId, record.OccurredAt, record.CorrelationId, record.Details,
        record.PreviousHash, record.EventHash);

    private static string ComputeHash(Guid id, Guid tenantId, long sequence, string category, string action,
        string outcome, string actorType, string? actorId, string? targetType, string? targetId,
        Guid correlationId, DateTimeOffset occurredAt, JsonElement details, string? previousHash)
    {
        JsonElement canonicalEvent = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1,
            id,
            tenantId,
            chainScope = $"tenant:{tenantId:D}",
            chainSequence = sequence,
            category,
            action,
            outcome,
            actor = new { type = actorType, id = actorId },
            target = targetType is null || targetId is null ? null : new { type = targetType, id = targetId },
            correlationId,
            occurredAt,
            details,
            previousHash,
        });
        byte[] domain = Encoding.UTF8.GetBytes("RSP-AUDIT-EVENT-V1\0");
        byte[] canonical = ControlPlaneCrypto.Canonicalize(canonicalEvent);
        byte[] payload = new byte[domain.Length + canonical.Length];
        domain.CopyTo(payload, 0);
        canonical.CopyTo(payload, domain.Length);
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }

    private static AuditVerificationContract Invalid(long sequence, string code, DateTimeOffset verifiedAt) =>
        new(false, sequence - 1, sequence, code, verifiedAt);

    private static bool FixedHex(string left, string right)
    {
        if (left.Length != right.Length) return false;
        byte[] leftBytes = Encoding.ASCII.GetBytes(left);
        byte[] rightBytes = Encoding.ASCII.GetBytes(right);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}

