#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path


def main() -> int:
    root = Path(sys.argv[1]).resolve()
    locks = sorted(root.rglob("packages.lock.json"))
    packages: dict[tuple[str, str], dict[str, str]] = {}
    for lock in locks:
        document = json.loads(lock.read_text(encoding="utf-8"))
        for framework in document["dependencies"].values():
            for name, details in framework.items():
                if "resolved" not in details:
                    continue
                packages[(name, details["resolved"])] = {"name": name, "version": details["resolved"]}
    digest = hashlib.sha256(b"".join(path.read_bytes() for path in locks)).hexdigest()
    timestamp = subprocess.run(
        ["git", "show", "-s", "--format=%cI", "HEAD"], cwd=root, check=True, capture_output=True, text=True
    ).stdout.strip()
    created = datetime.fromisoformat(timestamp).astimezone(timezone.utc).isoformat().replace("+00:00", "Z")
    spdx_packages = []
    relationships = []
    for index, package in enumerate(sorted(packages.values(), key=lambda item: (item["name"].lower(), item["version"]))):
        spdx_id = f"SPDXRef-Package-{index}"
        spdx_packages.append({
            "SPDXID": spdx_id,
            "name": package["name"],
            "versionInfo": package["version"],
            "downloadLocation": f"https://api.nuget.org/v3-flatcontainer/{package['name'].lower()}/{package['version'].lower()}/{package['name'].lower()}.{package['version'].lower()}.nupkg",
            "filesAnalyzed": False,
            "licenseConcluded": "NOASSERTION",
            "licenseDeclared": "NOASSERTION",
            "copyrightText": "NOASSERTION",
        })
        relationships.append({"spdxElementId": "SPDXRef-DOCUMENT", "relationshipType": "DESCRIBES", "relatedSpdxElement": spdx_id})
    document = {
        "spdxVersion": "SPDX-2.3",
        "dataLicense": "CC0-1.0",
        "SPDXID": "SPDXRef-DOCUMENT",
        "name": "remote-support-platform",
        "documentNamespace": f"https://remote-support.invalid/spdx/{digest}",
        "creationInfo": {"created": created, "creators": ["Tool: remote-support-create-sbom/1.0"]},
        "packages": spdx_packages,
        "relationships": relationships,
    }
    output = root / "artifacts/sbom/remote-support.spdx.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"Wrote SPDX 2.3 SBOM with {len(spdx_packages)} NuGet packages: {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
