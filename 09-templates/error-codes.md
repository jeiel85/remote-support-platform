# Stable Error Code Catalog

Stable codes are protocol/API contracts. User-facing text is localized separately. New codes are additive within a major protocol version; removed codes remain reserved.

| Code | Retryable | Meaning |
|---|---:|---|
| `AUTH_REQUIRED` | No | operator, peer, device, or bootstrap authentication missing |
| `AUTH_TOKEN_EXPIRED` | Yes | refresh or re-authentication required |
| `AUTH_TOKEN_REVOKED` | No | credential or authorization version revoked |
| `AUTH_PROOF_NONCE_EXPIRED` | Yes | proof-of-possession nonce expired |
| `AUTH_PROOF_INVALID` | No | public-key proof failed |
| `AUTH_SIGNALING_TICKET_INVALID` | No | signaling ticket invalid, used, expired, or mismatched |
| `AUTHZ_SCOPE_DENIED` | No | requested capability not granted |
| `AUTHZ_MFA_REQUIRED` | Yes | step-up authentication needed |
| `AUTHZ_TENANT_DENIED` | No | actor is not authorized in tenant context |
| `AUTHZ_POLICY_DENIED` | No | active policy denied the action |
| `AUTHZ_DEVICE_REVOKED` | No | device or current device key revoked |
| `SESSION_CODE_INVALID_OR_EXPIRED` | No | generic code resolution failure |
| `SESSION_BOOTSTRAP_EXPIRED` | No | host/operator bootstrap credential expired |
| `SESSION_STATE_CONFLICT` | Yes | stale state version or concurrent transition |
| `SESSION_PERMISSION_REVISION_STALE` | Yes | peer used an older permission revision |
| `PERMISSION_STATE_INVALID` | No | permission update was not the next host-originated scope reduction |
| `SESSION_EPOCH_STALE` | Yes | signaling/control message belongs to prior epoch |
| `SESSION_EXPIRED` | No | authorization expired |
| `SESSION_REVOKED` | No | policy/account/device/session revoked |
| `SESSION_RECONNECT_DENIED` | No | reconnect cannot revive current authorization |
| `SIGNAL_SEQUENCE_INVALID` | No | replay or invalid ordering |
| `SIGNAL_MESSAGE_TOO_LARGE` | No | signaling payload exceeded limit |
| `SIGNAL_PROTOCOL_INVALID` | No | signaling/control frame failed contract validation |
| `TRANSPORT_ICE_FAILED` | Yes | no viable candidate pair |
| `TRANSPORT_TURN_AUTH_FAILED` | Yes | relay credential, clock, or configuration issue |
| `TRANSPORT_DTLS_IDENTITY_MISMATCH` | No | media peer fingerprint/key binding failed |
| `TRANSPORT_CHANNEL_NEGOTIATION_FAILED` | Yes | required data channel incompatible or unavailable |
| `CAPTURE_ACCESS_LOST` | Yes | display reset; capture recreation required |
| `CAPTURE_TARGET_REMOVED` | Yes | selected monitor disappeared |
| `CAPTURE_SECURE_DESKTOP_UNAVAILABLE` | No | current desktop is outside supported capture boundary |
| `CAPTURE_UNSUPPORTED` | No | no supported capture path |
| `ENCODER_HARDWARE_FAILED` | Yes | retry or fall back to software encoder |
| `ENCODER_CONFIGURATION_UNSUPPORTED` | Yes | renegotiate profile, scale, or frame rate |
| `INPUT_PERMISSION_REVOKED` | No | host/server revoked input scope |
| `INPUT_SECURE_DESKTOP_LOCAL_ACTION_REQUIRED` | No | local action needed |
| `INPUT_TOPOLOGY_STALE` | Yes | refresh display topology |
| `INPUT_SEQUENCE_INVALID` | Yes | reliable input gap/duplicate required reset |
| `CLIPBOARD_POLICY_BLOCKED` | No | clipboard direction/content/size denied |
| `CLIPBOARD_CONTENT_MISMATCH` | No | clipboard offer/hash did not match content |
| `FILE_POLICY_BLOCKED` | No | type, size, direction, or tenant policy denial |
| `FILE_PATH_INVALID` | No | unsafe or unsupported filename/path |
| `FILE_DISK_SPACE_INSUFFICIENT` | Yes | destination lacks required free space |
| `FILE_HASH_MISMATCH` | Yes | corrupted or incomplete transfer |
| `FILE_TRANSFER_CANCELLED` | No | sender, receiver, or policy cancelled transfer |
| `FILE_PERMISSION_REVOKED` | No | active transfer stopped because its directional scope was revoked |
| `DEVICE_ENROLLMENT_INVALID_OR_EXPIRED` | No | generic enrollment-token failure |
| `DEVICE_KEY_ROTATION_REQUIRED` | Yes | device must rotate or refresh key material |
| `DEVICE_MINIMUM_VERSION_REQUIRED` | Yes | managed client is below enforced version |
| `POLICY_DOCUMENT_INVALID` | No | policy schema or semantic validation failed |
| `POLICY_VERSION_CONFLICT` | Yes | stale policy resource/version |
| `IPC_PEER_NOT_TRUSTED` | No | local process/signature/path/session verification failed |
| `IPC_CHALLENGE_FAILED` | No | launch-nonce challenge failed |
| `IPC_CAPABILITY_EXPIRED` | No | broker capability expired or mismatched |
| `IPC_COMMAND_NOT_ALLOWED` | No | command is not on the privileged allowlist |
| `IPC_MESSAGE_TOO_LARGE` | No | local message exceeded hard limit |
| `UPDATE_METADATA_EXPIRED` | Yes | signed update metadata expired |
| `UPDATE_SIGNATURE_INVALID` | No | security failure; stop update |
| `UPDATE_HASH_MISMATCH` | No | downloaded artifact hash mismatch |
| `UPDATE_PUBLISHER_MISMATCH` | No | Authenticode publisher not trusted for product |
| `UPDATE_ROLLBACK_BLOCKED` | No | lower release sequence rejected |
| `UPDATE_HEALTH_CHECK_FAILED` | Yes | new version failed post-install health check |
| `RATE_LIMITED` | Yes | bounded retry after policy delay |
| `ABUSE_ACCOUNT_SUSPENDED` | No | account or tenant blocked by enforcement |
| `INTERNAL_ERROR` | Maybe | unexpected failure with correlation ID |

