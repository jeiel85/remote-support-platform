# Native Bridge Contract

## 1. Scope

`02-protocol/native/remote_support_native.h` is the binding managed/native contract for capture, encoding, rendering, WebRTC transport, and local input injection. Product code must not add ad-hoc P/Invoke entry points outside this header without an ABI review and compatibility test.

## 2. Versioning

- `RS_NATIVE_ABI_MAJOR` changes only for an incompatible calling convention, ownership, layout, or semantic break.
- Minor versions are additive. New struct fields are appended and guarded by `struct_size`.
- Released enum numeric values and exported symbol semantics are never reused.
- Managed startup checks ABI major, minimum minor, build ID, architecture, and signed module provenance before creating a runtime.

## 3. Ownership and threading

- Opaque handles are native-owned and released only by their matching destroy function.
- Callback data is borrowed and copied before callback return when it must outlive the call.
- Destruction is dispatched outside callback execution; reentrant destroy is prohibited.
- Managed callback delegates are pinned for the full runtime lifetime.
- Each module documents allowed calling threads. UI HWND operations are marshalled to the owning UI thread; WebRTC and capture callbacks are marshalled through bounded queues.
- No exception crosses the ABI. Stable status and error codes cross the boundary; detailed diagnostics remain redacted and local.

## 4. Media modes

The WebRTC spike selects one video input mode for GA:

- `RS_VIDEO_INPUT_MODE_ENCODED_H264`: the Media Foundation encoder produces H.264 access units and the transport integrates them with WebRTC RTP/congestion control.
- `RS_VIDEO_INPUT_MODE_D3D11_TEXTURE`: WebRTC owns encoding and accepts capture textures through the bridge.

A build may expose both only after each path has independent compatibility evidence. A session selects exactly one mode; submitting the other frame type returns `RS_STATUS_INVALID_STATE`.

## 5. Data-channel mapping

Managed code opens channels using the labels and reliability profile in `03-client/webrtc-integration.md`. The returned runtime `channel_id` is process-local and must not be serialized into the peer protocol. Payload size, buffered amount, backpressure, and close behavior are enforced before invoking the native call.

## 6. D3D11 interoperability

The implementation defines one D3D11 device/adapter ownership strategy per process. A texture passed across the ABI must either:

1. originate from the shared native runtime device; or
2. be opened using an explicitly documented shared-handle/fence path.

Adapter LUID, format, dimensions, color metadata, keyed-mutex/fence requirements, and callback lifetime are validated. Cross-adapter implicit copies are prohibited in the low-latency path.

## 7. Input safety

The input injector is a low-level mechanism, not an authorization boundary. The managed host/elevation broker validates session, peer, scope, permission revision, transport epoch, topology generation, local override, rate limits, and secure-desktop capability before invoking it. The native layer still rejects stale topology and malformed sequences and provides release-all on teardown.

## 8. Required ABI tests

- compile the header as C11 and C++20;
- load expected/mismatched versions and architectures;
- verify every exported symbol and calling convention;
- fuzz struct sizes, null pointers, strings, buffers and invalid enums;
- verify callback lifetime and destruction races under sanitizers/application verifier;
- run 1,000 create/start/stop/destroy cycles per handle type;
- inject GPU/device loss, transport failure, timeout and cancellation;
- verify no managed delegate, native handle, D3D object, thread or buffer leak;
- compare ABI layout/exports against the latest released artifact in CI.
