# Threat Model

Method: STRIDE-inspired analysis with abuse-case emphasis.

## 1. Assets

- remote desktop confidentiality and integrity;
- operator and host identity;
- device private keys;
- session authorization and scopes;
- code-signing/update keys;
- tenant membership and policies;
- audit evidence;
- TURN capacity and service availability.

## 2. Trust boundaries

- endpoint process boundary;
- service ↔ agent IPC;
- endpoint ↔ control plane;
- peer ↔ peer transport;
- control plane ↔ TURN credential system;
- production ↔ build/signing;
- operator support staff ↔ customer tenant.

## 3. Major threats and controls

| Threat | Attack | Controls |
|---|---|---|
| Code guessing | attacker brute-forces support codes | high entropy + short expiry, rate limits, code not sole secret, generic errors |
| Fake operator | attacker requests session under misleading identity | authenticated operator, tenant display, signed identity, host consent |
| Signaling MITM | compromised signaling rewrites SDP, ICE or peer details | TLS plus mandatory reciprocal signed binding of both observed DTLS fingerprints, peer authorization context, epoch and scopes; optional out-of-band SAS for high-assurance policy |
| Support-code guessing or database disclosure | attacker enumerates short codes or steals lookup rows | 50-bit CSPRNG Crockford codes, versioned HMAC-SHA-256 lookup, account/tenant/edge/prefix/global rate limits, generic failures, short expiry; no raw code persistence |
| Consent or peer-proof replay | captured approval/bootstrap/challenge is reused | signed domain-separated canonical payloads, keyed nonce hashes, single-use state, state-version/epoch binding, short bootstrap and peer-token lifetimes |
| Test authentication enabled in production | deployment selects test environment or in-memory store | test OIDC handler and non-production adapters are Debug-only; Release startup requires OIDC, 256-bit keys and PostgreSQL |
| TURN abuse | stolen/static credentials create relay allocations | short-lived session credentials, quotas, no anonymous access |
| Service IPC abuse | local process sends privileged commands | pipe ACL, mutual challenge, command allowlist, capability token |
| Update compromise | attacker publishes malicious binary | code signing, threshold metadata, hashes, anti-rollback, staged rollout |
| Tenant breakout | API query omits tenant filter | explicit tenant context, repository rules, DB constraints/RLS, isolation tests |
| Clipboard leak | background sync copies secrets | opt-in scopes, direction controls, size limits, no logs |
| File malware | operator sends malicious executable | explicit acceptance, type policy, MOTW/AV handling, no auto-open |
| Hidden persistence | portable agent installs background service | separate packages, portable invariant tests, no persistence code |
| Input stuck/abuse | dropped key-up or continued control after revoke | sequence tracking, release-all, host enforcement, immediate revoke |
| Native memory bug | malformed packet corrupts media/parser | dependency patching, fuzzing, sanitizers, process isolation option |
| Audit tampering | privileged actor edits events | append-only API, hash chaining, restricted DB role, external export |
| DDoS | allocation/signaling floods | edge rate limits, quotas, TURN hardening, autoscale/runbooks |
| Credential phishing misuse | scammer convinces user to grant access | clear identity and warnings, abuse reporting, reputation/risk checks |

## 4. Abuse cases

### Social-engineering scam

Controls:

- display verified organization name and operator identity;
- show prominent warning not to share financial credentials or one-time passwords;
- configurable blocking for high-risk consumer scenarios;
- rapid abuse report from host UI;
- tenant verification and risk scoring for commercial operators;
- suspend and preserve audit metadata under policy.

### Stalkerware conversion

Controls:

- no hidden indicators;
- unattended access opt-in and discoverable in installed apps/settings;
- persistent local notification and revocation;
- no silent portable persistence;
- periodic access summaries for managed endpoints where appropriate.

### Data exfiltration

Controls:

- granular scopes;
- file and clipboard policy;
- session timeout;
- optional DLP hook;
- audit metadata;
- operator role/device restrictions.

## 5. Security test cases

- replay expired consent and peer tokens;
- substitute SDP fingerprint/peer key and verify content channels remain gated until reciprocal transport binding succeeds;
- bypass tenant filter with valid ID from another tenant;
- send privileged IPC from unprivileged local user;
- downgrade update metadata/version;
- transfer `..\\`, alternate data stream and reserved filenames;
- malformed Protobuf lengths and file chunk offsets;
- revoke operator/device during active session;
- simulate signaling and TURN compromise independently.
