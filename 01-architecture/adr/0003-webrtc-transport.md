# ADR-0003: WebRTC for Peer Transport

- Status: Accepted
- Date: 2026-07-01

## Decision

Use native WebRTC with ICE, DTLS-SRTP and SCTP data channels rather than a custom UDP/QUIC NAT traversal protocol.

## Rationale

WebRTC provides mature NAT traversal, relay fallback, congestion feedback and interoperable security primitives. A custom protocol would transfer substantial risk into connectivity, congestion control and cryptographic integration.

## Constraint

Pin the native dependency by tested commit and maintain a quarterly upgrade/security review process.
