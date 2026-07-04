# Goal 14 evidence

Date: 2026-07-04. Local validation environment: Windows x64, .NET SDK
10.0.301, ASP.NET Core 10.0.9, pinned Python environment, in-memory
deterministic test persistence and production PostgreSQL repository paths.
This goal builds entirely on Goal 13's device credential lifecycle and
managed-session/peer-authorization machinery; no new session or transport
primitive was introduced.

## Implemented deliverables

- a dedicated unattended threat model (`05-security/unattended-threat-model.md`)
  covering what changes when no local human witnesses a session, and mapping
  each removed human control to its technical replacement;
- explicit two-party unattended enrollment: a tenant OWNER/ADMIN with fresh
  MFA creates a short-lived, single-use confirmation code
  (`POST /v1/devices/{id}/unattended-enrollment-requests`), and only the
  enrolled device's own DPoP-authenticated key can consume it
  (`POST /v1/devices/{id}/unattended-enrollment-confirmations`) to set
  `unattendedEnabled`; neither the tenant admin nor a normal attended session
  can complete this alone, and `DeviceContract.UnattendedEnabled` now
  reflects the real per-device flag instead of a placeholder;
- three independent, unconditional gates on every `UNATTENDED`
  policy evaluation, none of which a policy document alone can satisfy:
  the target device's `unattendedEnabled` flag, the tenant-level
  `UNATTENDED_ACCESS` feature flag (absent from the default tenant feature
  list, so unattended is off unless explicitly turned on), and fresh
  step-up MFA on the requesting operator, independent of any policy rule's
  own MFA configuration;
- `POST /devices/{id}/sessions` and the managed-host-decision endpoint now
  accept `UNATTENDED` alongside `MANAGED_ATTENDED`, requiring the
  `UNATTENDED_SESSION` scope be both requested and policy-granted, matching
  the reference schema's session-scope invariant;
- device revocation (`DELETE /v1/devices/{id}` and the new
  `DELETE /v1/devices/{id}/unattended-enrollment`) now proactively cancels
  every non-terminal session bound to that device
  (`AttendedSessionService.TerminateSessionsForDevice`), not just future
  credential/session issuance;
- the Managed Host orchestrator auto-approves an `UNATTENDED` pending
  session (already fully gated server-side) without waiting on or gating on
  the interactive Agent, while still attempting a best-effort local
  notification and logging when one cannot be delivered;
- structural confirmation that the portable Agent build has no path to the
  unattended capability: `RemoteSupport.Agent.App` has no project reference
  to `RemoteSupport.ManagedHost.Service`.

## Automated evidence

`UnattendedAccessApiTests` (3 new integration tests): the first proves
session creation is denied when any one of device opt-in, the
`UNATTENDED_ACCESS` feature flag, or fresh MFA is missing, and succeeds only
once all three hold; the second proves enrollment confirmation with a valid
code but the wrong device key is rejected while the genuine device succeeds;
the third proves revoking a device with an AUTHORIZED unattended session
immediately cancels it and clears its granted scopes.
`ManagedHostOrchestratorTests` adds a case proving an unattended pending
session is approved and submitted with its full scope set even when the
local notification path is denied or unreachable.

```powershell
./build.ps1 -Target ValidateDesign
./build.ps1 -Target Test -Configuration Release
./build.ps1 -Target IntegrationTest -Configuration Release
python tools/operations/verify_goal14.py .
```

`build.ps1 IntegrationTest`/`CI` now also run `verify_goal14.py`.

## Production boundary

This evidence completes the unattended-specific control-plane protocol,
policy gates, and orchestrator behavior with real cryptographic
verification (forged confirmation attempts are actually rejected by
signature, not by convention). It does not claim the deliverables Goal 14
lists that require organizations, not code: a dedicated third-party
penetration test and social-abuse review of the unattended capability
specifically, and piloting with approved tenants before general
availability, remain outstanding exactly as `05-security/unattended-threat-model.md`
"Production boundary" and `07-delivery/goals/goal-14-unattended.md` require.
Device groups are not implemented as a distinct access-control dimension in
this pass; per-device-ID and all-devices policy targeting already provide
the equivalent scoping, and dedicated device-group CRUD/membership and an
Admin Portal unattended-management screen remain deferred, same as Goal 13's
Admin Portal follow-up. The local notification path relies on
`WtsInteractiveAgentLauncher.EnsureAgentRunning`, which Goal 13's evidence
already documents as requiring a Windows Session-0 lab to exercise for real.
