# Goal 09 — Attended Product Packaging

## Objective

Package the portable attended Agent and installed Operator Console for signed beta distribution without introducing LocalSystem service or unattended capabilities.

## Deliverables

- Reproducible x64/arm64 builds and SBOM/provenance.
- Portable Agent package with clear remote-session disclosure and emergency disconnect.
- Per-user Operator Console installer, clean uninstall, repair, downgrade protection, crash recovery, and signed binaries.
- Channel-aware signed updater integration for Agent/Console using update root and manifest contracts.
- SmartScreen/AV/EDR compatibility test evidence and supportable diagnostic bundle.
- Korean/English resources and accessibility evidence for install/update/error flows.

## Exit criteria

The two attended products install/run/update/rollback on the supported Windows matrix, leave no privileged service, pass signature and supply-chain gates, and satisfy all mapped acceptance tests. Managed Host installer work belongs to Goal 13.
