# Goal 09 evidence

Date: 2026-07-03. Local validation environment: Windows x64, .NET SDK
10.0.301, self-contained win-x64 packages, unsigned development channel.

Implemented deliverables:

- attended-only WPF Agent and Operator Console with Korean/English resources,
  explicit consent/scopes, persistent disclosure, immediate disconnect and an
  Agent emergency hotkey;
- system-browser OIDC authorization-code/PKCE for the Operator Console and
  ephemeral P-256 peer identity for each run;
- full control-plane, signaling, ICE/DTLS binding, screen/input and peer-data
  product wiring;
- host-only monotonic scope revocation over the reserved native control channel,
  local input release, affected-operation cancellation and peer UI reconciliation;
- portable Agent ZIP and payload-bearing per-user Operator Setup with manifest
  hashes, staging, atomic swap, repair, downgrade rejection, rollback recovery
  and clean uninstall; no service, driver or scheduled task is installed;
- strict threshold-Ed25519 update metadata/root rotation and
  product/channel/architecture/expiry/sequence/hash/rollout checks;
- allowlisted support bundles and crash-recovery markers without session content;
- deterministic package manifests, SBOM/provenance hooks, architecture checks,
  Authenticode signing hook and smoke-testable executables.

Validated locally:

```powershell
./build.ps1 -Target Test -Configuration Release
./build.ps1 -Target IntegrationTest -Configuration Release
./build.ps1 -Target Package -Configuration Release
./eng/test-attended-package.ps1 -Architecture x64
python tools/packaging/verify_goal09.py artifacts/packages/attended --allow-unsigned-development
```

The isolated package test installs under a temporary test root, launches both
packaged products in smoke mode, rejects a simulated downgrade, repairs the
installation, uninstalls it and verifies removal. Native integration establishes
two real local WebRTC peers, validates reciprocal signed DTLS binding, exchanges
product data and advances a host permission revision without closing unrelated
channels.

## Promotion boundary

This workstation proves a runnable **x64 unsigned development build**, not a
signed beta release. Signed-beta/GA promotion remains blocked until a protected
release worker supplies the organization Authenticode certificate, builds and
tests the arm64 native runtime, exercises install/update/rollback on the supported
Windows matrix, and records SmartScreen, Smart App Control, Defender, partner
EDR, screen-reader and keyboard-only evidence. The packaging target fails closed
instead of relabeling x64 binaries or silently emitting unsigned production
artifacts.
