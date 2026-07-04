# Goal 12 evidence

Date: 2026-07-04. Local validation environment: Windows x64, .NET SDK
10.0.301, ASP.NET Core 10.0.9, pinned Python environment, no external
network dependencies exercised.

## Implemented deliverables

- a gap analysis confirming all 99 P0/P1/P2 requirement rows in
  `07-delivery/traceability/requirements-traceability.csv` for release trains
  `ATTENDED_GA`/`ALL_RELEASES` are already implemented under Goals 01–11 with
  no dependency on the not-yet-built Goal 13/14 trains;
- an independent-penetration-test scope and closure-tracking record
  (`06-quality/penetration-test-scope-and-closure.md`);
- an AV/EDR false-positive submission process and tracking table
  (`06-quality/av-edr-false-positive-process.md`);
- incident-response and update-key-compromise tabletop exercise scripts with
  concrete injects, pass criteria and a scheduling record
  (`08-operations/tabletop-exercises.md`);
- a staged-beta program charter, promotion thresholds and a deterministic
  fixture-cohort rehearsal of the metrics/cost-model arithmetic
  (`07-delivery/beta-program.md`, `tools/operations/analyze_beta_cohort.py`);
- a consolidated supported capability matrix with explicit UAC/secure-desktop
  and Managed Host/unattended limitations
  (`06-quality/supported-capability-matrix.md`);
- draft privacy notice, terms of service and data-processing agreement
  clearly marked pending counsel review
  (`00-product/privacy-notice.md`, `terms-of-service.md`,
  `data-processing-agreement.md`);
- an abuse-response operational runbook
  (`08-operations/runbooks/abuse-response.md`);
- a multi-owner release-gate approval record mapping every
  `06-quality/release-gates.md` common/Attended-GA gate to its evidence, an
  explicit open-items list, and a blank sign-off table
  (`07-delivery/release-gate-approval.md`).

## Automated evidence

`tools/operations/verify_goal12.py` confirms every evidence document above
exists, that the release-gate-approval record references each one, that no
P0 `ATTENDED_GA`/`ALL_RELEASES` requirement's `goal_refs` point at Goal
13/14, and runs the beta-cohort metrics rehearsal.

```powershell
./build.ps1 -Target ValidateDesign
./build.ps1 -Target Test -Configuration Release
./build.ps1 -Target IntegrationTest -Configuration Release
python tools/operations/verify_goal12.py .
python tools/operations/analyze_beta_cohort.py .
```

`build.ps1 IntegrationTest`/`CI` now also runs `verify_goal12.py` alongside
the existing Goal 07/11 verification scripts.

## Production boundary

This evidence closes the gates that are producible from repository content:
requirement/goal-reference consistency, process design, and deterministic
metric-formula rehearsal. It explicitly does not claim: a completed external
penetration test, a dated AV/EDR vendor verdict, a scheduled/executed
tabletop with real incident staff, real staged-beta cohort data from a
deployed Release Candidate, or counsel-signed privacy/terms/DPA documents.
`07-delivery/release-gate-approval.md` lists these as open items with named
owners rather than treating them as complete, and its sign-off table must
carry a dated name before Attended GA ships. Goal 12 as implemented here
satisfies "every checkbox in release-gates.md has evidence or an approved
non-critical exception" by providing evidence or an explicit open-item
record for each one; it does not itself constitute the human/external
approvals that remain outstanding.
