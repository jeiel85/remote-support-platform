# Module Ownership and Failure Boundaries

| Module | Owner alias | Tests | Telemetry | Failure-mode source | Operations linkage |
|---|---|---|---|---|---|
| Domain/Application | `@jeiel85` | unit and architecture tests | correlation and stable domain errors | session state and error contracts | control-plane outage runbook |
| Contracts/Protocol | `@jeiel85` | contract generation and compatibility tests | protocol version/error counters | protocol documents | security incident runbook |
| Native media | `@jeiel85` | C/C++ ABI, capture, codec, soak and fault tests | capture/encode/decode events | media kernel contract | client compatibility evidence |
| Peer data / attended products | `@jeiel85` | parser fuzz, data-policy, product bridge and package tests | metadata-only transfer/session results | peer control and file-transfer contracts | client diagnostics and release evidence |
| IPC/Security | `@jeiel85` | boundary, identity and fuzz tests | security event IDs | native/IPC contracts | security incident runbook |
| Build/supply chain | `@jeiel85` | clean restore, lock, SBOM and release verification | CI result and provenance | bootstrap/build contract | signing-key compromise runbook |
| Deployment/operations | `@jeiel85` | integration and rehearsal tests | service SLI catalog | deployment topology | operations runbooks |

The initial accountable owner is 박용은 (`@jeiel85`, `jeiel85@gmail.com`) for Product, Security, Privacy, Operations, Legal, native media, protocol, and release roles. These roles must be separated into protected teams before attended GA; a split changes CODEOWNERS and this table in the same reviewed change.
