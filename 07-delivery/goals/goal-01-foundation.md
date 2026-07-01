# Goal 01 — Foundation and Contracts

## Goal objective

Create the monorepo, build system, architectural boundaries and executable contract baseline. This goal must make later work reproducible and testable.

## Required work

1. Create the repository layout from `repository-structure.md`.
2. Pin .NET 10 LTS SDK and native toolchain.
3. Add central package/dependency management and lock files.
4. Add OpenAPI and Protobuf generation with compatibility checks.
5. Create domain types for IDs, scopes, states and error codes.
6. Add structured logging, correlation, configuration schema and test helpers.
7. Configure CI for managed/native builds, tests, secret scan, dependency scan and SBOM.
8. Add architecture tests that enforce managed module boundaries.

## Acceptance criteria

- Clean checkout builds with one documented command.
- Generated code is deterministic.
- Unit tests cover session-state transitions and scope subset rules.
- CI has no production/signing secret exposure.
- SBOM and dependency inventory are produced.
- All P0 architecture decisions are represented in code/project boundaries.

## Forbidden shortcuts

- Do not add real remote-control behavior yet.
- Do not use floating dependency versions.
- Do not place shared “utility” code that bypasses module ownership.
- Do not suppress compiler/security warnings without documented rationale.

## Required deliverables

- repository skeleton;
- build and test scripts;
- generated protocol libraries;
- CI pipeline;
- architecture test report;
- updated ADR links.

## Audited contract inputs

The implementation for this goal must treat the following files as binding inputs:

- `IMPLEMENTATION_READINESS.md`
- `02-protocol/openapi/openapi.yaml`
- `02-protocol/protobuf/remote_support.proto`
- `02-protocol/ipc/service_ipc.proto`
- `02-protocol/native/remote_support_native.h`
- `02-protocol/schemas/*.json`
- `07-delivery/bootstrap-and-build.md`

## Evidence package

The goal is complete only when the repository contains:

- runnable implementation and deterministic setup instructions;
- automated tests mapped to the applicable IDs in `07-delivery/acceptance-test-catalog.md`;
- performance/security evidence required by the goal acceptance criteria;
- updated contract, ADR, risk, and compatibility records when behavior differs from the design;
- no unresolved placeholder implementation, disabled security gate, or undocumented manual step.
