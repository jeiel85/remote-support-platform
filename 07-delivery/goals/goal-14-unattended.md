# Goal 14 — Managed Unattended Access

## Goal objective

Add unattended access as a separate high-risk product capability after the Managed Host release is stable and its production evidence is approved.

## Required work

1. Create a new threat model and privacy/abuse review.
2. Implement explicit local/admin enrollment and discoverable status.
3. Require MFA/step-up and policy for each unattended session.
4. Add access schedules, device groups, revocation and notifications.
5. Implement service-driven agent lifecycle and reconnect.
6. Add live revocation and account/device compromise controls.
7. Run dedicated penetration test and social-abuse review.
8. Pilot only with approved tenants before general availability.

## Acceptance criteria

- Unattended is disabled by default and absent from portable agent.
- Enrollment requires local admin or managed deployment evidence.
- Every access is policy-bound, MFA-protected, visible/audited and revocable.
- Revocation terminates or blocks sessions within defined bounds.
- No hidden indicator or covert operation.
- Dedicated security/abuse test has no unresolved blocker.
- Tenant can enumerate all unattended-enabled devices.

## Forbidden shortcuts

- No password-only unattended access.
- No global shared device secret.
- No silent enrollment through a normal attended session.
- No secure-desktop/logon-screen marketing claim without compatibility evidence.

## Required deliverables

- managed-host capability;
- admin controls and notifications;
- new threat model;
- dedicated penetration-test closure;
- pilot report;
- separate release gate approval.

## Audited contract inputs

The implementation for this goal must treat the following files as binding inputs:

- `04-backend/policy-engine.md`
- `04-backend/authorization-matrix.md`
- `03-client/elevation-broker.md`
- `01-architecture/runtime-sequences.md`
- `05-security/threat-model.md`

## Evidence package

The goal is complete only when the repository contains:

- runnable implementation and deterministic setup instructions;
- automated tests mapped to the applicable IDs in `07-delivery/acceptance-test-catalog.md`;
- performance/security evidence required by the goal acceptance criteria;
- updated contract, ADR, risk, and compatibility records when behavior differs from the design;
- no unresolved placeholder implementation, disabled security gate, or undocumented manual step.


This goal depends on Goal 13 Managed Host Foundation and has its own security, privacy, abuse, legal, and release approval.
