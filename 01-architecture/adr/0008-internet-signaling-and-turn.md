# ADR 0008: Internet signaling and regional TURN boundary

- Status: accepted
- Date: 2026-07-02

## Decision

Peer REST calls use RFC 9449 DPoP with ES256, exact method/URI/token-hash
binding, a two-minute proof window, and persisted hashed `jti` replay state.
Peer tokens, current session state, role, key thumbprint, scopes, permission
revision, and transport epoch must all agree.

The API issues a signaling ticket with an opaque server-keyed session shard and
a random 256-bit authenticator, valid for at most 60 seconds. Only its lookup
hash is stored. The WebSocket upgrade consumes it atomically
and binds the socket to one peer. Signaling accepts strict JSON text envelopes,
persists monotonic peer sequence, validates role/epoch, parses bounded SDP and
ICE, and never persists or logs their payloads. Replacing or losing signaling
does not mutate the independent WebRTC media transport or epoch.

TURN uses coturn's time-limited REST credential mechanism. The username is an
expiry plus an opaque server-derived peer reference; the password is the
coturn-compatible HMAC-SHA1 value. SHA-1 is used only because this credential
protocol requires it, over a random server-held secret, and is not a content
integrity or general signature choice. The shared secret never leaves the API
and TURN secret stores.

coturn 4.14.0 is pinned by release commit and immutable image digest. Peer
destinations, management access, ports, lifetime, bandwidth, allocation count,
and TLS versions are bounded. Redis final-allocation events are converted by a
durable collector into HMAC-authenticated usage reports. Stored dimensions use
tenant, session, bounded node/region/transport, and opaque credential IDs.

The native developer build uses libjuice for TURN/UDP. Builds that advertise
TURN/TCP or TURN/TLS select libnice explicitly; route probes use the ABI's
relay-only flag and still require DTLS and reciprocal transport binding.

## Consequences

- DPoP replay state and signaling sequence participate in the same serialized
  attended aggregate as authorization state in the current modular monolith.
- Signaling worker ownership is ephemeral; the edge consistently hashes the
  opaque shard so both peers land on one worker without revealing session ID.
- Raw SDP, candidates, TURN passwords, and customer content are excluded from
  audit, metering, and logs.
- Physical NAT, firewall, quota, TLS scanner, and relay capacity evidence is a
  deployment-lab promotion gate and cannot be replaced by HTTP unit tests.

## Primary references

- RFC 9449: <https://www.rfc-editor.org/rfc/rfc9449>
- coturn 4.14.0 source: <https://github.com/coturn/coturn/tree/4.14.0>
- libdatachannel transport notes:
  <https://github.com/paullouisageneau/libdatachannel/blob/v0.24.3/DOC.md>
