# Coding Standards

## C#/.NET

- nullable reference types enabled;
- warnings as errors for production projects;
- async all the way for I/O; no sync-over-async;
- cancellation tokens on bounded operations;
- immutable records/value objects for protocol/domain values;
- no exception-driven expected control flow;
- source-generated serialization where useful;
- structured logging with stable event IDs;
- authorization enforced in application use cases, not only controllers.

## C++

- C++20, RAII, no raw owning pointers;
- `std::span`, strong enums and explicit sizes;
- checked integer/size conversions;
- no exceptions across C ABI;
- bounds before memory copy;
- thread ownership and callback context documented;
- compiler hardening and static analysis;
- fuzzable parsers separated from OS side effects.

## Protocol

- length and count bounds on every untrusted collection;
- stable enum values and unknown-value handling;
- timestamps specify wall-clock vs monotonic semantics;
- idempotency and sequence behavior documented;
- no arbitrary JSON blobs in security-critical commands.

## Security

- no custom cryptographic algorithm;
- no secrets in logs/tests/fixtures;
- no arbitrary command or path from remote input;
- explicit allowlists for privileged operations;
- safe temporary files and atomic replacement;
- constant-time comparison for secret-derived values where applicable.

## Tests

- every bug adds a regression test where feasible;
- tests reference requirement IDs;
- property tests for state and coordinate transformations;
- security tests fail closed;
- flaky tests are quarantined only with owner and resolution issue, never ignored indefinitely.
