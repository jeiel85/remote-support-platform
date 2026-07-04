# Goal 11 evidence

Date: 2026-07-04. Local validation environment: Windows x64, .NET SDK
10.0.301, ASP.NET Core 10.0.9, pinned Python environment and deterministic
in-memory/test publication adapters.

## Implemented deliverables

- threshold-Ed25519 root/manifest signing and combination tool plus a clean-worker
  release verifier; signing keys enter only through the protected-job environment;
- sequential root publication and product/channel/architecture-bound manifest
  publication with immutable ETags and no-store responses;
- Windows updater verification of signed metadata, exact size/SHA-256 and the
  Authenticode trust chain/publisher before activation;
- an atomic `stage -> health -> commit/rollback` Operator Setup transaction,
  startup recovery and separate highest-seen security floor so a failed canary
  cannot make an older manifest eligible;
- internal/canary/5%/25%/100% rollout metadata and a promotion gate that blocks
  skipped stages, insufficient observation/cohort size, update success below
  99.5%, crash-free sessions below 99.5%, boot loops or any signature failure;
- OpenTelemetry-compatible client/server activities and meters, structured
  allowlisted telemetry, authenticated bounded-cardinality Prometheus export,
  collector privacy deletion, production dashboard and linked alert rules;
- preview-bound, explicitly approved support bundles containing only three
  allowlisted JSON files and never uploading automatically;
- update, database restore, TURN replacement, signing compromise and support
  collection procedures plus automated local control drills.

## Automated evidence

`UpdateMetadataVerifierTests` and `SecureUpdateCoordinatorTests` map to
AT-NFR-SEC-009 and AT-NFR-REL-007. They cover signer/verifier compatibility,
product/channel/architecture/sequence binding, tamper rejection, publisher/hash
checks, failed-health rollback, retained anti-rollback floor and idempotent
interrupted-update recovery.

`UpdateAndObservabilityApiTests` maps to AT-NFR-SEC-009 and AT-NFR-REL-001. It
checks sequential root/manifest publication, current-version 304 behavior,
ETags/no-store/correlation, protected metric scraping, route-template labels and
absence of product/tenant identity in metrics. Existing privacy tests plus the
Goal 11 drill map to AT-NFR-SEC-005; support generation rejects missing approval.

The 2026-07-04 deterministic drill replayed 500 WAL-like records over a 4,500
record base snapshot, verified all 5,000 hash-chained records with zero fixture
record loss, and measured 40.594 ms local restore time. The two-node TURN control
drill removed `turn-a` from issuance while `turn-b` remained, measured 0.010 ms
control selection/replacement time, and confirmed the checked-in 600-session
target retains 25% quota and 40% relay-port headroom. Audit-backlog, update
signature and TURN-allocation failure fixtures all fired; four injected privacy
canaries had zero telemetry leakage. The committed capture is
`07-delivery/evidence/goal-11-drill.json`; each run also writes fresh ignored
output to `artifacts/evidence/goal-11-drill.json`.

```powershell
./build.ps1 -Target ValidateDesign
./build.ps1 -Target Test -Configuration Release
./build.ps1 -Target IntegrationTest -Configuration Release
python tools/operations/verify_goal11.py .
python tools/operations/run_goal11_drills.py .
```

## Production boundary

This evidence completes the repository implementation and deterministic Goal 11
control drills; it does not claim production-provider RPO/RTO or relay capacity.
Goal 12 release approval still requires isolated PostgreSQL PITR with named
backup/WAL evidence, exact-image dual-node coturn replacement/load results,
protected Authenticode/update-role signing, alert delivery through the real
pager, supported Windows/EDR validation and independent security review. The
pipeline fails closed when those signed artifacts or promotion records are
absent rather than treating local development evidence as commercial GA proof.
