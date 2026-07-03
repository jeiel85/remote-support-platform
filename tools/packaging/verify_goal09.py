#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import struct
import subprocess
import tempfile
import zipfile
from pathlib import Path, PurePosixPath


MACHINES = {"x64": 0x8664, "arm64": 0xAA64}


def digest_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def digest(path: Path) -> str:
    return digest_bytes(path.read_bytes())


def safe_name(name: str) -> bool:
    path = PurePosixPath(name)
    return not path.is_absolute() and ".." not in path.parts and ":" not in name and "\\" not in name and bool(path.name)


def pe_machine(path: Path) -> int:
    data = path.read_bytes()
    if data[:2] != b"MZ" or len(data) < 0x40:
        raise AssertionError(f"not a PE executable: {path}")
    offset = struct.unpack_from("<I", data, 0x3C)[0]
    if data[offset:offset + 4] != b"PE\0\0":
        raise AssertionError(f"invalid PE header: {path}")
    return struct.unpack_from("<H", data, offset + 4)[0]


def verify_zip(archive_path: Path, manifest_path: Path, allow_unsigned: bool) -> str:
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    architecture = manifest["architecture"]
    assert architecture in MACHINES and manifest["schemaVersion"] == 1
    assert digest(archive_path) == manifest["payloadSha256"]
    declared = {entry["path"]: entry for entry in manifest["files"]}
    assert len(declared) == len(manifest["files"])
    with zipfile.ZipFile(archive_path) as archive:
        actual = {entry.filename: entry for entry in archive.infolist() if not entry.is_dir()}
        assert set(actual) == set(declared)
        assert all(safe_name(name) for name in actual)
        for name, entry in actual.items():
            payload = archive.read(entry)
            expected = declared[name]
            assert len(payload) == expected["size"] and digest_bytes(payload) == expected["sha256"]
        executable_name = "RemoteSupport.Agent.exe" if manifest["product"] == "PORTABLE_AGENT" else "RemoteSupport.Operator.Console.exe"
        assert executable_name in actual
        with tempfile.TemporaryDirectory(prefix="rsp-pe-") as temporary:
            executable = Path(temporary, executable_name)
            executable.write_bytes(archive.read(executable_name))
            assert pe_machine(executable) == MACHINES[architecture]
            if not allow_unsigned:
                verify_authenticode(executable)
    return architecture


def verify_authenticode(path: Path) -> None:
    escaped = str(path).replace("'", "''")
    result = subprocess.run(
        ["powershell", "-NoProfile", "-Command", f"(Get-AuthenticodeSignature -LiteralPath '{escaped}').Status"],
        check=True, capture_output=True, text=True,
    ).stdout.strip()
    if result != "Valid":
        raise AssertionError(f"Authenticode signature is not valid: {path} ({result})")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    parser.add_argument("--allow-unsigned-development", action="store_true")
    args = parser.parse_args()
    directory = args.directory.resolve()
    provenance_path = directory / "provenance.json"
    provenance = json.loads(provenance_path.read_text(encoding="utf-8"))
    assert provenance["schemaVersion"] == 1
    if not args.allow_unsigned_development:
        assert provenance["signed"] is True and provenance["sourceDirty"] is False
    for artifact in provenance["artifacts"]:
        path = directory / artifact["name"]
        assert path.is_file() and path.stat().st_size == artifact["size"] and digest(path) == artifact["sha256"]
    architectures: set[str] = set()
    for manifest in sorted(directory.glob("RemoteSupport-Agent-*.manifest.json")):
        archive = manifest.with_name(manifest.name.replace(".manifest.json", ".zip"))
        architectures.add(verify_zip(archive, manifest, args.allow_unsigned_development))
    assert architectures == set(provenance["architectures"])
    for setup in directory.glob("RemoteSupport-Operator-Setup-*.exe"):
        architecture = setup.stem.rsplit("-", 1)[-1]
        assert pe_machine(setup) == MACHINES[architecture]
        if not args.allow_unsigned_development:
            verify_authenticode(setup)
    assert len(list(directory.glob("RemoteSupport-Operator-Setup-*.exe"))) == len(architectures)
    print(f"Verified Goal 09 packages for {', '.join(sorted(architectures))}; signed={provenance['signed']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
