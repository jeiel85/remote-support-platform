# Managed Host Command Channel

## Baseline transport

The installed Windows Service authenticates with a short-lived device credential obtained through active-key proof. It performs a bounded 20-second HTTPS long poll to `/devices/{deviceId}/pending-session-requests`; reconnect uses exponential backoff with jitter and a 60-second ceiling. This baseline avoids an undeclared always-on proprietary socket. A future push channel may be introduced behind the same command semantics and an ADR.

## Delivery and acknowledgement

1. An authenticated operator creates a managed session. Policy evaluation persists an immutable decision snapshot and the session enters `HOST_PENDING`.
2. The API commits the session, host-delivery outbox item, and audit event atomically.
3. The device poll returns only requests matching its tenant, device ID, authorization version, active status, and unexpired policy decision.
4. The Service launches or contacts the interactive Agent in the active user session when local consent/notification is required.
5. The Service or Agent displays operator identity and scopes, generates a fresh host ephemeral peer key, and signs `ManagedHostDecision` with the active device key.
6. The API atomically consumes the consent nonce, binds the host participant/key, records the decision, and returns a single-use host bootstrap credential only for approval.
7. Both peers exchange bootstrap credentials for peer authorization and complete mandatory signed WebRTC transport binding.

Polling is at-least-once. `sessionId`, `stateVersion`, and the signed nonce make host decisions idempotent. A device must not launch two host peers for the same session/epoch. Revocation increments the device authorization version; older credentials, pending requests, and poll cursors are rejected.

## Credential lifecycle

The device requests a credential challenge, signs the canonical challenge using its active device key, and exchanges it for a short-lived device credential. Renewal begins before expiry and continues using bounded backoff. Key rotation proves the new key under the current key, then proves possession of the new key before the old key is retired. If the old key is suspected compromised, administrators revoke the device and require re-enrollment instead of rotation.

## Failure behavior

Offline devices cause the operator request to remain `HOST_PENDING` only until the configured deadline. Expiry transitions the session to `EXPIRED`; policy denial transitions it to `FAILED` with a non-sensitive reason. The operator receives delivery state, but no information that would reveal another tenant's device existence. Service restart resumes polling and reconciles pending sessions by server state rather than local memory alone.

## DPoP on device operations

Heartbeat, polling, managed-host decisions and key rotation present the short-lived device access token as a DPoP token with a per-request proof from the active device key. Reverse proxies must preserve the externally visible method and URI inputs needed for verification.
