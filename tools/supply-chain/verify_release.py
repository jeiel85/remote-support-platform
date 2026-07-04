#!/usr/bin/env python3
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path


def main() -> int:
    artifacts = Path(sys.argv[1]).resolve()
    sbom = artifacts / "sbom/remote-support.spdx.json"
    tests = list((artifacts / "test-results").glob("*.trx"))
    if not sbom.exists():
        raise AssertionError("SPDX SBOM is missing")
    if json.loads(sbom.read_text(encoding="utf-8")).get("spdxVersion") != "SPDX-2.3":
        raise AssertionError("SPDX SBOM version is invalid")
    if not tests:
        raise AssertionError("Managed test evidence is missing")
    packages = artifacts / "packages/attended"
    subprocess.run([sys.executable, str(Path(__file__).parents[1] / "packaging/verify_goal09.py"), str(packages)], check=True)
    goal11 = artifacts / "evidence/goal-11-drill.json"
    if not goal11.exists():
        raise AssertionError("Goal 11 failure-drill evidence is missing")
    drill = json.loads(goal11.read_text(encoding="utf-8"))
    if any(drill[name]["result"] != "PASS" for name in ("turnReplacement", "alerts", "privacy")) or drill["restore"]["integrity"] != "PASS":
        raise AssertionError("Goal 11 failure-drill evidence did not pass")
    print(f"Verified release evidence: {sbom} and {len(tests)} test result file(s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
