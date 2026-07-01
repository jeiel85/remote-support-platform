# Session State Machine

The API and database use the following closed state set:

`CREATED`, `WAITING_FOR_OPERATOR`, `CONSENT_PENDING`, `HOST_PENDING`, `AUTHORIZED`, `NEGOTIATING`, `CONNECTED`, `RECONNECTING`, `EXPIRED`, `REJECTED`, `FAILED`, `CANCELLED`, `ENDED`.

Every transition uses optimistic concurrency (`stateVersion`/`If-Match`), emits a session event and audit event in the same transaction, and is idempotent by command identifier.

## Attended

```text
CREATED -> WAITING_FOR_OPERATOR -> CONSENT_PENDING -> AUTHORIZED
AUTHORIZED -> NEGOTIATING -> CONNECTED
CONNECTED -> RECONNECTING -> NEGOTIATING | ENDED
WAITING_FOR_OPERATOR | CONSENT_PENDING -> EXPIRED | CANCELLED
CONSENT_PENDING -> REJECTED
any nonterminal -> FAILED | CANCELLED
```

The host creates the session and receives only a host bootstrap credential. The operator resolves the support code and receives an operator bootstrap credential. Approval does not directly authorize media: each peer separately proves its ephemeral key, exchanges its bootstrap credential for a scoped peer credential, obtains signaling/TURN credentials, and completes transport binding.

## Managed attended

```text
CREATED -> HOST_PENDING -> CONSENT_PENDING -> AUTHORIZED -> NEGOTIATING -> CONNECTED
HOST_PENDING -> EXPIRED | FAILED | CANCELLED
CONSENT_PENDING -> REJECTED | EXPIRED
```

`HOST_PENDING` means the policy-authorized request is queued for an enrolled device but no host peer is bound. The installed host acknowledges delivery, presents local consent when required, generates a fresh host peer key, and signs the decision with the active device key.

## Unattended

```text
CREATED -> HOST_PENDING -> AUTHORIZED -> NEGOTIATING -> CONNECTED
HOST_PENDING -> EXPIRED | FAILED | CANCELLED
```

Unattended authorization requires the separately released policy profile, operator MFA evidence, enrolled-device status, active device key, `UNATTENDED_SESSION` scope, local configuration/deployment authorization, and configured notification behavior. It never bypasses peer authorization or transport binding.

## Global invariants

- Terminal states are immutable except metadata-only post-processing.
- Granted scopes are a subset of requested scopes and may only shrink during a session.
- Increasing `transportEpoch` invalidates old signaling tickets, reconnect grants, transport bindings, and reliable-input windows.
- Content and control channels remain disabled until both transport bindings are verified.
- Expiry is evaluated by the server, not trusted client clocks.
- A device authorization-version change invalidates pending managed sessions and device credentials issued under an older version.
