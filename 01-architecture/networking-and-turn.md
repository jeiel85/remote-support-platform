# Networking and NAT Traversal

## 1. Transport decision

Use native WebRTC for:

- ICE candidate gathering and route selection;
- DTLS-SRTP media protection;
- SCTP data channels;
- congestion feedback and packet-loss handling;
- UDP-first operation with TURN/TCP/TLS fallback.

The native dependency is pinned to a tested commit in `deps.lock`; do not track an unpinned branch.

## 2. Channels

| Channel | Transport | Reliability | Purpose |
|---|---|---|---|
| Video | RTP/SRTP | lossy, congestion-controlled | screen frames |
| Cursor/input | DataChannel | ordered or sequenced, low-latency | pointer/keyboard/control |
| Session control | DataChannel | reliable ordered | permissions, monitor state, heartbeat |
| Clipboard | DataChannel | reliable ordered | scoped clipboard payload |
| File control | DataChannel | reliable ordered | manifest, consent, resume |
| File chunks | DataChannel | reliable, chunked | file bytes with bounded flow control |
| Chat | DataChannel | reliable ordered | session chat |

Input move events may be coalesced; button/key transitions must not be silently dropped.

## 3. Signaling

- HTTPS/WebSocket over TLS.
- Authenticated operator and signed host session identity.
- Server authorizes message relay but does not terminate peer encryption.
- SDP and ICE payload sizes are bounded and validated.
- Duplicate/replayed messages are rejected using session sequence numbers.
- Signaling logs metadata only; raw SDP is diagnostic-sensitive and disabled in normal logs.

## 4. TURN

- coturn deployed as independent regional nodes.
- Current production baseline at bundle creation: coturn 4.14.0, pinned by
  immutable image digest and release commit; promotion still applies the CVE
  and provenance gate.
- Use short-lived REST-derived credentials or equivalent ephemeral credentials.
- Disable anonymous access and web admin unless operationally required and isolated.
- Deny relay to private/control-plane/database networks.
- Restrict relay port range and open only required firewall rules.
- Export metrics over protected management network.
- Apply per-tenant/session quotas and source rate limits.

## 5. Port plan

Typical defaults, configurable by environment:

- `443/TCP`: API, WebSocket signaling, update metadata.
- `3478/UDP,TCP`: TURN/STUN.
- `5349/TCP`: TURN over TLS.
- relay UDP range: restricted dedicated range, e.g. `49152-65535`, sized to capacity.

Corporate environments may require TURN/TLS on 443 through a dedicated public endpoint. Do not multiplex it casually with the control API without tested proxy support.

## 6. Route policy

1. Gather host and server-reflexive candidates.
2. Attempt direct UDP.
3. Attempt TURN UDP.
4. Attempt TURN TCP/TLS for restrictive networks.
5. Surface route and quality state to diagnostics.

## 7. TURN capacity model

Approximate relay egress per session:

```text
relay_egress_bps ≈ video_bps + data_bps + protocol_overhead
```

A relay node carrying `N` sessions requires both inbound and outbound packet processing. Capacity planning must use measured packets per second, egress, CPU and NIC saturation, not Mbps alone.

Example planning input, not a promise:

- average relayed video: 2.5 Mbps;
- overhead/data allowance: 20%;
- 500 concurrent relayed sessions;
- one direction egress: about 1.5 Gbps;
- provision substantial headroom and validate with real packet sizes.

## 8. Abuse controls

- session-bound credentials;
- allocation quotas by tenant and source;
- no open STUN/TURN admin interfaces;
- reflection/amplification monitoring;
- automatic credential revocation on session end;
- anomaly detection for excessive allocation churn or bandwidth.
