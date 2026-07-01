# Native WebRTC Build

`eng/bootstrap-webrtc.ps1` restores the two security-sensitive transport
dependencies into ignored `.tools` paths and verifies their immutable pins:

| Dependency | Pin | Integrity |
|---|---|---|
| libdatachannel | 0.24.3 / `c6696d157b5612df2a741d9a03b192b47ab6cefb` | Git commit plus recursive submodule pins |
| Mbed TLS | 3.6.6 | SHA-256 `8fb65fae8dcae5840f793c0a334860a411f884cc537ea290ce1c52bb64ca007a` |

Run `./build.ps1 -Target Test -Configuration Debug` from a normal PowerShell
session. No global SDK install is required beyond the repository bootstrap
prerequisites. The build is static and enables DTLS-SRTP, SRTP, SCTP, H.264 RTP,
and WebSocket support. Third-party warnings remain visible but do not inherit
the product target's warnings-as-errors policy.

The product boundary is ABI 1.2 in `02-protocol/native/remote_support_native.h`.
Only `src/client/native/src/transport.cpp` consumes libdatachannel. Peer P-256
private material is copied into locked process memory, validated against its
public key, zeroed on teardown, and never logged.

The default developer backend is libjuice and qualifies direct UDP and
TURN/UDP only. A `turns:` URI or `transport=tcp` is not production-qualified
unless the Goal 07 libnice build and its network-lab tests are selected.
