# Goal 13 evidence

Date: 2026-07-04. Local validation environment: Windows x64, .NET SDK
10.0.301, ASP.NET Core 10.0.9, pinned Python environment, in-memory
deterministic test persistence and production PostgreSQL repository paths.

## Implemented deliverables

- device credential lifecycle endpoints (`POST /v1/devices/{id}/credential-challenges`,
  `/credentials`, `/keys/rotate`, `/heartbeat`) built on the existing Goal 10
  device-key model, extended with per-version `DeviceKeyRecord`s, a
  cross-tenant `governance_device_lookups` table so anonymous device calls can
  locate their tenant before authentication, and RFC 9449 DPoP device access
  tokens sharing the peer-token DPoP proof validator (`DpopProof.cs`, factored
  out of `PeerAccessService` and reused unchanged by `DeviceAccessService`);
- key rotation that requires the current key to sign a challenge naming the
  new key's thumbprint before the new key becomes active, immediately
  retiring the old key and forcing a fresh `CREDENTIAL_REFRESH` exchange with
  the new key before any further device access;
- managed-session creation (`POST /v1/devices/{id}/sessions`) that evaluates
  the existing Goal 10 policy engine first, rejects scopes the policy does
  not grant, records the policy decision hash on the session, and enters
  `HOST_PENDING` with no host peer bound yet (`SessionAggregate.Host` and
  `CodeHash` are now nullable to represent this pre-attended-flow-compatible
  state, migration `0003_managed_host.sql` extends `attended_session_aggregates`
  accordingly);
- authenticated bounded long-poll delivery (`GET /pending-session-requests`,
  capped at 20 seconds) returning a deterministic per-session consent nonce
  (`crypto.DeriveSecret`, no extra storage) and the policy decision hash;
- device-signed `POST /sessions/{id}/managed-host-decision` that verifies the
  decision proof against the device's active key before binding a fresh host
  ephemeral peer key, then reuses the existing attended peer-authorization,
  signaling and TURN machinery unchanged;
- an extended `service_ipc.proto` with `ManagedSessionConsentRequest`/
  `ManagedSessionConsentResult` so the Service can hand the local
  consent/notification workflow to the interactive Agent without ever
  rendering UI itself;
- `RemoteSupport.Ipc`: length-prefixed Protobuf framing with an enforced
  message-size bound, a mutual HMAC challenge-response handshake over the
  one-time launch secret, and Windows pipe ACL/OS-verified caller
  process/session identity helpers;
- `RemoteSupport.ManagedHost.Service`: non-exportable CNG P-256 device
  identity key, the client-side mirror of the server's canonical proof-byte
  layout, a credential/heartbeat/poll/decide HTTP client, an orchestrator
  that generates and discards a fresh ephemeral host key per decision, and a
  DPAPI-protected (`ProtectedData`, `LocalMachine` scope) reboot-grant store
  that enforces its own expiry.

## Automated evidence

`ManagedHostApiTests` (2 new integration tests) drives the full
enrollment -> credential exchange -> heartbeat -> policy-gated session
creation -> long-poll -> signed host decision -> peer authorization path
against a real in-memory server, and separately proves key rotation
invalidates the old key's token immediately while device revocation blocks
the new key's poll. `IpcHandshakeTests` (4 tests) exercises the framing and
mutual handshake over a real Windows named pipe, including a message that is
correctly rejected for exceeding the configured bound. `RemoteSupport.ManagedHost.Service.Tests`
(9 tests) covers non-exportable CNG key creation/signing, ephemeral host-key
independence, DPAPI reboot-grant round-trip and expiry, and the orchestrator's
approve/deny/timeout-as-deny paths against a fake server that independently
verifies every submitted P-256 signature.

```powershell
./build.ps1 -Target ValidateDesign
./build.ps1 -Target Test -Configuration Release
./build.ps1 -Target IntegrationTest -Configuration Release
python tools/operations/verify_goal13.py .
```

`build.ps1 IntegrationTest`/`CI` now also run `verify_goal13.py`.

## Production boundary

This evidence completes the control-plane protocol, the credential/session
security properties, and the Service-side client library with real
cryptographic and IPC tests. It does not claim a finished Windows Service
deployment. `WtsInteractiveAgentLauncher` implements session enumeration and
the secure named-pipe consent channel, but `EnsureAgentRunning` (the
`WTSQueryUserToken` -> `CreateProcessAsUserW` launch of the Agent under the
target user token) is documented rather than executed, because creating or
manipulating another logon session's process is not something this session
performs against a live machine without a dedicated Windows Session-0 lab;
this is the same class of boundary already recorded for Goals 01-11 (see
`FINAL_AUDIT_REPORT.md` "Remaining empirical proof"). The Managed Host
MSI-equivalent installer, service-recovery policy configuration, and new
Admin Portal enrollment/revocation screens are not built in this pass; Goal
10's existing device inventory, revocation and audit-event views already
surface every new audit action this goal adds
(`DEVICE_KEY_ROTATED`, `MANAGED_SESSION_CREATED`, `MANAGED_HOST_APPROVED`,
`MANAGED_HOST_REJECTED`) through the unchanged audit API, but dedicated
managed-host screens remain a follow-up. Goal 12's release-gate approval
record (`07-delivery/release-gate-approval.md`) and the pending external
security review over the Service/IPC/device-key surface, required by this
goal's exit criteria, remain outstanding until that review is scheduled and
completed.
