# Goal 11 — Secure Update, Observability and Operations

## Goal objective

Make the system operable and safely updatable in production.

## Required work

1. Implement signed update metadata and artifact verification.
2. Add anti-rollback sequence, channels and staged rollout.
3. Implement updater health check and safe recovery.
4. Add OpenTelemetry traces/metrics/logs across client and server.
5. Build dashboards and alerts for connection, TURN, crash, update and security health.
6. Implement support bundle preview/redaction.
7. Complete backup/restore, outage, TURN and signing-key runbooks.
8. Run failure drills and document measured RPO/RTO/capacity.

## Acceptance criteria

- Compromised artifact storage alone cannot publish accepted update.
- Older signed metadata cannot force rollback.
- Bad canary release automatically stops promotion based on defined metrics.
- Logs contain no test-injected secrets/content.
- Backup restore and TURN replacement drills succeed.
- Alerts fire on simulated allocation failure, audit backlog and signature failure.
- Release evidence package is complete.

## Forbidden shortcuts

- No updater that trusts version string/HTTPS alone.
- No production dashboard with unbounded sensitive labels.
- No support bundle automatic upload without user/admin workflow.
- No undocumented production manual step.

## Required deliverables

- updater and release-verifier;
- signed release pipeline;
- dashboards/alerts;
- support bundle tool;
- completed runbooks;
- drill reports.

## Audited contract inputs

The implementation for this goal must treat the following files as binding inputs:

- `02-protocol/schemas/update-manifest.schema.json`
- `03-client/installer-updater.md`
- `08-operations/slo-alert-catalog.md`
- `08-operations/disaster-recovery-plan.md`
- `07-delivery/ci-cd.md`

## Evidence package

The goal is complete only when the repository contains:

- runnable implementation and deterministic setup instructions;
- automated tests mapped to the applicable IDs in `07-delivery/acceptance-test-catalog.md`;
- performance/security evidence required by the goal acceptance criteria;
- updated contract, ADR, risk, and compatibility records when behavior differs from the design;
- no unresolved placeholder implementation, disabled security gate, or undocumented manual step.
