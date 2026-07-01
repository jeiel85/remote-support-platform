# Bootstrap Utilities

These files are implementation helpers. They do not replace the contracts or goal documents.

- `initialize-repository.ps1` creates the initial .NET repository skeleton on Windows.
- `validate-bundle.py` validates JSON Schemas and examples, OpenAPI 3.1, Protobuf compilation, PostgreSQL parsing, local links, scope/state synchronization, native ABI symbols, requirement traceability, acceptance coverage, placeholders, and the SHA-256 manifest.
- `update-manifest.py` deterministically refreshes the design-bundle manifest after an approved contract or design change. Implementation files outside the numbered bundle directories are intentionally out of manifest scope.

Install validator dependencies in an isolated Python environment:

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install -r .\11-bootstrap\requirements.txt
```

On Linux/macOS, activate with `source .venv/bin/activate`.

Run bundle validation from the bundle root:

```powershell
python ./11-bootstrap/validate-bundle.py .
```

After an approved bundle edit, refresh and revalidate it:

```powershell
python ./11-bootstrap/update-manifest.py
python ./11-bootstrap/validate-bundle.py .
```

Create a new implementation repository from a clean destination on a Windows development machine:

```powershell
pwsh ./11-bootstrap/initialize-repository.ps1 -Destination D:\Project\remote-support-platform
```

The initializer refuses to write into a non-empty directory. It creates a compilable baseline skeleton and package lock files; Goal 01 then adds the selected contract generators, native build, CI, packaging, and full canonical build targets defined in `07-delivery/bootstrap-and-build.md`.
