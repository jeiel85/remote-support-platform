# Observability

## 1. Principles

- OpenTelemetry-compatible traces, metrics and structured logs.
- Correlation ID spans client, API, signaling and TURN credential issuance.
- Session ID may be logged as opaque ID; content and credentials may not.
- Cardinality is bounded: do not use user email/device name as metric labels.
- Client telemetry is transparent and configurable according to privacy policy.

## 2. Core traces

- session create → code resolve → consent → authorization;
- peer connect and route selection;
- policy evaluation;
- TURN credential issuance;
- file transfer lifecycle metadata;
- update check/stage/apply/health;
- device enrollment/revocation.

## 3. Alerts

- elevated authentication/code-guessing failures;
- signaling connect failure spike;
- TURN allocation failure or NIC/packet-drop pressure;
- audit outbox backlog;
- tenant-isolation/security exception;
- updater signature/hash failure;
- crash spike by release/OS/GPU;
- abnormal relay bandwidth or allocation churn.

## 4. Dashboards

- business/session health;
- connection quality;
- relay capacity/cost;
- client stability;
- update rollout;
- security/abuse;
- database/outbox health.

## 5. Diagnostic sampling

High-detail diagnostics are sampled and redacted. Raw SDP, IP detail and crash dumps have separate restricted access and short retention. Customer content is never added merely to simplify debugging.
