# Signaling Service

## Responsibilities

- authenticate WebSocket peers;
- bind connection to session/peer/epoch;
- relay validated SDP and ICE messages;
- enforce state, role, sequence and rate rules;
- publish presence/connection metadata;
- push policy revocation and session end;
- support bounded reconnect.

## Non-responsibilities

- no media proxy;
- no SDP rewriting except strictly required sanitization/limits;
- no storage of long-lived candidate or media details;
- no authorization based only on a support code.

## Scale model

- sticky routing is optional if connection ownership is tracked through a backplane.
- Initial scale can use in-memory connection registry per node plus Valkey/pub-sub or a lightweight broker for cross-node delivery.
- Presence is ephemeral and not the source of truth.
- Session state remains in PostgreSQL/cache with version checks.

## Backpressure

- bounded outbound queue per socket;
- disconnect slow or abusive clients;
- per-message and per-second limits;
- ICE candidate burst allowance followed by lower sustained rate;
- maximum concurrent pending/active connections per tenant/account/source.

Implemented baseline limits are 64 KiB per text envelope, 32 KiB per SDP, 2
KiB per candidate, 256 candidates per connection, 30 candidates per 10
seconds, and 120 total messages per minute. Each socket must start with `HELLO`.
Peer sequence is strictly contiguous and persists across signaling reconnects;
reconnect therefore reconciles signaling state without changing a healthy
WebRTC epoch. A five-second bounded socket send supplies backpressure.

Authentication uses a 60-second, 256-bit, one-time ticket whose stored record
binds session, peer, role, key thumbprint, scopes, permission revision, epoch,
and protocol. Ticket issuance requires a current RFC 9449 DPoP peer request.
The ticket and all SDP/ICE values are excluded from logs and storage.

## Shutdown

- stop accepting new sockets;
- notify clients to reconnect;
- drain for a bounded period;
- close remaining sockets with retryable reason;
- preserve active peer media when signaling reconnects elsewhere.
