# ADR-0002: Native C++ Media Core with Stable C ABI

- Status: Accepted
- Date: 2026-07-01

## Context

Desktop capture, GPU textures, Media Foundation and native WebRTC require low-level Windows and C++ integration. Implementing all of this through managed wrappers increases GC and wrapper-lifecycle risk.

## Decision

Implement capture, color conversion, encoding, transport and rendering in C++20. Expose a versioned C ABI consumed by .NET through source-generated P/Invoke or equivalent.

## Consequences

- Better control of GPU/native resources.
- Higher build and memory-safety burden.
- Mandatory sanitizers, fuzzing, narrow boundary and deterministic ownership rules.
