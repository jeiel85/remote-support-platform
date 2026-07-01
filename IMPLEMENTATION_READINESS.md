# Implementation Readiness

## Decision

**Begin Goal 01.** The architecture-significant contracts are sufficiently specified to create the repository, generated clients/contracts, database migrations, server modules, Windows clients and test harnesses without reopening the core product/security model.

## Goal 01 completion prerequisites

- Protected Windows x64 and arm64 workers, pinned .NET/MSVC/Windows SDK and reproducible native WebRTC build.
- Contract generation/compatibility checks for OpenAPI, Protobuf, JSON Schemas and native ABI.
- Disposable PostgreSQL migration, constraint, transaction and RLS tests using at least two tenant identities and separate platform-worker roles.
- Working PowerShell bootstrap on a clean supported Windows worker.
- Dependency locks, SBOM/provenance, secret scanning and artifact signing design.
- Named Product, Security, Privacy, Operations and Legal owners.

## Stop-and-redesign conditions

Create or update an ADR before continuing when target-environment evidence contradicts an assumption about capture fallback, encoder availability, WebRTC fingerprint access, DPoP proxy URI reconstruction, Service/IPC identity, device-key storage, updater trust root, PostgreSQL/RLS behavior or reboot continuity. Consent, sender-constrained authorization, transport binding, signing, tenant isolation and audit integrity cannot be weakened as a schedule shortcut.

## Release boundaries

- **Attended GA:** Goals 01–12; Portable Agent and Operator Console; no privileged service or unattended capability in released packages.
- **Managed Host:** Goal 13 after attended GA; installed service, device identity/command delivery and managed attended/reboot continuity.
- **Unattended GA:** Goal 14 after Managed Host evidence; separate MFA, policy, notification, abuse, legal and external-security approval.
