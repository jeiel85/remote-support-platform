# ADR-0006: Signed Update Metadata with Rollback Protection

- Status: Accepted
- Date: 2026-07-01

## Decision

All installers and binaries are code-signed. The updater additionally verifies signed release metadata, artifact hashes, channel, architecture, minimum version and monotonically increasing release sequence.

## Consequences

- Compromise of ordinary object storage cannot publish executable updates alone.
- Signing keys require separated roles and incident procedures.
- Emergency rollback uses a new signed release sequence rather than accepting older metadata.
