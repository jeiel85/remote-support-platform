# Goal 02 — Capture and Rendering Kernel

## Goal objective

Implement reliable local screen capture and local rendering before introducing networking.

## Required work

1. Implement native C ABI and handle lifecycle.
2. Implement DXGI Desktop Duplication for monitor capture.
3. Implement Windows.Graphics.Capture abstraction for selected window/display.
4. Add display topology enumeration and change detection.
5. Add cursor metadata capture.
6. Implement D3D11 renderer in a minimal WPF host.
7. Add synthetic frame source and display-change fault injection.
8. Record timing, resource and error telemetry.

## Acceptance criteria

- 60-minute capture/render soak has no unbounded memory/GPU growth.
- Monitor hot-plug, resolution, DPI and rotation changes recover.
- Access-lost events recreate capture without application restart.
- Cursor position/shape is correct.
- 1080p and 4K reference tests meet measured frame targets on lab hardware.
- Native teardown passes sanitizer/leak checks.

## Forbidden shortcuts

- No GDI as the primary path.
- No CPU readback by default when a GPU path is available.
- No unbounded frame queues.
- No C++ exceptions across the ABI.

## Required deliverables

- native capture library;
- WPF local viewer;
- synthetic source;
- soak/performance test report;
- capture capability matrix.

## Audited contract inputs

The implementation for this goal must treat the following files as binding inputs:

- `01-architecture/media-pipeline.md`
- `03-client/capture-encoder-implementation.md`
- `03-client/media-kernel-contract.md`
- `02-protocol/native/remote_support_native.h`
- `06-quality/compatibility-matrix.md`

## Evidence package

The goal is complete only when the repository contains:

- runnable implementation and deterministic setup instructions;
- automated tests mapped to the applicable IDs in `07-delivery/acceptance-test-catalog.md`;
- performance/security evidence required by the goal acceptance criteria;
- updated contract, ADR, risk, and compatibility records when behavior differs from the design;
- no unresolved placeholder implementation, disabled security gate, or undocumented manual step.
