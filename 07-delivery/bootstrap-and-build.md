# Bootstrap and Build Contract

## 1. Required developer environment

- Windows 11 development workstation for the full client build.
- Visual Studio Build Tools with MSVC C++20, Windows SDK, CMake, and .NET desktop workload.
- .NET 10 SDK pinned by `global.json`.
- PowerShell 7.
- Git with long-path support.
- Docker-compatible runtime for PostgreSQL, Valkey, coturn, and backend integration tests.

Backend-only development may run on Windows or Linux, but release client artifacts are produced in protected Windows workers.

## 2. Canonical commands

The implementation repository must expose these commands from its root:

```powershell
./build.ps1 -Target Restore
./build.ps1 -Target GenerateContracts
./build.ps1 -Target Build -Configuration Debug
./build.ps1 -Target Test
./build.ps1 -Target IntegrationTest
./build.ps1 -Target Package -Configuration Release
./build.ps1 -Target VerifyRelease -Artifacts ./artifacts
```

CI calls the same script. IDE-only steps are forbidden.

## 3. Generated contracts

`GenerateContracts` must:

1. validate OpenAPI and JSON Schemas;
2. compile peer and service IPC Protobuf for C# and C++;
3. generate API clients/server interfaces;
4. compare generated output with committed output when that policy is chosen;
5. run compatibility checks against the latest released contract.

## 4. Artifact layout

```text
artifacts/
├─ bin/<component>/<rid>/
├─ packages/
├─ installers/
├─ symbols-private/
├─ sbom/
├─ test-results/
├─ coverage/
├─ contracts/
└─ release-evidence/
```

Private symbols and dumps are never published to public package feeds.

## 5. Configuration

- Development defaults are non-secret and local-only.
- Secrets use development secret storage or environment injection.
- Production configuration is validated at startup against a versioned schema.
- Unknown security-sensitive keys fail startup; obsolete keys produce an explicit migration error.

## 6. Definition of build reproducibility

A clean protected worker using the lock files and pinned toolchain must produce artifacts with identical source/dependency provenance. Byte-for-byte identity is required where toolchains support deterministic output; otherwise the documented nondeterministic fields are normalized or attested.
