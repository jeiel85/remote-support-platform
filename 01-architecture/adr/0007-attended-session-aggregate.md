# ADR-0007: Transactional Attended-Session Aggregate

- Status: Accepted
- Date: 2026-07-02

## Decision

The Sessions module persists the attended command model as a versioned JSONB
aggregate with indexed session ID, state, version, keyed support-code lookup hash
and expiry columns. Append-only audit-chain rows and outbox rows are written in
the same serializable PostgreSQL transaction. Public peer keys and verified
display metadata may be present in the aggregate; raw support codes, bootstrap
tokens, proof nonces, signatures and peer tokens may not be persisted.

The normalized `support_sessions`, participant and proof tables remain the
cross-module reporting/projection contract. They are not read directly by the
Sessions command handler. Projection delivery is idempotent through the outbox.

## Rationale

Consent is one small, concurrency-sensitive aggregate. A single versioned
document keeps code lookup, bootstrap use counters, single-use proof state and
optimistic state transitions atomic without cross-module table reads. Indexed
columns preserve expiry and abuse-query efficiency; append-only audit/outbox
tables preserve independent operational evidence.

## Constraints

- PostgreSQL transactions use serializable isolation and an advisory lock in
  the initial single-region implementation.
- Schema changes are forward-only migrations; aggregate JSON readers tolerate
  additive fields.
- A later multi-region writer design must replace the coarse advisory lock with
  per-session compare-and-swap and retain the same state-version semantics.
- In-memory persistence and header-based OIDC test authentication are compiled
  only in Debug and accepted only in Development/Testing environments.
