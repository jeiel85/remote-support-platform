# AV/EDR False-Positive Submission Process

Addresses risk `R-001` in `00-product/risk-register.md`: AV/EDR products may
flag a remote-control binary heuristically. This is a process document; it
does not claim any vendor has cleared the product.

## Preventive controls (already implemented, verifiable in-repo)

- Every shipped binary is Authenticode-signed; the updater and Operator Setup
  reject unsigned or publisher-mismatched artifacts
  (`UpdateMetadataVerifierTests`, `SecureUpdateCoordinatorTests`).
- No packer/obfuscator/runtime self-modification is used; `build.ps1 Package`
  produces a reproducible SBOM (`tools/supply-chain/create_sbom.py`) so a
  vendor analyst can diff shipped binaries against source.
- The remote-input/capture surface has no generic command-execution primitive
  (`03-client/windows-service.md` non-responsibilities); this is stated in the
  vendor submission to reduce heuristic scoring as a RAT/backdoor.
- Visible UX: the Agent always shows an operator-identity/scope consent
  surface before control begins (`02-protocol/session-state-machine.md`,
  `AT-FR-CON-*`); submissions include this evidence because "silent remote
  control" is the primary heuristic trigger for this product category.

## Submission process

1. On every signed Release Candidate, the release owner submits binaries and
   the SBOM to the Microsoft Defender/Windows Defender Security Intelligence
   portal and to each vendor covered by the current supported security
   software list (`06-quality/supported-capability-matrix.md`).
2. Track each submission: vendor, binary hash, submission date, ticket/case
   ID, verdict, and re-submission date if a verdict lapses on the next engine
   update.
3. A confirmed false positive blocks GA promotion for that vendor's coverage
   row in the compatibility matrix until cleared or explicitly risk-accepted
   by the release owner with a customer-facing workaround (exclusion
   instructions) documented.
4. Re-submit on every major version bump and on any unexplained detection
   regression reported by a customer or telemetry (`UpdateFailureSpike`,
   `SigningAnomaly` alerts in `deploy/observability/prometheus-rules.yaml`).

## Tracking table (fill per release candidate)

| Vendor/engine | Binary hash | Submitted | Case ID | Verdict | Re-check date |
|---|---|---|---|---|---|
| Microsoft Defender | _pending RC build_ | _pending_ | _pending_ | _pending_ | _pending_ |
| Representative enterprise EDR (per partner lab program) | _pending RC build_ | _pending_ | _pending_ | _pending_ | _pending_ |

## Production boundary

The signed, unobfuscated, reproducible-SBOM build pipeline and consent UX are
implemented and tested. Actual vendor lab submissions require a signed
Release Candidate binary and active vendor/partner program enrollment, which
is an operational step performed once a GA candidate is cut; this document
does not fabricate a submitted verdict. `release-gates.md` blocks GA
promotion for a security-software row until its tracking-table entry shows a
non-pending verdict or an approved documented-limitation exception.
