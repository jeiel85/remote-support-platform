#!/usr/bin/env python3
from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path

PATTERNS = {
    "private key": re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
    "AWS access key": re.compile(rb"\bAKIA[0-9A-Z]{16}\b"),
    "GitHub token": re.compile(rb"\bgh[pousr]_[A-Za-z0-9]{30,}\b"),
    "Slack token": re.compile(rb"\bxox[baprs]-[A-Za-z0-9-]{20,}\b"),
}


def main() -> int:
    root = Path(sys.argv[1]).resolve()
    listed = subprocess.run(
        ["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"],
        cwd=root,
        check=True,
        capture_output=True,
    ).stdout.split(b"\0")
    findings: list[str] = []
    for raw in listed:
        if not raw:
            continue
        relative = raw.decode("utf-8")
        path = root / relative
        if not path.is_file() or path.stat().st_size > 10 * 1024 * 1024:
            continue
        payload = path.read_bytes()
        for name, pattern in PATTERNS.items():
            if pattern.search(payload):
                findings.append(f"{relative}: {name}")
    if findings:
        raise AssertionError("Potential secrets found:\n" + "\n".join(findings))
    print(f"Secret scan passed for {len(listed) - 1} repository files")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

