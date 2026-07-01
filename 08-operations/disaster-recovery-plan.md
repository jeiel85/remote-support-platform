# Disaster Recovery Plan

## 1. Initial objectives

| Component | RPO | RTO | Notes |
|---|---:|---:|---|
| Control-plane PostgreSQL | 15 minutes | 4 hours | point-in-time recovery and encrypted backups |
| Audit/outbox data | 5 minutes | 4 hours | prioritize security-event durability |
| API/signaling containers | stateless | 1 hour | redeploy from immutable artifacts |
| TURN region | no durable session state | 30 minutes regional capacity restoration | active relay sessions may reconnect |
| Update artifacts/metadata | zero after publication | 4 hours | immutable replicated object storage |
| Signing keys | zero | incident-dependent | offline/root and online roles separated |

Objectives must be revised from provider capability and business impact before contractual commitments.

## 2. Backup scope

- PostgreSQL base backups and WAL/PITR;
- immutable update artifacts and signed metadata;
- infrastructure definitions and deployment configuration;
- encrypted secret/key references, not raw offline root key backups in application storage;
- tenant configuration, policy versions, and audit records;
- release evidence and SBOMs.

Screen, clipboard, chat, keystroke, and transferred-file content are not backup data because the control plane does not store them.

## 3. Restore validation

Quarterly before mature GA, and after material infrastructure changes:

1. restore into an isolated recovery environment;
2. verify schema migration level and integrity checks;
3. validate tenant isolation and sample authorization queries;
4. verify audit hash-chain continuity and outbox replay idempotency;
5. verify update metadata/artifact signatures;
6. execute a synthetic attended session without production customer content;
7. record achieved RPO/RTO and remediation items.

A backup is not considered valid until a restore drill succeeds.

## 4. Regional failure

- Stop issuing credentials for an unhealthy TURN region.
- Return alternate tested ICE server lists.
- Existing direct sessions continue independently of the control plane where transport permits.
- Existing relayed sessions are expected to reconnect; no seamless TURN allocation migration is assumed.
- Control-plane regional failover follows the database consistency model; split-brain writes are prohibited.

## 5. Security-related recovery

Compromise of signing, identity, database, or deployment credentials follows the security/signing runbooks. Availability restoration never precedes containment when doing so could distribute malicious updates or expose another tenant.
