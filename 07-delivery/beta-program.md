# Staged Beta Program

Goal 12 requires performing a staged beta and analyzing real failure/cost
metrics before GA. This charter defines the program and the metrics analysis
method; the cohort data itself must come from a deployed Release Candidate; a
local repository run cannot produce real customer telemetry.

## Stages

1. **Internal** — company devices only, `internal` update channel
   (`src/client/managed/RemoteSupport.Updater`), minimum 5 business days.
2. **Design partners** — 3–10 opted-in tenants under signed beta terms,
   `canary` channel, minimum 10 business days or 200 sessions, whichever is
   longer.
3. **Staged public** — 5% then 25% cohort on `stable` channel using the
   promotion gate already implemented in `SecureUpdateCoordinator`
   (`AT-NFR-SEC-009`, `AT-NFR-REL-007`), each stage held for the observation
   window in `evaluate_update_rollout.py` before advancing.

Advancing a stage requires: update success ≥ 99.5%, crash-free sessions ≥
99.5%, zero signature failures, zero boot loops, and no unresolved SEV-1/SEV-2
from that stage — the same thresholds the updater promotion gate enforces
mechanically, applied here as the human go/no-go criteria for the *program*
stage rather than the artifact rollout stage.

## Metrics collected per stage

- Attempted vs. completed session rate, failure reason distribution
  (from `support_sessions.end_reason` and `SigningAnomaly` /
  `SessionAuthorizationFailure` alert firing counts).
- Crash-free session rate (`RemoteSupportTelemetry` client crash events).
- TURN relay cost per session-minute (bytes relayed × current provider rate)
  against the model in `04-backend/capacity-and-cost.md`.
- Support-bundle submission rate as a proxy for unresolved-friction incidents.
- Abuse-report rate (`08-operations/runbooks/abuse-response.md` intake).

## Deterministic local rehearsal

`tools/operations/analyze_beta_cohort.py` computes the same metric formulas
against a fixture cohort dataset and asserts the promotion thresholds and
cost-model arithmetic are correct before any real cohort data exists. It is a
rehearsal of the analysis method, not beta evidence.

```powershell
python tools/operations/analyze_beta_cohort.py .
```

## Production boundary

The program design, promotion thresholds and metrics formulas are defined and
their arithmetic is verified against a fixture cohort. Real internal/design-
partner/staged-public cohorts, their actual failure and cost data, and the
resulting go/no-go record are produced only by running this program against a
deployed Release Candidate with real tenants; that record — not this charter
— is the artifact `release-gates.md` requires before GA.
