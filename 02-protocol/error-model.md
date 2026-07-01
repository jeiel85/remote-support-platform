# Error Model

## 1. Error response

```json
{
  "error": {
    "code": "SESSION_EXPIRED",
    "message": "The support session has expired.",
    "correlationId": "uuidv7",
    "retryable": false,
    "details": {}
  }
}
```

## 2. Categories

- `AUTH_*`: authentication or token validation.
- `AUTHZ_*`: role, tenant, scope or policy denial.
- `SESSION_*`: lifecycle and state conflicts.
- `SIGNAL_*`: signaling validation or sequence errors.
- `TRANSPORT_*`: ICE, TURN, DTLS or DataChannel failures.
- `CAPTURE_*`: display/capture failure.
- `ENCODER_*`: codec or GPU failure.
- `INPUT_*`: permission, mapping or secure desktop limitation.
- `FILE_*`: file validation, disk, hash or policy failure.
- `UPDATE_*`: manifest, signature, hash or rollout failure.
- `RATE_*`: abuse and quota controls.
- `INTERNAL_*`: unexpected server/client failure.

## 3. Rules

- User-facing messages do not expose stack traces or secrets.
- Logs use stable code and correlation ID.
- Retryable errors include bounded retry guidance internally, not indefinite loops.
- Security denials avoid revealing whether a tenant/device/account exists.
- Native error codes are translated at module boundary and retained only in restricted diagnostics.
