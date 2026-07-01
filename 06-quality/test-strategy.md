# Test Strategy

## 1. Test pyramid

### Unit

- coordinate transforms;
- state machines;
- policy evaluation;
- token and expiry rules;
- file name/path validation;
- bitrate adaptation functions;
- audit canonicalization.

### Component

- capture sources with synthetic displays;
- encoder selection/fallback;
- input state tracker;
- IPC ACL/authentication;
- update verifier;
- TURN credential generator.

### Integration

- Agent ↔ control plane ↔ Console;
- WebSocket reconnect;
- direct and relayed WebRTC;
- service ↔ user-session agent;
- database/outbox;
- OIDC and policy;
- file transfer resume.

### End-to-end

- supported Windows builds and hardware classes;
- consumer NAT and enterprise firewall profiles;
- reboot and update scenarios;
- account/device revocation during sessions;
- accessibility and localization.

## 2. Deterministic lab

Build a test controller that can:

- start Agent/Console VMs;
- configure monitors, DPI and rotation;
- inject synthetic desktop patterns;
- apply latency, loss, jitter, bandwidth and reordering;
- force direct or TURN route;
- capture synchronized telemetry;
- compare remote rendered output and input coordinates.

## 3. Security tests

- IDOR and tenant isolation;
- token replay and scope escalation;
- rate-limit bypass;
- malicious SDP/ICE/file metadata;
- named-pipe impersonation;
- updater rollback/signature failure;
- local tampering with installed binaries/config;
- abuse workflow.

## 4. Native quality

- sanitizers in test builds;
- static analysis;
- fuzzing corpus retained and expanded;
- GPU resource leak soak;
- crash dump symbolization in restricted environment;
- dependency ABI compatibility tests.

## 5. Exit rule

A test failure in security, update integrity, tenant isolation, consent, scope enforcement or audit integrity blocks release regardless of aggregate pass percentage.
