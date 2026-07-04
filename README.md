# Remote Support Platform — Production Design Bundle

**Revision:** 1.3 final re-audited  
**Target:** Windows-first commercial remote-support platform  
**Status:** Implementation-ready contracts; production release remains conditional on goal evidence and release gates.

**Project site:** https://jeiel85.github.io/remote-support-platform/
**Current build:** `v0.9.0-beta.1` unsigned Windows x64 attended beta

## What this bundle fixes

The final audit separates attended GA, Managed Host, and unattended release trains; defines the missing managed-device command and credential lifecycle; makes consent/device decisions cryptographically sender-constrained; binds WebRTC DTLS fingerprints to peer authorization; defines update/audit canonicalization; adds Admin Portal and tenant-governance APIs; strengthens tenant-bound database relationships; and adds explicit codec/legal gates.

## Start here

1. `FINAL_AUDIT_REPORT.md`
2. `IMPLEMENTATION_READINESS.md`
3. `START_HERE.md`
4. `07-delivery/implementation-order.md`
5. `07-delivery/goals/goal-01-foundation.md`

## Release sequence

- Goals 01–12: attended GA.
- Goal 13: separately released Managed Host foundation.
- Goal 14: separately approved unattended access.

Goals 01–11 now have runnable implementation evidence. Goal 11 adds the
signature/hash/Authenticode-bound updater, anti-rollback state, transactional
health rollback, release verifier, bounded observability, dashboards/alerts,
support-bundle approval and deterministic failure drills. External penetration,
provider-backed recovery/capacity evidence and commercial approval remain Goal 12.

The canonical machine-readable contracts are OpenAPI, Protobuf, JSON Schemas, native C ABI header, PostgreSQL schema, requirements traceability CSV and acceptance-test CSV. Markdown explains intent but does not override those contracts.

## Implementation quick start

The design bundle and implementation now coexist in this repository. On Windows PowerShell 7, the canonical local flow is:

```powershell
./build.ps1 -Target Bootstrap
./build.ps1 -Target ValidateDesign
./build.ps1 -Target Build
./build.ps1 -Target Test
./build.ps1 -Target IntegrationTest
./build.ps1 -Target Package -Configuration Release
```

The bootstrap downloads the pinned .NET SDK and hash-locked Python/CMake tools into ignored `.tools/` storage. Generated contracts are committed; CI regenerates and rejects drift. See `docs/implementation/module-ownership.md` and the goal evidence under `docs/implementation/goals/`.

## Runnable attended artifacts

`./build.ps1 -Target Package -Configuration Release` emits the portable Agent
ZIP, per-user Operator Setup executable, manifests and provenance under
`artifacts/packages/attended`. Run `./eng/test-attended-package.ps1 -Architecture
x64` to exercise both packaged applications plus install/repair/downgrade/
uninstall plus transactional update rollback behavior in an isolated temporary root. Configure real endpoints as
described in `deploy/client/README.md`.

Local packages are unsigned development artifacts unless release signing is
explicitly required and `RS_SIGN_CERT_THUMBPRINT` is available. Arm64 packaging
also requires a matching arm64 native runtime; neither condition is silently
bypassed.

Protected release jobs generate staged update metadata with
`eng/publish-update.ps1`; clean workers verify it through
`RemoteSupport.ReleaseVerifier`. Local builds never receive production update
role keys or silently accept an unsigned update artifact.
