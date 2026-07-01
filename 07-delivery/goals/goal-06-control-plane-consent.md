# Goal 06 — Attended Control Plane and Consent

## Goal objective

Build the production-style session API, operator authentication boundary, one-time code flow and host consent state machine.

## Required work

1. Implement ASP.NET Core modular monolith foundation.
2. Add PostgreSQL migrations and outbox.
3. Implement attended session creation, code resolution and consent endpoints.
4. Integrate OIDC for operator sign-in.
5. Implement short-lived host and peer tokens.
6. Add code entropy, lookup hashing, expiry, attempt limits and generic failures.
7. Bind operator identity and requested scopes into host consent UI.
8. Implement audit events and contract tests.

## Acceptance criteria

- Code alone cannot authorize a session.
- Replay and expired requests fail.
- Concurrent consent/state transitions use version checks.
- Host sees verified operator/tenant identity and exact scopes.
- Authorization tokens are peer/session/scope bound.
- Audit outbox records all lifecycle transitions.
- API security and rate-limit tests pass.

## Forbidden shortcuts

- No anonymous production operator access.
- No plaintext support-code database storage.
- No long-lived peer/TURN token.
- No authorization logic only in controllers/UI.

## Required deliverables

- working API and migrations;
- OIDC test integration;
- updated Agent consent UI;
- audit event evidence;
- OpenAPI contract tests;
- threat-model update.

## Audited contract inputs

The implementation for this goal must treat the following files as binding inputs:

- `01-architecture/runtime-sequences.md`
- `02-protocol/api-flow-contract.md`
- `02-protocol/openapi/openapi.yaml`
- `02-protocol/session-state-machine.md`
- `04-backend/database-schema.sql`

## Evidence package

The goal is complete only when the repository contains:

- runnable implementation and deterministic setup instructions;
- automated tests mapped to the applicable IDs in `07-delivery/acceptance-test-catalog.md`;
- performance/security evidence required by the goal acceptance criteria;
- updated contract, ADR, risk, and compatibility records when behavior differs from the design;
- no unresolved placeholder implementation, disabled security gate, or undocumented manual step.
