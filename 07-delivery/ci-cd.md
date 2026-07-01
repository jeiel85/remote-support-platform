# CI/CD and Release Pipeline

## 1. Pull request pipeline

1. Validate formatting and generated files.
2. Restore only from approved registries.
3. Build managed and native code.
4. Run unit/component tests.
5. Static analysis and secret scan.
6. Dependency/license scan.
7. Generate provisional SBOM.
8. Contract compatibility check for OpenAPI/Protobuf.
9. Build unsigned test artifacts.

Untrusted pull-request jobs have no signing or production secrets.

## 2. Main pipeline

- repeat PR gates;
- integration tests with ephemeral PostgreSQL and TURN;
- package local installers;
- publish internal artifacts;
- deploy integration environment;
- run end-to-end smoke tests.

## 3. Release candidate pipeline

- compatibility lab matrix;
- network impairment suite;
- native sanitizer/fuzz campaign;
- installer upgrade/uninstall tests;
- security and tenant-isolation suite;
- generate final SBOM/provenance;
- sign binaries and packages in protected job;
- generate/sign update metadata;
- publish immutable artifacts;
- verify from a clean machine using release-verifier tool.

## 4. Production rollout

- deployment manifests reviewed and signed/approved;
- database migrations backward compatible;
- control plane deployed canary then full;
- client update channel internal → canary → staged percentages;
- monitor defined rollback signals;
- promote only after observation gate.

## 5. Rollback

- Server: previous container plus compatible schema; forward-fix when migration is irreversible.
- Client: updater may restore previous verified artifact after failed health check, but rollback metadata itself has a new signed sequence.
- TURN: replace node image/config, drain allocations where possible.

## 6. Release evidence

Archive:

- source commit/tag;
- dependency lock and SBOM;
- test reports;
- signatures and certificate chain metadata;
- artifact hashes;
- deployment and update metadata;
- approval records;
- compatibility and security exceptions.
