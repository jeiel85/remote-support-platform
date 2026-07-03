#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import os
import subprocess
import zipfile
from datetime import datetime, timezone
from pathlib import Path


def digest(path: Path) -> str:
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def source_time(root: Path) -> tuple[int, int, int, int, int, int]:
    stamp = subprocess.run(
        ["git", "show", "-s", "--format=%ct", "HEAD"], cwd=root, check=True, capture_output=True, text=True
    ).stdout.strip()
    moment = datetime.fromtimestamp(int(stamp), timezone.utc)
    year = max(1980, min(2107, moment.year))
    return year, moment.month, moment.day, moment.hour, moment.minute, moment.second - moment.second % 2


def create_zip(root: Path, source: Path, output: Path) -> list[dict[str, object]]:
    files = sorted((path for path in source.rglob("*") if path.is_file()), key=lambda path: path.relative_to(source).as_posix())
    timestamp = source_time(root)
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in files:
            relative = path.relative_to(source).as_posix()
            info = zipfile.ZipInfo(relative, timestamp)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = (0o100644 & 0xFFFF) << 16
            info.create_system = 3
            archive.writestr(info, path.read_bytes(), compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
    return [{"path": path.relative_to(source).as_posix(), "size": path.stat().st_size, "sha256": digest(path)} for path in files]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("root", type=Path)
    parser.add_argument("source", type=Path)
    parser.add_argument("output_zip", type=Path)
    parser.add_argument("output_manifest", type=Path)
    parser.add_argument("--product", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--sequence", required=True, type=int)
    parser.add_argument("--architecture", choices=("x64", "arm64"), required=True)
    args = parser.parse_args()
    root = args.root.resolve()
    source = args.source.resolve()
    output = args.output_zip.resolve()
    if not source.is_dir() or output.is_relative_to(source):
        raise SystemExit("source must be a directory and output must be outside it")
    files = create_zip(root, source, output)
    manifest = {
        "schemaVersion": 1,
        "product": args.product,
        "version": args.version,
        "releaseSequence": args.sequence,
        "architecture": args.architecture,
        "payloadSha256": digest(output),
        "files": files,
    }
    args.output_manifest.parent.mkdir(parents=True, exist_ok=True)
    args.output_manifest.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"Created deterministic {args.product} payload with {len(files)} files: {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
