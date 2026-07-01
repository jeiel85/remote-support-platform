# Windows Service

## Service responsibilities

- maintain installation identity and device key access;
- enroll and refresh device authorization;
- supervise per-user Agent processes;
- coordinate reboot reconnect;
- apply device policy;
- broker allowlisted privileged actions;
- stage and apply signed updates;
- expose health and audit events.

## Non-responsibilities

- no normal UI;
- no direct display of consent dialogs;
- no generic process launcher;
- no arbitrary remote command execution;
- no direct storage of operator passwords.

## Service state

```text
installationId
deviceId
deviceKeyReference
currentPolicyVersion
releaseChannel
agentProcessesBySession
pendingRebootGrant?
lastKnownControlPlaneConnection
healthState
```

Secrets are protected by machine-scoped platform protection or non-exportable key storage. ACLs restrict local files to SYSTEM and Administrators.

## User-session agent launch

- Enumerate interactive sessions.
- Select allowed active session according to policy.
- Obtain user token using documented WTS flow.
- Build a clean environment block.
- Launch signed Agent executable from trusted installation path.
- Pass only a one-time bootstrap handle/token through secure IPC.
- Verify child executable signature/hash before launch.

## Privileged broker command allowlist

Examples:

- `StartAgentForSession`
- `StopAgentForSession`
- `RequestSystemReboot`
- `StageUpdate`
- `ApplyUpdate`
- `ReadServiceHealth`
- `RotateDeviceKey`

Each command has schema validation and explicit authorization. There is no `Execute(commandLine)` operation.

## Service hardening

- restrictive service SID and file ACLs;
- no network listener on localhost unless authenticated and necessary;
- named-pipe ACL and mutual challenge;
- service recovery with bounded restart;
- protected configuration directory;
- minimal privileges and removal of unnecessary token privileges after startup where possible;
- event log source plus structured local logs with rotation.

## Managed command delivery

The installed Service uses the authenticated bounded-long-poll channel and credential lifecycle in `../02-protocol/managed-host-command-channel.md`. It treats every request as untrusted until tenant/device authorization version, expiry, policy decision, nonce, and device-key proof validate.
