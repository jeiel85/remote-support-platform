# Peer Control Channel

## 1. WebRTC channel contract

The canonical labels and reliability settings are defined in `03-client/webrtc-integration.md`. Channel creation is deterministic: the offerer creates all initial channels before the first offer. A channel with a known label but incompatible settings fails negotiation.

## 2. Framing

Each DataChannel application message contains a fixed 24-byte header followed by one serialized Protobuf `Envelope` body:

```text
Offset  Size  Field
0       4     ASCII magic "RSP1"
4       2     framing major, unsigned big-endian
6       2     message type hint, unsigned big-endian
8       2     flags, unsigned big-endian
10      2     reserved, MUST be zero
12      8     channel-local sequence, unsigned big-endian
20      4     payload length, unsigned big-endian
24      N     Protobuf Envelope
```

The message type hint is an optimization and must match the Protobuf oneof case. A mismatch is `SIGNAL_PROTOCOL_INVALID`. The full frame must equal `24 + payload_length`; trailing or truncated bytes are rejected.

### Hard limits before negotiation

- control/chat/clipboard message: 256 KiB;
- reliable input message: 64 KiB;
- fast input message: 8 KiB;
- file control message: 256 KiB;
- file data message: 1 MiB;

The negotiated limit is the lower of both peers and the product hard maximum. Allocation occurs only after bounds validation.

## 3. Protocol negotiation

1. Each reliable control channel sends `ProtocolHello` as its first message.
2. The receiver validates session, role, epoch, protocol range, peer ID, and channel limit.
3. The receiver returns `ProtocolHelloAck`.
4. No input, clipboard, file, chat, or lifecycle command is applied before successful hello/ack.
5. Major version mismatch ends the channel. Minor versions use feature negotiation and unknown-field compatibility.

## 4. Sequence and replay

- Each channel has an independent monotonically increasing sequence starting at 1.
- Reliable channels reject duplicate or decreasing sequence values.
- Fast pointer moves may arrive out of order; the host applies only a newer move sequence, but button/key events never use the fast channel.
- Transport epoch changes reset channel-local sequence and invalidate prior-epoch frames.
- `message_id` is unique per peer/epoch and supports diagnostics/idempotent control handling.

## 5. Input correctness

- Key/button transitions carry `input_sequence` on the reliable input channel.
- Host tracks remotely pressed state independently from local physical state.
- On gap, permission revision change, channel reset, desktop transition, or disconnect, host releases all remote state.
- Pointer clicks are applied only against the stated display topology generation.
- Input acknowledgements report applied/rejected sequence and a stable error code.

## 6. Permission changes

`PermissionState` contains:

- monotonically increasing permission revision;
- active and revoked typed scopes;
- reliable input sequence at which the change becomes effective;
- stable reason code.

A peer may locally revoke immediately before server propagation. Scope additions require a new server authorization and, for attended access, local consent.

## 7. Backpressure

- Control and reliable input channels reserve bounded queues and cannot be starved by file transfer.
- Pointer moves are coalesced before serialization.
- File sending pauses at the configured buffered-amount high-water mark and resumes below low-water mark.
- Queue overflow generates a stable error and cancels the affected feature rather than growing memory unbounded.

## 8. Heartbeat

Heartbeat is liveness and reconciliation metadata, not authentication. Missing heartbeat alone does not end a healthy WebRTC connection; the state machine combines DataChannel, ICE/DTLS, media, and signaling observations.

## Pre-content gate

`ProtocolHello` may be exchanged only to negotiate protocol bounds. All other messages, media rendering, input injection, clipboard, chat, and file transfer are blocked until reciprocal `TransportBinding` and `TransportBindingAck` messages validate for the current transport epoch and permission revision. A failed binding closes the channels and records a security audit event.
