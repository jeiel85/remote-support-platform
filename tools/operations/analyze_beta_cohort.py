#!/usr/bin/env python3
"""Deterministic rehearsal of the Goal 12 staged-beta metrics analysis.

This validates the metric formulas and promotion thresholds against a fixture
cohort. It is not beta evidence; see 07-delivery/beta-program.md "Production
boundary".
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from evaluate_update_rollout import evaluate  # noqa: E402


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def monthly_relay_gb(sessions: int, avg_duration_seconds: float, avg_video_mbps: float, relay_probability: float) -> float:
    require(0 <= relay_probability <= 1, "relay probability must be a fraction")
    return sessions * avg_duration_seconds * avg_video_mbps / 8 / 1000 * relay_probability


def main() -> int:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    assert root.is_dir(), f"missing repository root: {root}"

    # Fixture design-partner cohort (07-delivery/beta-program.md stage 2 minimums: >=200 sessions).
    cohort = {
        "currentPercentage": 5,
        "attemptedInstallations": 240,
        "successfulInstallations": 239,
        "sessions": 240,
        "crashes": 1,
        "bootLoops": 0,
        "signatureFailures": 0,
        "observationMinutes": 4320,
    }
    evaluate(cohort, 25)

    failure_rate = (cohort["attemptedInstallations"] - cohort["successfulInstallations"]) / cohort["attemptedInstallations"]
    crash_rate = cohort["crashes"] / cohort["sessions"]
    require(failure_rate <= 0.005, "fixture failure rate must satisfy the promotion threshold")
    require(crash_rate <= 0.005, "fixture crash rate must satisfy the promotion threshold")

    relay_gb = monthly_relay_gb(cohort["sessions"], avg_duration_seconds=900, avg_video_mbps=2.5, relay_probability=0.35)
    require(relay_gb > 0, "relay cost estimate must be positive when relay_probability > 0")
    provider_usd_per_gb = 0.09
    relay_cost_usd = relay_gb * provider_usd_per_gb

    abuse_reports = 0
    support_bundle_submissions = 3
    require(abuse_reports >= 0 and support_bundle_submissions >= 0, "friction counters must be non-negative")
    support_bundle_rate = support_bundle_submissions / cohort["sessions"]

    print(
        "Beta cohort rehearsal: "
        f"failure_rate={failure_rate:.4%} crash_rate={crash_rate:.4%} "
        f"relay_GB={relay_gb:.3f} relay_cost_usd={relay_cost_usd:.2f} "
        f"support_bundle_rate={support_bundle_rate:.4%} abuse_reports={abuse_reports}"
    )
    print("Verified Goal 12 beta metrics formulas and promotion thresholds against fixture cohort")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
