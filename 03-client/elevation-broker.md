# Elevation and Privileged Broker Design

## 1. Purpose

Installed mode may require operations unavailable to a medium-integrity user process. The privileged boundary exists to perform a small set of product operations, not to expose a remote shell or generic process launcher.

## 2. Process model

- `RemoteSupport.Service.exe`: LocalSystem, Session 0, no interactive UI.
- `RemoteSupport.Agent.exe`: interactive user's token and desktop.
- `RemoteSupport.PrivilegedAgent.exe`: service-launched, restricted command surface, target interactive session, visible-session only.
- `RemoteSupport.Updater.exe`: separate service-controlled update path; not callable through session IPC.

The privileged agent is optional and starts only for a locally approved installed session or an explicitly authorized managed policy. It terminates at session end or idle deadline.

## 3. IPC peer authentication

Pipe ACL alone is insufficient because another process running as the same user may connect. The service:

1. creates a 256-bit launch nonce;
2. launches the signed child from the protected installation path;
3. passes the nonce through an inherited handle, not command line or a persistent file;
4. retrieves and validates the connecting process ID from the named-pipe connection;
5. verifies executable path, file identity, publisher signature, expected hash/version policy, session ID, token user/SID, and process ancestry;
6. completes nonce challenge-response;
7. creates an in-memory broker capability bound to process ID, Windows session ID, product session ID, allowed commands, and expiry.

The Agent validates the pipe server process as the installed service and verifies the service challenge before sending any request.

## 4. Command allowlist

| Command | Caller | Required product scope/policy | Additional validation |
|---|---|---|---|
| Query capabilities | Agent | authenticated IPC | none |
| Start/stop privileged input context | Agent | input scopes + local consent/managed policy | current session/desktop, timeout |
| Inject bounded input batch | Privileged Agent | active context | sequence, topology generation, foreground desktop, rate limit |
| Request reboot | Agent | `REQUEST_REBOOT` | local decision/policy, reconnect grant |
| Store reboot grant | Agent | `RECONNECT_AFTER_REBOOT` | encrypted, same session/device/operator |
| Consume reboot grant | launched Agent | service launch binding | single-use and expiry |
| Report installed health/version | Agent | device enrollment | schema and size limits |

Forbidden commands include arbitrary executable launch, command line execution, DLL loading, registry writes, file copies to arbitrary locations, credential entry, security-policy changes, and UAC-policy modification.

## 5. Input safeguards

- The broker accepts only typed `PrivilegedInputEvent` messages from `service_ipc.proto`; opaque encoded command blobs are prohibited.
- One batch contains at most 256 events and 64 KiB serialized data, with strictly increasing input sequence numbers and a bounded events-per-second policy.
- It independently rechecks current permission revision, transport epoch, granted input scopes, expiry, peer binding, and session authorization proof.
- It rejects stale display topology generations.
- It rate-limits event batches and releases all pressed state on timeout/disconnect.
- It operates only on an expected interactive desktop and reports secure-desktop transitions.
- It never accepts text interpreted as a command or script.

## 6. Service hardening

- Install under Program Files with non-admin users denied write access.
- Use a restricted service SID and explicit resource ACLs.
- Remove unneeded service privileges after startup where possible.
- Disable interactive service behavior.
- Configure bounded failure recovery, not endless restart loops.
- Validate child image before every launch, including upgrades.
- Deny junction/reparse-point replacement in update and launch paths.
- Use protected temporary directories owned by the service.
- Audit command category and result, never raw input text or key data.

## 7. Secure desktop and logon screen

Initial attended GA does not claim remote secure-desktop or pre-logon control. When Windows switches away from the supported interactive desktop:

- capture reports a capability transition;
- remote input is stopped;
- pressed remote state is released;
- the operator sees a local-action-required state;
- stale frames are visually marked or blanked.

Any future secure-desktop module requires a separate ADR, threat model, signed module, compatibility evidence, and release gate.
