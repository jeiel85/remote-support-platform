#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
from pathlib import Path

BUNDLE_DIRECTORIES = {f"{index:02d}-{name}" for index, name in enumerate([
    "product", "architecture", "protocol", "client", "backend", "security",
    "quality", "delivery", "operations", "templates", "references", "bootstrap",
])}
BUNDLE_ROOT_FILES = {
    "DOCUMENT_INDEX.md", "FINAL_AUDIT_REPORT.md", "IMPLEMENTATION_READINESS.md",
    "README.md", "START_HERE.md",
}


def main() -> int:
    root = Path(__file__).resolve().parent.parent
    paths = sorted(
        (
            path for path in root.rglob("*")
            if path.is_file()
            and path.name != "MANIFEST.json"
            and (
                path.relative_to(root).parts[0] in BUNDLE_DIRECTORIES
                or path.relative_to(root).as_posix() in BUNDLE_ROOT_FILES
            )
        ),
        key=lambda path: path.relative_to(root).as_posix(),
    )
    files = []
    for path in paths:
        payload = path.read_bytes()
        files.append({
            "path": path.relative_to(root).as_posix(),
            "bytes": len(payload),
            "sha256": hashlib.sha256(payload).hexdigest(),
        })
    manifest = {
        "schemaVersion": 1,
        "fileCount": len(files),
        "files": files,
    }
    (root / "MANIFEST.json").write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    print(f"Updated MANIFEST.json with {len(files)} design-bundle files")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
