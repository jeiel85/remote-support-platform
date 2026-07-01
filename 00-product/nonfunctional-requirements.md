# Non-Functional Requirements

Requirement IDs are stable and must be referenced by tests, dashboards, release evidence, and operational reviews. Targets are engineering objectives until measured production data and commercial contracts establish formal SLA/SLO commitments.

## 1. Performance targets

| ID | Requirement |
|---|---|
| NFR-PERF-001 | Capture-to-display median latency is below 150 ms on a healthy direct connection in the reference lab. |
| NFR-PERF-002 | Input event round-trip p95 is below 200 ms on a healthy direct connection. |
| NFR-PERF-003 | Connection establishment p95 is below 10 seconds when either a direct or TURN route is available. |
| NFR-PERF-004 | Idle-desktop bitrate converges below 300 Kbps at 1080p when no meaningful visual change occurs. |
| NFR-PERF-005 | Active office-work bitrate remains adaptive, with a reference target range of 1–5 Mbps at 1080p. |
| NFR-PERF-006 | Client working-set target is below 500 MB for one 1080p session; exceptions require profiling evidence and release approval. |

## 2. Reliability and resilience

| ID | Requirement |
|---|---|
| NFR-REL-001 | Attended-GA control-plane monthly availability objective is at least 99.9%, excluding declared maintenance under the published policy. |
| NFR-REL-002 | Each production TURN region has at least two independently restartable relay nodes and a regional availability objective of at least 99.9%. |
| NFR-REL-003 | Client crash-free session objective is at least 99.5% before beta and 99.9% before GA, measured with a documented denominator and privacy-safe telemetry. |
| NFR-REL-004 | Active peer media continues during transient signaling loss when the established WebRTC transport remains valid, and state reconciles after signaling recovery. |
| NFR-REL-005 | Every resource-owning native module supports deterministic teardown, bounded shutdown, device-loss recovery, and watchdog-observable failure states. |
| NFR-REL-006 | Database backups, point-in-time recovery, and restore verification meet the RPO/RTO objectives in the disaster-recovery plan. |
| NFR-REL-007 | Client update failure cannot leave the last known-good signed version unlaunchable; rollback is automatic or explicitly recoverable. |

## 3. Security

| ID | Requirement |
|---|---|
| NFR-SEC-001 | All internet traffic uses authenticated encryption with approved protocol versions and cipher policy. |
| NFR-SEC-002 | Device identity, operator identity, tenant membership, and ephemeral peer identity are distinct trust subjects and are bound explicitly during authorization. |
| NFR-SEC-003 | Session authorization, signaling tickets, reconnect grants, and TURN credentials are short-lived, purpose-bound, audience-bound, and scope-bound where applicable. |
| NFR-SEC-004 | Long-lived TURN credentials are never distributed to clients. |
| NFR-SEC-005 | Secrets, bearer tokens, clipboard/chat contents, keystrokes, screen contents, and transferred-file contents never appear in application logs or telemetry. |
| NFR-SEC-006 | Every privileged or security-relevant state transition emits a stable audit event with actor, tenant, target, outcome, reason, correlation, and tamper-evident sequence data. |
| NFR-SEC-007 | Dependency, container, secret, static-analysis, and artifact-signature checks block release according to the secure-SDLC exception policy. |
| NFR-SEC-008 | LocalSystem/service IPC authenticates the connecting process and user session, authorizes each command, rejects replay, and exposes no generic command-execution primitive. |
| NFR-SEC-009 | Update metadata and artifacts are signature-verified, hash-verified, product/channel/architecture-bound, expiry-checked, and protected against rollback. |
| NFR-SEC-010 | Tenant isolation is enforced and tested at API, application, database-policy, background-job, cache-key, webhook, and audit-query boundaries. |
| NFR-SEC-011 | Before any screen, input, clipboard, chat, or file payload is accepted, both peers verify a signed transport binding covering the session, peer identities, granted scopes, permission revision, transport epoch, and negotiated DTLS fingerprints. |

## 4. Privacy and data governance

| ID | Requirement |
|---|---|
| NFR-PRV-001 | The control plane stores only the documented session, identity, device, security, billing, and operational metadata required by an approved data inventory. |
| NFR-PRV-002 | Screen frames, keystrokes, clipboard contents, chat contents, and transferred-file contents are not persisted by the SaaS control plane by default. |
| NFR-PRV-003 | Any future session recording is a separately released feature with explicit policy, local disclosure/consent, encryption, retention, access control, and deletion behavior. |
| NFR-PRV-004 | Every persisted field has classification, purpose, lawful/contractual basis review, retention period, regional processing rule, and deletion/export behavior. |
| NFR-PRV-005 | Tenant deletion, user deletion, device revocation, data export, and retention expiry are testable workflows with auditable completion evidence. |
| NFR-PRV-006 | Diagnostic bundles apply allowlisted collection and redaction, require user/admin intent, and never include session content or reusable credentials. |

## 5. Maintainability and supply chain

| ID | Requirement |
|---|---|
| NFR-MNT-001 | Public HTTP, peer, IPC, native ABI, configuration, event, and update interfaces are versioned and compatibility-tested. |
| NFR-MNT-002 | Native C++ components expose a stable C ABI so managed code does not depend directly on unstable C++ layouts or exceptions. |
| NFR-MNT-003 | Every production module has an owner, tests, telemetry, failure-mode documentation, dependency inventory, and operational runbook linkage. |
| NFR-MNT-004 | A clean protected worker restores from lock files, builds reproducibly, and emits dependency inventory, provenance, and an SBOM. |
| NFR-MNT-005 | Architecture-significant decisions and deviations are recorded in ADRs; undocumented boundary changes fail review. |
| NFR-MNT-006 | Database changes use forward-only versioned migrations with compatibility windows, rollback/roll-forward procedures, and production rehearsal for destructive changes. |

## 6. Accessibility and localization

| ID | Requirement |
|---|---|
| NFR-ACC-001 | Consent, session control, emergency disconnect, file decisions, and security settings are fully operable by keyboard. |
| NFR-ACC-002 | Operator and host UI expose accessible names, roles, states, focus order, live announcements, and high-contrast behavior for supported assistive technologies. |
| NFR-ACC-003 | Korean and English resources are present from the first beta, with no user-visible security text hard-coded in implementation code. |
| NFR-ACC-004 | Security-relevant state is never communicated by color, animation, or iconography alone. |
| NFR-ACC-005 | At supported Windows text scaling and DPI settings, critical consent/disconnect controls remain visible, readable, and operable without clipping. |

## 7. Cost and capacity control

| ID | Requirement |
|---|---|
| NFR-CST-001 | A secure direct peer route is preferred when reachable; relay fallback does not weaken authorization or encryption. |
| NFR-CST-002 | TURN usage is metered by tenant, region, route, transport, session, and byte direction with bounded-cardinality identifiers. |
| NFR-CST-003 | Rate limits, quotas, credential TTLs, allocation limits, and abuse controls prevent unbounded relay and control-plane consumption. |
| NFR-CST-004 | Video adaptation reduces idle and low-motion traffic without violating the latency and text-readability targets. |
| NFR-CST-005 | Server-side storage defaults to metadata only; any new content-bearing storage requires an approved architecture, privacy, security, and cost review. |
| NFR-CST-006 | Capacity models are validated by load tests before each production tier increase, and alert thresholds are derived from measured saturation behavior. |
