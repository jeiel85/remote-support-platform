# Goal 03 — Low-Latency Encoder

## Goal objective

Add an adaptive H.264 encoding and decoding pipeline with hardware preference, software fallback and low-latency behavior.

## Required work

1. Enumerate Media Foundation encoders and capabilities.
2. Build GPU conversion/scaling to encoder input format.
3. Implement hardware encoder selection and validation.
4. Implement software fallback.
5. Support dynamic bitrate, frame rate and resolution.
6. Implement keyframe requests and decoder reset.
7. Add text/balanced/motion quality profiles.
8. Create deterministic encode/decode and latency benchmarks.

## Acceptance criteria

- Encoded stream decodes correctly after start, keyframe and resolution change.
- Hardware failure triggers bounded fallback.
- Queue depth remains bounded and stale frames are dropped.
- Capture-to-decode latency is measured and reported.
- Idle desktop bitrate falls materially below active motion bitrate.
- No frame timestamp regression or memory leak in soak tests.

## Forbidden shortcuts

- No fixed high bitrate independent of network/content.
- No B-frame configuration that creates unacceptable latency.
- No encoder vendor assumption without capability probing.
- No silent fallback; telemetry and UI diagnostics must report it.

## Required deliverables

- encoder/decoder modules;
- adaptation interface;
- benchmark harness and results;
- codec capability report;
- failure-injection tests.

## Audited contract inputs

The implementation for this goal must treat the following files as binding inputs:

- `03-client/webrtc-integration.md`
- `03-client/media-kernel-contract.md`
- `01-architecture/media-pipeline.md`
- `02-protocol/native/remote_support_native.h`
- `06-quality/performance-and-slo.md`

## Evidence package

The goal is complete only when the repository contains:

- runnable implementation and deterministic setup instructions;
- automated tests mapped to the applicable IDs in `07-delivery/acceptance-test-catalog.md`;
- performance/security evidence required by the goal acceptance criteria;
- updated contract, ADR, risk, and compatibility records when behavior differs from the design;
- no unresolved placeholder implementation, disabled security gate, or undocumented manual step.
