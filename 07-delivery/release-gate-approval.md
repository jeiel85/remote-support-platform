# Attended GA — Multi-Owner Release Gate Approval Record

Maps every item in `06-quality/release-gates.md` "Common gates" and
"Attended GA" to evidence, per Goal 12 acceptance criteria. `06-quality/
release-gates.md` requires the target train's gates plus `ALL_RELEASES`
gates; no security or consent invariant may be waived by product management
alone.

## Common gates

| Gate | Evidence | Status |
|---|---|---|
| Contracts validate; schema/API/Proto/SQL/ABI compatibility | `build.ps1 ValidateDesign` (47/54 OpenAPI ops, 31/18 Protobuf, 12 scopes, 13 states synchronized); `RemoteSupport.ContractTests`, `RemoteSupport.ArchitectureTests` | Pass |
| Clean protected-worker build from lock files, SBOM/provenance/signed artifacts | `build.ps1 Package` + `tools/supply-chain/create_sbom.py`; `--locked-mode` restore | Pass (signing performed in the protected-job environment per `07-delivery/evidence/goal-11.md`) |
| Threat model, abuse review, privacy/data inventory, dependency/license review, codec/patent review, crypto/export review, incident runbooks current | `05-security/threat-model.md`, `05-security/abuse-prevention.md`, `00-product/commercialization-and-compliance.md`, `10-references/dependency-and-license-policy.md`, codec/patent legal gate in same file, `08-operations/runbooks/*` | Pass — documents current; codec/patent and crypto/export legal gate itself needs the signed counsel record (see Open items) |
| External penetration testing, no unresolved critical/high | `06-quality/penetration-test-scope-and-closure.md` | **Open** — scope/process ready, external engagement pending |
| Accessibility/localization, Windows matrix, performance/SLO, backup/restore, rollback evidence | `06-quality/goal-09-accessibility-localization-report.md`, `06-quality/compatibility-matrix.md`, `06-quality/supported-capability-matrix.md`, `06-quality/performance-and-slo.md`, `07-delivery/evidence/goal-11.md` restore/TURN drills | Pass for implementation/local evidence; physical Windows lab and provider-backed DR drill remain per `08-operations/failure-drill-plan.md` |
| Update root/manifest/artifact signatures, expiry, rollback tests | `UpdateMetadataVerifierTests`, `SecureUpdateCoordinatorTests` | Pass |

## Attended GA gate

| Requirement | Evidence | Status |
|---|---|---|
| Goals 01–12 complete | This record plus `07-delivery/evidence/goal-01.md`…`goal-12.md` | Pass once this file's evidence is complete |
| Signed consent, peer proof, TURN authorization, signed transport binding cannot be bypassed | `AT-FR-CON-*`, `AT-SEC-TRN-001-*` native tests, Goal 07 evidence | Pass |
| Managed Host and unattended code absent or release-disabled and excluded from installers | Goal 13/14 are separate release trains not yet built at this Goal 12 checkpoint; `eng/package-attended.ps1` packages only Portable Agent/Operator Console | Pass |
| Abuse response and security contact are operational | `08-operations/runbooks/abuse-response.md` | Pass for the technical containment process; published security-contact address is an Open item below |

## Open items blocking unconditional GA (tracked, not waived)

1. External penetration test dated closure — `06-quality/penetration-test-scope-and-closure.md`.
2. AV/EDR vendor verdicts — `06-quality/av-edr-false-positive-process.md` tracking table.
3. Incident-response and update-key-compromise tabletop scheduling record — `08-operations/tabletop-exercises.md`.
4. Staged beta cohort evidence from a deployed Release Candidate — `07-delivery/beta-program.md`.
5. Privacy notice/terms/DPA counsel review and publication — `00-product/privacy-notice.md`, `terms-of-service.md`, `data-processing-agreement.md`.
6. Physical Windows/EDR compatibility lab pass — `06-quality/compatibility-matrix.md`.
7. Provider-backed PostgreSQL PITR and coturn replacement drill — `08-operations/failure-drill-plan.md`.

Each open item has an owner-assignable row above rather than a hidden manual
step. None may be silently marked complete; this file must be updated with a
dated entry and approver name in the sign-off table below when closed.

## Sign-off (required before GA; leave blank until performed)

| Owner role | Name | Date | Gate scope confirmed |
|---|---|---|---|
| Engineering release owner | | | Common gates, Attended GA gate |
| Security lead | | | Penetration test, threat model, key-compromise tabletop |
| Privacy/legal | | | Privacy notice, terms, DPA, codec/patent gate |
| Operations lead | | | Runbooks, DR drill, alerting |

GA ships only when every row above has a name and date, or the corresponding
open item has an explicit non-critical risk acceptance recorded per
`06-quality/release-gates.md`'s P1/P2 exception process — security/consent
invariants excluded from that exception path.
