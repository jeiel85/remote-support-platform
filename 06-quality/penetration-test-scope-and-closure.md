# Independent Penetration Test — Scope and Closure Record

Goal 12 requires "complete independent penetration test and remediate
blockers" and `06-quality/release-gates.md` blocks GA on any unresolved
critical/high external finding. This document is the scope charter and the
closure tracking record; it is not itself the test, which must be performed
by a party independent of the implementation team.

## In-scope targets

1. Control-plane API (`02-protocol/openapi/openapi.yaml`): operator OIDC/PKCE
   flow, tenant isolation (`X-Tenant-Id` resolution), device enrollment and
   credential lifecycle, policy evaluation, signed update distribution.
2. Peer/session authorization: bootstrap credential issuance/consumption,
   DPoP sender-constraining, WebRTC DTLS transport binding
   (`02-protocol/canonicalization-and-signatures.md`).
3. Native attack surface: DataChannel/Protobuf/file-manifest/IPC parsers
   already covered by `tools/fuzz/RemoteSupport.ProtocolFuzz` — the external
   test should attempt exploitation paths beyond the fixed fuzz corpus.
4. Update chain: root/manifest signing, anti-rollback, artifact tampering
   (`src/client/managed/RemoteSupport.Security`).
5. Admin Portal BFF: CSRF/origin, session cookie handling, CSP
   (`PortalRouteTests` baseline).

## Out of scope

Denial-of-service load testing (covered separately by
`08-operations/failure-drill-plan.md` and capacity evidence), physical
security, and social engineering against staff (covered by the abuse/security
awareness program, not this technical test).

## Pre-test internal evidence (already produced, reduces expected finding volume but does not replace the external test)

- `dotnet list RemoteSupport.sln package --vulnerable --include-transitive`
  runs on every `build.ps1 CI` invocation.
- `tools/security/scan_secrets.py` runs on every CI invocation.
- Protocol fuzzing: 10,000 cases per `build.ps1 IntegrationTest` run, seeded
  corpus in `tools/fuzz/RemoteSupport.ProtocolFuzz`.
- Tenant-isolation and authorization negative tests:
  `GovernanceApiTests`, `AT-SEC-TRN-001-*` native tests.
- Native sanitizer/hardened-flag build per `05-security/secure-sdlc.md` §2.

## Closure tracking table

| Finding ID | Severity | Component | Status | Remediation | Verified by |
|---|---|---|---|---|---|
| _none yet — populate at test completion_ | | | | | |

A critical/high row blocks `06-quality/release-gates.md` "Attended GA" until
its Status column reads `Remediated` with a linked regression test, or
`Risk-accepted` with an owner, expiry and customer impact statement signed
per the P1/P2 exception process in that file's introduction. No security or
consent invariant may be waived this way.

## Production boundary

Scope, in-repo pre-test hardening evidence and the closure process are
authored and ready. The actual independent penetration test requires
engaging a third party with no implementation involvement against a deployed
Release Candidate; that engagement, its dated report and this table's
populated findings are an operational step outside what a repository commit
can produce, and `release-gates.md` fails closed — GA may not ship — until
that dated report exists and every critical/high row is closed.
