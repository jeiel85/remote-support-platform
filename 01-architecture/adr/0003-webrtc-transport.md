# ADR-0003: WebRTC for Peer Transport

- Status: Accepted
- Date: 2026-07-02

## Decision

Use native WebRTC with ICE, DTLS-SRTP and SCTP data channels rather than a custom UDP/QUIC NAT traversal protocol.

The Windows client pins `libdatachannel` 0.24.3 at commit
`c6696d157b5612df2a741d9a03b192b47ab6cefb`, uses its C ABI behind the
product C ABI, and builds it with Mbed TLS 3.6.6. The selected media path is
product-managed Media Foundation H.264 (Path A): Annex-B access units enter a
libdatachannel H.264 RTP packetizer, while RTCP PLI/REMB feedback drives the
product encoder.

## Rationale

WebRTC provides mature NAT traversal, relay fallback, congestion feedback and interoperable security primitives. A custom protocol would transfer substantial risk into connectivity, congestion control and cryptographic integration.

## Constraint

Pin the native dependency by tested commit and maintain a quarterly upgrade/security review process.

The default `libjuice` ICE backend supports direct UDP and TURN/UDP. Production
TURN/TCP and TURN/TLS qualification therefore requires the separately selected
`libnice` client build described by Goal 07; route capability must never be
inferred from coturn server support alone.
