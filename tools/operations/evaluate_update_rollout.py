#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
from pathlib import Path


def evaluate(evidence: dict[str, object], requested: int, halt: bool = False) -> None:
    allowed = [0, 5, 25, 100]
    current = int(evidence["currentPercentage"])
    if halt:
        if requested != 0 or current <= 0:
            raise AssertionError("emergency halt must reduce an active rollout to zero")
        return
    if requested not in allowed or current not in allowed or allowed.index(requested) != allowed.index(current) + 1:
        raise AssertionError("rollout promotion must advance exactly one approved stage")
    attempted = int(evidence["attemptedInstallations"])
    succeeded = int(evidence["successfulInstallations"])
    sessions = int(evidence["sessions"])
    crashes = int(evidence["crashes"])
    failures = attempted - succeeded
    if attempted < 0 or succeeded < 0 or succeeded > attempted or sessions < 0 or crashes < 0 or crashes > sessions:
        raise AssertionError("rollout counters are internally inconsistent")
    if int(evidence["observationMinutes"]) < (60 if requested <= 25 else 240):
        raise AssertionError("rollout observation window is incomplete")
    if attempted < 100 or sessions < 100:
        raise AssertionError("rollout cohort is below the absolute-count guard")
    if failures / attempted > 0.005:
        raise AssertionError("eligible installation success is below 99.5%")
    if crashes / sessions > 0.005:
        raise AssertionError("crash-free session rate is below 99.5%")
    if int(evidence["bootLoops"]) != 0 or int(evidence["signatureFailures"]) != 0:
        raise AssertionError("boot-loop or signature failure blocks promotion")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("evidence")
    parser.add_argument("--requested", type=int, required=True)
    parser.add_argument("--halt", action="store_true")
    args = parser.parse_args()
    evidence = json.loads(Path(args.evidence).read_text(encoding="utf-8"))
    evaluate(evidence, args.requested, args.halt)
    print(f"Update rollout gate passed for {args.requested}% promotion")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
