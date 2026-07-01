# Backend Structure

```text
src/server/
├─ RemoteSupport.Server.sln
├─ src/
│  ├─ ApiHost/
│  ├─ Modules/
│  │  ├─ Identity/
│  │  ├─ Tenancy/
│  │  ├─ Devices/
│  │  ├─ Sessions/
│  │  ├─ Signaling/
│  │  ├─ Policy/
│  │  ├─ TurnCredentials/
│  │  ├─ Audit/
│  │  ├─ Updates/
│  │  ├─ Metering/
│  │  ├─ Notifications/
│  │  └─ Abuse/
│  ├─ BuildingBlocks/
│  │  ├─ Persistence/
│  │  ├─ Messaging/
│  │  ├─ Security/
│  │  ├─ Observability/
│  │  └─ Web/
│  └─ Workers/
│     ├─ OutboxDispatcher/
│     ├─ RetentionWorker/
│     ├─ NotificationWorker/
│     └─ MeteringAggregator/
└─ tests/
   ├─ Unit/
   ├─ Integration/
   ├─ Contract/
   ├─ TenantIsolation/
   ├─ Security/
   └─ Load/
```

## Module contract

Each module exposes:

- public application commands/queries;
- domain events;
- database migrations it owns;
- authorization policy names;
- audit event mapping;
- telemetry names;
- test fixtures.

No module reads another module's tables directly. Read models use explicit projections or interfaces.

## Request pipeline

1. Correlation and trace context.
2. TLS/forwarded-header validation.
3. Authentication.
4. Tenant resolution.
5. Rate and abuse checks.
6. Input validation.
7. Authorization/policy.
8. Use case execution.
9. Audit/outbox transaction.
10. Response redaction and security headers.

## Background jobs

Jobs are idempotent, lease-based and observable. They have bounded batches, retry limits and dead-letter handling. Job payloads contain IDs, not sensitive object snapshots.
