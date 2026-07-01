# Goal 10 — Tenancy, Policy and Audit

## Goal objective

Implement commercial multi-tenant governance, managed devices, RBAC/policy and tamper-evident audit.

## Required work

1. Implement tenants, memberships, roles and device enrollment/revocation.
2. Implement policy evaluation with scope/condition output.
3. Require MFA claim for high-risk actions.
4. Add managed-device session request and local-consent policy mode.
5. Enforce tenant context in every repository/query.
6. Implement audit canonicalization/hash chaining and export hooks.
7. Add retention/deletion workers.
8. Build tenant-isolation and privilege-escalation suites.

## Acceptance criteria

- Cross-tenant IDs never disclose or mutate resources.
- Revoked user/device cannot obtain new credentials.
- Policy version/decision ID recorded with sessions.
- High-risk actions require fresh MFA.
- Audit verification detects modification/gaps.
- Tenant export/deletion workflows are testable.
- Support employee access follows JIT/break-glass design.

## Forbidden shortcuts

- No tenant ID accepted blindly from client without authenticated context.
- No role-only shortcut for resource policy.
- No audit details containing content/secrets.
- No unattended access yet unless explicitly limited to test-only disabled code.

## Required deliverables

- tenant/device/policy modules;
- admin API/portal minimum UI;
- tenant isolation report;
- audit verifier/export;
- retention/deletion evidence;
- updated privacy inventory.

## Audited contract inputs

The implementation for this goal must treat the following files as binding inputs:

- `04-backend/policy-engine.md`
- `04-backend/authorization-matrix.md`
- `04-backend/database-schema.sql`
- `04-backend/migration-policy.md`
- `02-protocol/schemas/audit-event.schema.json`
- `02-protocol/schemas/policy-document.schema.json`

## Evidence package

The goal is complete only when the repository contains:

- runnable implementation and deterministic setup instructions;
- automated tests mapped to the applicable IDs in `07-delivery/acceptance-test-catalog.md`;
- performance/security evidence required by the goal acceptance criteria;
- updated contract, ADR, risk, and compatibility records when behavior differs from the design;
- no unresolved placeholder implementation, disabled security gate, or undocumented manual step.

## Admin Portal contract

Implement the Blazor BFF and the tenant/settings/membership/invitation/data-export/closure APIs in `03-client/admin-portal.md` and OpenAPI. Portal completion is not satisfied by API-only endpoints.
