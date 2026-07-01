# Goal 12 — Attended Commercial GA Hardening

## Goal objective

Close all release gates for the attended commercial product and produce defensible release evidence.

## Required work

1. Execute full compatibility, performance, resilience and security suites.
2. Complete independent penetration test and remediate blockers.
3. Complete AV/EDR false-positive process and signed publisher validation.
4. Validate privacy notice, terms, DPA, abuse and support operations.
5. Run incident-response and update-key compromise tabletop exercises.
6. Perform staged beta and analyze real failure/cost metrics.
7. Set supported capability matrix and documented limitations.
8. Approve release only through the multi-owner release gate.

## Acceptance criteria

- Every checkbox in `release-gates.md` has evidence or an approved non-critical exception.
- No open critical/high exploitable security defect.
- Consent, scope, tenant isolation and updater integrity have zero exceptions.
- Measured SLO and capacity values support published claims.
- Customer-facing documentation accurately states UAC/secure-desktop limits.
- Abuse response and security contact are operational.
- Signed artifacts reproduce from approved commit.

## Forbidden shortcuts

- No marketing claim beyond validated compatibility.
- No waiver of core safety/security invariants.
- No hidden feature flags that enable unattended access in portable GA.
- No production launch without rollback and incident capability.

## Required deliverables

- GA candidate artifacts;
- release evidence index;
- penetration-test closure record;
- supported matrix;
- operational readiness review;
- final signed release and rollback package.

## Audited contract inputs

The implementation for this goal must treat the following files as binding inputs:

- `FINAL_AUDIT_REPORT.md`
- `06-quality/release-gates.md`
- `07-delivery/acceptance-test-catalog.md`
- `07-delivery/traceability/requirements-traceability.csv`
- `08-operations/operations-overview.md`

## Evidence package

The goal is complete only when the repository contains:

- runnable implementation and deterministic setup instructions;
- automated tests mapped to the applicable IDs in `07-delivery/acceptance-test-catalog.md`;
- performance/security evidence required by the goal acceptance criteria;
- updated contract, ADR, risk, and compatibility records when behavior differs from the design;
- no unresolved placeholder implementation, disabled security gate, or undocumented manual step.
