# Goal 01 Evidence

## Implemented baseline

- Pinned .NET SDK 10.0.301 with SHA-512-verified non-admin bootstrap.
- Hash-locked Python/CMake environment and centrally pinned NuGet packages with per-project lock files.
- Deterministic OpenAPI operation interfaces, Protobuf C#/C++ output, JSON Schema copies, native header copy, and generation manifest.
- Domain IDs, capability-scope subset enforcement, closed session state transitions, stable error-code generation, correlation IDs, and telemetry event IDs.
- Managed layer project references plus executable architecture tests.
- C11/C++20 ABI compilation tests, secret scanning, dependency vulnerability inspection, SPDX 2.3 SBOM generation, and release evidence verification.
- Windows CI definition with read-only token permissions and credential persistence disabled.

## Local verification commands

```powershell
./build.ps1 -Target ValidateDesign
./build.ps1 -Target Test
./build.ps1 -Target IntegrationTest
./build.ps1 -Target Package -Configuration Release
```

The local test result files are written under ignored `artifacts/test-results`; generated contract hashes are written under `artifacts/contracts`; the SBOM is written under `artifacts/sbom`.

## Requirement mapping

- NFR-MNT-001: generated public contracts and contract tests.
- NFR-MNT-002: ABI 1.1 header compiled as C11 and C++20.
- NFR-MNT-003: CODEOWNERS, module ownership, tests, telemetry, failure sources, and runbook links.
- NFR-MNT-004: pinned tool bootstrap, locks, CI, dependency inspection, and SPDX SBOM.
- NFR-MNT-005: ADR workflow plus architecture boundary tests.
- NFR-MNT-006: PostgreSQL parser/invariant test; live disposable PostgreSQL evidence is recorded separately when executed.

