# Repository Structure

Recommended monorepo during initial product development:

```text
remote-support-platform/
├─ README.md
├─ docs/
│  ├─ architecture/
│  ├─ security/
│  ├─ runbooks/
│  └─ adr/
├─ src/
│  ├─ client/
│  ├─ server/
│  ├─ portal/
│  └─ shared-contracts/
├─ deploy/
│  ├─ local/
│  ├─ staging/
│  ├─ production/
│  ├─ turn/
│  └─ observability/
├─ tests/
│  ├─ e2e/
│  ├─ compatibility/
│  ├─ performance/
│  └─ security/
├─ tools/
│  ├─ lab-controller/
│  ├─ protocol-fuzzer/
│  ├─ support-bundle-validator/
│  └─ release-verifier/
├─ schemas/
│  ├─ openapi/
│  ├─ protobuf/
│  ├─ config/
│  └─ events/
├─ .github/ or ci/
├─ deps.lock
├─ global.json
└─ LICENSES/
```

## Branch and release model

- protected `main`;
- short-lived feature branches;
- signed release tags;
- release branches only when servicing requires them;
- mandatory review for security, updater, IPC, policy and protocol modules;
- generated artifacts are reproducible and not edited manually.

## Ownership

Use CODEOWNERS or equivalent:

- native media and input;
- service/IPC;
- identity/policy;
- updater/signing;
- TURN/deployment;
- audit/privacy.

## Versioning

- Product version: semantic version with separate monotonically increasing update sequence.
- API: `/v1`, additive compatible changes by default.
- Protobuf: never reuse field numbers; reserve removed fields.
- IPC/native ABI: explicit major/minor negotiation.
- Configuration: schema version with migration.

## Release build boundaries

The attended release graph excludes `RemoteSupport.Service` and the Managed Host installer from published artifacts. Goal 13 enables those projects in a distinct product/package and CI release train. Goal 14 is policy/server/client capability-gated independently; its code cannot be activated merely by installing Managed Host. `RemoteSupport.AdminPortal` is an ASP.NET Core Blazor BFF and calls the API only through generated clients.
