# Runbook — Database Restore

## Preconditions

- encrypted backups and PITR enabled;
- restore credentials separated;
- target RPO/RTO documented;
- test environment available.

## Procedure

1. Declare maintenance/incident state.
2. Stop or fence writers to avoid split brain.
3. Select restore point and verify backup integrity.
4. Restore into isolated target.
5. Run schema and consistency checks.
6. Verify tenant counts, active sessions, audit/outbox continuity and key references.
7. Repoint canary control-plane instance.
8. Run session/auth/device smoke tests.
9. Promote or abort.
10. Reconcile outbox/external notifications idempotently.

## Required evidence

Record backup/base and WAL identifiers, requested and achieved recovery point,
writer-fence and first-healthy timestamps, measured RPO/RTO, schema version,
tenant-isolation query results, audit-chain verification, outbox replay counts,
synthetic attended-session result, operator and independent approver. A copied
backup without this isolated restore evidence is not a successful drill.

## Cautions

- Active session tokens issued after restore point may require global revocation.
- Audit chain discontinuity must be documented and investigated.
- Do not overwrite the only forensic copy of the failed database.
