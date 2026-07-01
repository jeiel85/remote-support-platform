# Goal 05 — Remote Input and Display Topology

## Goal objective

Implement safe remote pointer and keyboard control with exact multi-monitor and DPI mapping.

## Required work

1. Implement display topology protocol and generation handling.
2. Implement viewport inverse transform in Console.
3. Implement pointer move/button/wheel injection.
4. Implement scan-code keyboard transitions and Unicode text path.
5. Track remote pressed state and release-all behavior.
6. Enforce view/input scopes locally.
7. Detect integrity/secure-desktop limitations and emit capability state.
8. Build automated coordinate/input tests across the compatibility matrix.

## Acceptance criteria

- Corners, edges and center map correctly for negative origins and mixed DPI.
- Rotated monitor tests pass.
- Key/button state never remains stuck after disconnect/revoke/focus loss.
- View-only session cannot inject input even with forged peer message.
- Elevated/UIPI failure is surfaced accurately.
- Emergency local disconnect stops input immediately.

## Forbidden shortcuts

- No arbitrary global keyboard hook in Operator Console.
- No assumption that SendInput succeeded solely from API return without capability/result checks.
- No remote secure-desktop bypass.
- No coordinate mapping based only on WPF logical pixels.

## Required deliverables

- input module;
- topology and viewport tests;
- permission enforcement tests;
- local emergency control;
- compatibility report.

## Audited contract inputs

The implementation for this goal must treat the following files as binding inputs:

- `03-client/input-clipboard.md`
- `03-client/elevation-broker.md`
- `02-protocol/protobuf/remote_support.proto`
- `06-quality/compatibility-matrix.md`

## Evidence package

The goal is complete only when the repository contains:

- runnable implementation and deterministic setup instructions;
- automated tests mapped to the applicable IDs in `07-delivery/acceptance-test-catalog.md`;
- performance/security evidence required by the goal acceptance criteria;
- updated contract, ADR, risk, and compatibility records when behavior differs from the design;
- no unresolved placeholder implementation, disabled security gate, or undocumented manual step.
