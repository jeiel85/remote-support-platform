# Signaling Protocol

## 1. WebSocket envelope

```json
{
  "protocolVersion": 1,
  "messageId": "uuidv7",
  "sessionId": "uuidv7",
  "peerId": "uuidv7",
  "epoch": 1,
  "sequence": 42,
  "sentAt": "2026-07-01T00:00:00Z",
  "type": "ICE_CANDIDATE",
  "payload": {}
}
```

## 2. Message types

| Type | Direction | Notes |
|---|---|---|
| `HELLO` | peer → server | protocol, app version, capabilities |
| `HELLO_ACK` | server → peer | accepted version, heartbeat interval |
| `SESSION_AUTHORIZED` | server → peer | peer metadata and scoped token confirmation |
| `SDP_OFFER` | peer → peer | bounded SDP, authenticated relay |
| `SDP_ANSWER` | peer → peer | bounded SDP |
| `ICE_CANDIDATE` | peer → peer | candidate validation and rate limits |
| `ICE_COMPLETE` | peer → peer | candidate gathering complete |
| `PEER_CAPABILITIES` | peer → peer | codec, monitor, input and feature matrix |
| `POLICY_UPDATE` | server → peer | immediate scope revocation or termination |
| `RECONNECT_REQUEST` | peer → server | reconnect grant and next epoch |
| `SESSION_END` | peer/server → peers | reason code, final state |
| `PING/PONG` | both | liveness only, no authorization extension |

## 3. Validation

- Reject unknown mandatory fields and unsupported major versions.
- Limit SDP size and ICE candidate count/rate.
- Parse candidate fields; never pass arbitrary strings into shell or logs.
- Verify sender belongs to session and current epoch.
- Ensure only expected role can send offer in the current negotiation step.
- Never log access tokens or raw candidate credentials.

## 4. Authentication binding

At WebSocket connect:

1. TLS server identity is validated.
2. Peer presents short-lived authorization token.
3. Peer proves possession of host/device or operator-session key.
4. Server binds connection to `session_id`, `peer_id`, role, scopes and epoch.
5. Every relayed message is stamped with server-observed identity.

## 5. Reconnect

- WebSocket reconnect does not imply media renegotiation.
- If media remains active, only signaling state is restored.
- If peer connection fails, a new transport epoch is created.
- Pending ICE/SDP from prior epoch is discarded.

## Signaling-to-DTLS binding

SDP and ICE signaling alone do not authorize the peer. Once DTLS is established, the peers exchange signed transport bindings over the reliable control channel. The binding covers both observed DTLS fingerprints, session and peer IDs, role, transport epoch, permission revision, scopes, and authorization-context hash. The server never instructs a client to skip this check.