## Final-audit additions

| Code | Retryable | Meaning |
|---|---:|---|
| `AUTH_DPOP_REQUIRED` | No | a peer/device access token was presented without the required DPoP proof |
| `AUTH_DPOP_INVALID` | No | DPoP method, URI, token hash, key binding, time or nonce validation failed |
| `AUTH_DPOP_REPLAYED` | No | a DPoP proof `jti` was already accepted for the proof key |
| `AUTH_CHALLENGE_PURPOSE_MISMATCH` | No | challenge purpose/canonicalization version does not match the operation |
| `AUTH_PEER_CHALLENGE_EXPIRED` | Yes | peer-authorization challenge expired before exchange |
| `AUTH_PEER_CHALLENGE_REPLAYED` | No | peer-authorization challenge was already consumed |
| `TENANT_CONTEXT_REQUIRED` | No | tenant-scoped operator request omitted or malformed `X-Tenant-Id` |
| `MEMBERSHIP_VERSION_CONFLICT` | Yes | membership privilege version changed since the caller read it |
| `INVITATION_INVALID_OR_EXPIRED` | No | invitation token is invalid, expired, revoked, already accepted or tenant-incompatible |
| `SESSION_HOST_PENDING` | Yes | managed session is waiting for the enrolled host to receive or decide it |
| `DEVICE_COMMAND_EXPIRED` | No | managed-session request expired before host acknowledgement |
| `DEVICE_OFFLINE` | Yes | no current device channel or heartbeat satisfies policy |
| `TRANSPORT_BINDING_REQUIRED` | Yes | content/control attempted before reciprocal binding completed |
| `TRANSPORT_BINDING_INVALID` | No | signed binding, epoch, scopes, authorization context or DTLS fingerprints failed validation |
| `TENANT_EXPORT_NOT_READY` | Yes | requested export has not reached the ready state |
| `TENANT_EXPORT_EXPIRED` | No | export download authorization or artifact retention expired |
| `TENANT_CLOSURE_STATE_CONFLICT` | Yes | closure workflow state/version does not permit the requested transition |
| `UPDATE_ROOT_INVALID` | No | root threshold, old/new rotation proof, version or expiry validation failed |
| `UPDATE_ROOT_SEQUENCE_REQUIRED` | Yes | client must fetch and validate the next sequential root version |
