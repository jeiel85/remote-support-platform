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

## Shutdown

- stop accepting new sockets;
- notify clients to reconnect;
- drain for a bounded period;
- close remaining sockets with retryable reason;
- preserve active peer media when signaling reconnects elsewhere.
