# Native WebRTC Integration

## 1. Integration decision

The client uses one pinned native WebRTC revision exposed through the product's C ABI. The build must be reproducible and license-reviewed. Product code does not depend directly on unstable C++ WebRTC headers outside the transport module.

## 2. Video path options

The implementation spike must select one supported path and record an ADR:

### Path A — WebRTC encoder factory integration

- Capture produces D3D11 textures.
- Product encoder factory wraps Media Foundation H.264 encoders.
- Encoded frames enter WebRTC with correct RTP timestamps, frame type, QP where available, codec profile/level, and fragmentation metadata.
- WebRTC congestion control drives target bitrate, frame rate, and keyframe requests.

### Path B — WebRTC-managed encoder

- Capture frames are converted to a supported native buffer.
- WebRTC selects/configures hardware encoding through its supported Windows integration.
- Product retains capture, scaling, quality profile, and fallback policy.

Do not combine two independent congestion controllers. The selected encoder must respond to WebRTC bitrate allocation and keyframe requests.

## 3. Required spike evidence

- 1080p30 office-motion latency and CPU/GPU profile;
- idle bitrate convergence;
- hardware encoder enumeration and fallback on representative Intel/AMD/NVIDIA devices;
- resize, monitor switch, GPU reset, sleep/resume, RDP transition;
- decoder interoperability between supported product versions;
- packet loss/jitter adaptation and keyframe recovery;
- no unbounded frame queue;
- clean teardown over 1,000 connect/disconnect cycles.

## 4. Data channels

| Label | Ordered | Reliability | Purpose |
|---|---:|---|---|
| `rsp.control.v1` | Yes | reliable | hello, permissions, topology, lifecycle, errors |
| `rsp.input.fast.v1` | No | max retransmits 0 | pointer move and wheel only |
| `rsp.input.reliable.v1` | Yes | reliable | buttons, keys, release-all, acknowledgements |
| `rsp.clipboard.v1` | Yes | reliable | clipboard offer/decision/text |
| `rsp.file.control.v1` | Yes | reliable | offers, decisions, resume, cancellation, completion |
| `rsp.file.data.v1` | Yes | reliable | bounded chunks with application backpressure |
| `rsp.chat.v1` | Yes | reliable | chat and delivery acknowledgement |

Channel labels and semantics are protocol-versioned. Unknown required channel versions fail negotiation; unknown optional channels are ignored.

## 5. Media security binding

- DTLS certificate fingerprint in SDP is checked against the authenticated peer binding exchanged through signaling.
- Session/peer identity is never inferred solely from an SDP string.
- ICE candidates are bounded, parsed, and rate-limited.
- mDNS/private candidates are handled according to the pinned WebRTC behavior and test matrix.
- Renegotiation requires the current transport epoch and authorization.

## 6. Codec baseline

Initial GA codec baseline:

- H.264 constrained baseline/main compatibility profile selected by measured decoder support;
- 4:2:0 8-bit transport path;
- SDR output baseline, with HDR source tone-mapped until an HDR transport profile is separately released;
- no B-frames in low-latency mode;
- bounded keyframe interval plus explicit recovery requests;
- dynamic scale and frame-rate adaptation.

Exact profile/level and packetization mode are pinned by the compatibility spike and recorded in generated capability tests.

## Mandatory transport binding

After DTLS establishment and before application content, each peer reads the local and remote certificate fingerprints from the active WebRTC transport, constructs `TransportBinding`, signs it with its authorized ephemeral peer key, and verifies the reciprocal binding. Video tracks remain muted and all application data-channel handlers reject payloads until both bindings and acknowledgements validate. ICE restart, certificate change, permission revision change, or transport-epoch change invalidates the binding and repeats the exchange. See `../02-protocol/canonicalization-and-signatures.md`.

## API proof key reuse

The ephemeral peer signing key authorized during peer exchange is also the DPoP key for peer REST calls. Its private key remains in the peer process, is never serialized, and is destroyed at session end. A transport restart changes epoch and transport binding but does not silently replace the peer key; key replacement requires a new explicit authorization flow.
