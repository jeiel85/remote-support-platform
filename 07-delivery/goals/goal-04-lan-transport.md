# Goal 04 — LAN Peer Session

## Goal objective

Establish a direct peer session on a LAN using native WebRTC, carrying screen video and control-channel heartbeats.

## Required work

1. Integrate pinned native WebRTC dependency behind the native bridge.
2. Implement local/manual signaling for development only.
3. Create video RTP track and receive/render path.
4. Create reliable control DataChannel and heartbeat.
5. Implement peer capability exchange and protocol version checks.
6. Add clean connect/disconnect/reconnect lifecycle.
7. Export transport metrics.
8. Add malformed/oversized message validation.

## Acceptance criteria

- Two clean Windows machines establish a direct LAN session.
- Screen renders and adapts without memory/handle leak.
- Session ends cleanly from either peer.
- Repeated 100-session connect/disconnect test passes.
- Unsupported protocol versions fail clearly.
- Existing capture/encoding performance remains within budget.

## Forbidden shortcuts

- Manual signaling is not reused as production authentication.
- Do not expose an unauthenticated listening TCP port.
- Do not log raw credentials or full SDP by default.
- Do not couple WPF UI directly to WebRTC callbacks.

## Required deliverables

- peer transport module;
- LAN demo Agent/Console;
- lifecycle test suite;
- transport metric catalog;
- dependency pin and build documentation.

## Audited contract inputs

The implementation for this goal must treat the following files as binding inputs:

- `03-client/webrtc-integration.md`
- `02-protocol/control-channel.md`
- `02-protocol/protobuf/remote_support.proto`
- `02-protocol/signaling-protocol.md`

## Evidence package

The goal is complete only when the repository contains:

- runnable implementation and deterministic setup instructions;
- automated tests mapped to the applicable IDs in `07-delivery/acceptance-test-catalog.md`;
- performance/security evidence required by the goal acceptance criteria;
- updated contract, ADR, risk, and compatibility records when behavior differs from the design;
- no unresolved placeholder implementation, disabled security gate, or undocumented manual step.

## Mandatory security spike

Implement and test reciprocal `TransportBinding` against the actual WebRTC DTLS fingerprints. No video/control acceptance test may pass while this gate is bypassed. Produce packet captures and negative tests for substituted fingerprints, stale epochs, changed scopes, and replayed binding messages.
