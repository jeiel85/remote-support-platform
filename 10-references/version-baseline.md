# Version Baseline

Verified on 2026-07-01. These values are an implementation starting point, not permission to ignore later servicing or security releases.

| Component | Verified candidate baseline | Pinning and upgrade policy |
|---|---|---|
| .NET | .NET 10 LTS; current servicing line at verification was 10.0.9 | pin SDK via `global.json`; remain on current supported patch after validation |
| C++ | C++20 with supported MSVC and Windows SDK | pin protected Windows build image and toolset |
| Windows client | Microsoft-supported Windows 10/11 builds at release | publish exact tested build matrix; remove unsupported builds |
| coturn | 4.14.0 / immutable image revision 4.14.0-r0 available | pin image digest; monitor upstream security releases; validate before promotion |
| PostgreSQL | PostgreSQL 18; current documentation/release line at verification was 18.4 | managed service preferred; pin supported minor and test upgrades |
| Valkey | 9.1.0 GA, optional | use only for demonstrated ephemeral need; PostgreSQL remains source of truth |
| Native WebRTC | selected tested commit SHA | lock source/toolchain/patches; compatibility and license gate on every update |
| Protobuf | selected supported release | central lock and wire-compatibility check |
| OpenTelemetry | selected supported release | central lock and metric-schema/cardinality review |

Production prohibits preview runtimes, floating package versions, floating container tags, and unrecorded native patches. Human-readable tags may appear in local development examples; protected release environments use immutable digests/hashes and archived provenance.
