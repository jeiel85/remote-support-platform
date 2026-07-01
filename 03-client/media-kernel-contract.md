# Media Kernel Execution Contract

This document closes the implementation choices that were previously left implicit between Goals 02 and 03. The public ABI in `../02-protocol/native/remote_support_native.h` is authoritative; this document fixes behavior, limits, and evidence expectations for ABI minor version 1.

## 1. Runtime and callback model

- A runtime owns one D3D11 device and adapter. Capture, encoder, decoder, and renderer handles created from that runtime use that device.
- Callbacks for one handle are serialized, but callbacks from different handles may run concurrently.
- Callback payloads are borrowed until callback return. A callback must not destroy the handle that invoked it.
- Capture and encode queues default to three frames and accept capacities from two through six. When full, the oldest frame not required as an encoder reference is dropped.
- `RS_STATUS_WOULD_BLOCK` means the caller may retry after progress; it never transfers ownership.
- All timestamps are unsigned nanoseconds from `QueryPerformanceCounter`, converted without using wall-clock time. RTP timestamps use a 90 kHz clock derived from the same origin and never regress.

## 2. Capture targets and discovery

`rs_runtime_enumerate_displays` synchronously reports the active desktop topology. Display IDs use the UTF-8 form of the DisplayConfig monitor device path and are stable until a topology generation change. The callback data expires when the callback returns.

Capture source selection is deterministic:

1. `RS_CAPTURE_SOURCE_AUTO` selects DXGI Desktop Duplication for a display target and WGC for a window target.
2. `RS_CAPTURE_SOURCE_DXGI` accepts display targets only.
3. `RS_CAPTURE_SOURCE_WGC` accepts display or window targets.
4. `RS_CAPTURE_SOURCE_SYNTHETIC` accepts only the synthetic target and is test-only.

Window target IDs are lowercase hexadecimal HWND values prefixed with `hwnd:`. A caller must obtain the HWND through a local user selection surface; remote input cannot choose an arbitrary window. An invalid, closed, protected, or cross-session target fails closed with a stable error.

Topology generations start at one, increase after any monitor add/remove, mode, rotation, DPI, HDR, or adapter change, and never decrease in a runtime. A frame identifies the generation under which its target and dimensions were resolved.

Each frame includes the selected target's `desktop_origin_x` and `desktop_origin_y` in canonical virtual-desktop physical pixels. The renderer subtracts that origin from cursor coordinates before applying the frame-to-view transform; this is required for negative monitor origins.

## 3. Cursor contract

Cursor updates are independent of video frames. `on_cursor` carries position in canonical virtual-desktop physical pixels, visibility, hotspot, shape ID, dimensions, pitch, and an optional BGRA8 premultiplied shape. A repeated shape ID may omit bytes. A hidden cursor has zero shape bytes. Shape buffers are limited to 256 KiB and dimensions to 256 by 256.

The renderer composites the latest cursor whose display generation matches the rendered frame. A stale cursor generation is discarded.

## 4. Rendering contract

- FIT preserves aspect ratio and letterboxes.
- ACTUAL_SIZE maps one source pixel to one destination physical pixel.
- STRETCH fills the target rectangle.
- `rs_renderer_set_transform` adds pan and zoom after the selected base view mode. Zoom is finite and in the inclusive range 0.25 through 8.0. Pan is expressed in source pixels.
- `rs_renderer_submit_cursor` copies the bounded cursor payload and applies it only to frames with the same display generation. Shape bytes may be omitted when the submitted shape ID matches the renderer cache.
- Resize and transform changes are applied before the next submitted frame. Rendering rejects frames from a different runtime device.

## 5. H.264 encoder contract

The encoder enumerates Media Foundation transforms in this order: hardware transforms on the runtime adapter, other hardware transforms that support an explicit shared-texture path, then Microsoft software H.264. Every candidate is validated with a color-bar frame before selection.

The wire form is one complete Annex-B H.264 access unit per `on_encoded_frame` callback. Parameter sets precede every keyframe. The initial compatibility baseline is 8-bit 4:2:0, progressive, no B-frames, with constrained-baseline or main profile chosen from decoder capability. The selected profile and level are reported in frame metadata.

Quality profiles define policy, not a vendor-specific encoder setting:

| Profile | Frame-rate bias | Scale bias | Quality behavior |
|---|---:|---:|---|
| TEXT | 15 fps | preserve native scale longest | favor sharp edges and lower QP variance |
| BALANCED | 30 fps | scale after sustained pressure | balance motion and detail |
| MOTION | 60 fps when supported | scale before latency grows | favor temporal smoothness |

`rs_encoder_reconfigure` atomically changes width, height, rate, and profile. A dimension change drains or drops stale input, recreates the transform, and makes the first output a keyframe. Rate-only changes do not reset timestamps. The maximum keyframe interval defaults to two seconds and is bounded from 250 through 10,000 milliseconds.

Hardware failure emits `on_encoder_fallback` with the failed backend and stable reason, then attempts software fallback within two seconds when allowed. Silent fallback is forbidden.

## 6. Decoder contract

The decoder accepts the encoder's Annex-B access units. It outputs NV12 or BGRA8 D3D11 textures through `on_decoded_frame`, preserves frame and monotonic timestamps, and returns `RS_STATUS_INVALID_STATE` for delta frames before a keyframe. `rs_decoder_reset` discards reference state and requires a new keyframe. A resolution change is accepted only on a keyframe and recreates the output pool without leaking the previous pool.

## 7. Resource and input limits

- Width and height: 16 through 8192 pixels.
- Target frame rate: 1 through 120 frames per second.
- Target bitrate: 64,000 through 100,000,000 bits per second and never above configured maximum.
- Encoded access unit: at most 16 MiB.
- Capture acquisition timeout: at most 1,000 milliseconds per attempt.
- Device-loss retries: 100 ms, 250 ms, 500 ms, 1 s, then 2 s, with a 30-second cumulative failure surfaced to the caller.

All invalid sizes, enum values, struct sizes, null pointers, and incompatible runtime devices are rejected before state changes.

## 8. Required evidence split

Deterministic CI evidence covers the synthetic source, bounded queues, timestamp monotonicity, profile decisions, encode/decode stream semantics, keyframe/reset, fallback state machine, ABI layout, and repeated teardown. Windows hardware-lab evidence separately covers DXGI, WGC, real cursor shapes, D3D11 rendering, Media Foundation hardware transforms, adapter loss, display changes, latency, GPU memory, and vendor compatibility. Goal completion requires both sets; a synthetic result cannot be presented as hardware evidence.

## 9. Transport binding inputs

ABI 1.2 requires `rs_transport_binding_options_v1` for every peer transport. The caller supplies the authenticated remote peer ID and role, current permission revision and canonical scope names, a 32-byte authorization-context hash, and an authorized ephemeral P-256 key pair plus the reciprocal peer public key. Raw private scalars are copied on create, locked in process memory when supported, zeroed on every close path, and never logged.

`rs_runtime_generate_peer_key_pair` produces a P-256 private scalar and SEC1 uncompressed public point for the authorization flow. A transport parses the local and remote SHA-256 DTLS fingerprints from the descriptions active in the WebRTC peer connection, signs `RSP-TRANSPORT-BINDING-V1` with ECDSA P-256/SHA-256 in P1363 form, and verifies the reciprocal binding. A transport remains pre-content until both the binding and its acknowledgement verify. Any identity, role, epoch, permission revision, scope, authorization hash, fingerprint, key ID, signature, or replay mismatch is fatal.
