# Windows Process and Privilege Model

## 1. Executables

| Executable | Account/session | Purpose |
|---|---|---|
| `RemoteSupport.Agent.exe` | interactive user | consent UI, capture, local indicator, clipboard, user input context |
| `RemoteSupport.Console.exe` | operator user | remote view/control UI and session tools |
| `RemoteSupport.Service.exe` | LocalSystem, Session 0 | installation health, user-session agent lifecycle, reboot continuity, privileged IPC broker |
| `RemoteSupport.ElevatedHelper.exe` | elevated interactive context when authorized | operations requiring higher integrity; tightly scoped |
| `RemoteSupport.Updater.exe` | service-controlled | verify and apply signed updates |
| `RemoteSupport.CrashReporter.exe` | least privilege | user-approved diagnostic packaging; no automatic sensitive dump upload |

Portable attended mode runs only `Agent.exe` and native libraries. It has no service, persistence or unattended capability.

## 2. Session 0 separation

Windows services run in Session 0 and cannot provide normal interactive UI. The service therefore:

1. detects active logon sessions;
2. obtains the target user-session token through documented WTS APIs;
3. starts `Agent.exe` in that user session;
4. communicates through authenticated named pipes;
5. never renders consent UI itself.

## 3. IPC design

- Named pipe path includes installation ID and protocol version.
- Pipe ACL allows only LocalSystem, Administrators and the exact logged-on user SID.
- Both sides perform challenge-response using installation device keys or an ephemeral service nonce.
- Messages are length-prefixed Protobuf with maximum size bounds.
- Privileged operations use an allowlist command enum; no arbitrary command line or DLL path.
- Every request includes correlation ID, caller session ID and capability token.
- Service re-evaluates authorization; it never trusts the UI process claim alone.

## 4. Privilege levels

### Portable mode

- Standard user integrity.
- Can control equal/lower integrity applications through `SendInput` subject to Windows UIPI.
- UAC secure desktop requires local user action.

### Installed attended mode

- Service maintains lifecycle and can launch an authorized helper.
- Elevated application control is enabled only after local consent and validated broker setup.
- Secure desktop is reported as a distinct capability.

### Unattended mode

- Requires per-machine installation and organization policy.
- Service authenticates operator authorization and starts the interactive agent.
- No hidden desktop or covert user monitoring.
- Pre-logon and secure-desktop control remain a separate, compatibility-gated product feature.

## 5. Secure desktop policy

Do not implement unsupported SAS/UAC bypasses. The product must:

- detect desktop switch and signal `SECURE_DESKTOP_LOCAL_ACTION_REQUIRED` unless a validated module is available;
- freeze or blank the remote frame rather than showing stale content as live;
- clearly tell the operator that local approval is required;
- gate any future secure-desktop module behind code signing, threat review, enterprise policy and Windows build testing;
- never weaken UAC policy automatically.

## 6. Watchdog and recovery

- Service supervises per-session agent heartbeat.
- Agent crash restarts are rate-limited to avoid loops.
- Native media worker can be isolated in-process initially, then moved to a sandboxed child process if crash evidence justifies it.
- All restarts emit audit and diagnostic events without sensitive payloads.
