#!/usr/bin/env python3
from __future__ import annotations

import csv
import re
import subprocess
import sys
from pathlib import Path


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def main() -> int:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()

    required_docs = [
        "08-operations/runbooks/abuse-response.md",
        "06-quality/av-edr-false-positive-process.md",
        "06-quality/supported-capability-matrix.md",
        "06-quality/penetration-test-scope-and-closure.md",
        "07-delivery/beta-program.md",
        "08-operations/tabletop-exercises.md",
        "00-product/privacy-notice.md",
        "00-product/terms-of-service.md",
        "00-product/data-processing-agreement.md",
        "07-delivery/release-gate-approval.md",
    ]
    for rel in required_docs:
        require((root / rel).is_file(), f"Goal 12 evidence document is missing: {rel}")

    with (root / "07-delivery/acceptance-test-cases.csv").open(encoding="utf-8") as handle:
        rows = list(csv.DictReader(handle))
    require(len(rows) > 0, "acceptance test catalog is empty")

    gated_trains = {"ATTENDED_GA", "ALL_RELEASES"}
    p0_rows = [row for row in rows if row["priority"] == "P0" and row["release_train"] in gated_trains]
    require(len(p0_rows) > 0, "expected P0 rows for ATTENDED_GA/ALL_RELEASES")

    with (root / "07-delivery/traceability/requirements-traceability.csv").open(encoding="utf-8") as handle:
        traceability = {row["id"]: row for row in csv.DictReader(handle)}
    p0_ids = {row["requirement_id"] for row in p0_rows}
    later_train_ids: list[tuple[str, list[str]]] = []
    for req_id in sorted(p0_ids):
        require(req_id in traceability, f"P0 requirement missing from traceability: {req_id}")
        goal_refs = traceability[req_id]["goal_refs"]
        goal_numbers = {int(match) for match in re.findall(r"goal-(\d+)-", goal_refs)}
        later = sorted(n for n in goal_numbers if n >= 13)
        if later:
            later_train_ids.append((req_id, [f"goal-{n:02d}" for n in later]))
    require(not later_train_ids,
            f"ATTENDED_GA/ALL_RELEASES P0 requirement depends on a later release-train goal: {later_train_ids}")

    approval = (root / "07-delivery/release-gate-approval.md").read_text(encoding="utf-8")
    for rel in required_docs:
        if rel == "07-delivery/release-gate-approval.md":
            continue
        require(rel in approval or Path(rel).name in approval,
                f"release-gate-approval.md does not reference {rel}")

    result = subprocess.run(
        [sys.executable, str(root / "tools/operations/analyze_beta_cohort.py"), str(root)],
        capture_output=True, text=True,
    )
    require(result.returncode == 0, f"beta cohort rehearsal failed: {result.stderr}")

    print(f"Verified Goal 12 release-gate evidence set, {len(p0_rows)} gated P0 acceptance IDs covered, and beta metrics rehearsal")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
