# Functional Requirements

Requirement IDs are stable and should be referenced by tests, issues and release evidence.

## 1. Session initiation

| ID | Requirement | Priority |
|---|---|---|
| FR-SES-001 | Portable Agent displays a cryptographically random, short-lived support code. | P0 |
| FR-SES-002 | The code identifies a pending session but is not itself the sole authentication secret. | P0 |
| FR-SES-003 | Operator must be authenticated for commercial SaaS sessions. | P0 |
| FR-SES-004 | Host displays operator identity, organization and requested permissions before approval. | P0 |
| FR-SES-005 | Host can approve view-only, input-control, clipboard and file scopes independently. | P0 |
| FR-SES-006 | Session request expires automatically and cannot be replayed. | P0 |
| FR-SES-007 | Both peers show connection state and relay/direct route classification. | P1 |

## 2. Screen capture and display

| ID | Requirement | Priority |
|---|---|---|
| FR-SCR-001 | Capture the selected monitor at native resolution and current orientation. | P0 |
| FR-SCR-002 | Detect monitor add/remove, resolution, rotation, HDR and DPI changes. | P0 |
| FR-SCR-003 | Allow operator to choose monitor and fit/actual-size/stretch modes. | P0 |
| FR-SCR-004 | Preserve cursor shape, position and visibility. | P0 |
| FR-SCR-005 | Adapt bitrate, frame rate and scale to network and CPU/GPU pressure. | P0 |
| FR-SCR-006 | Provide text-clarity and motion-priority quality profiles. | P1 |
| FR-SCR-007 | Mask configured windows or regions when enterprise policy requires it. | P2 |

## 3. Remote input

| ID | Requirement | Priority |
|---|---|---|
| FR-INP-001 | Remote control is disabled until host grants input scope. | P0 |
| FR-INP-002 | Map absolute coordinates correctly across mixed-DPI multi-monitor desktops. | P0 |
| FR-INP-003 | Support mouse buttons, wheel, keyboard scan codes and Unicode text input. | P0 |
| FR-INP-004 | Release stuck keys/buttons on disconnect, focus loss and transport reset. | P0 |
| FR-INP-005 | Local user activity may temporarily override or pause remote input by policy. | P1 |
| FR-INP-006 | Local emergency disconnect hotkey is always active and cannot be disabled. | P0 |
| FR-INP-007 | Elevated and secure-desktop capabilities are reported explicitly, never silently assumed. | P0 |

## 4. Clipboard and files

| ID | Requirement | Priority |
|---|---|---|
| FR-DAT-001 | Clipboard sync is off until permission is granted. | P0 |
| FR-DAT-002 | Initial GA supports text clipboard only; rich formats are separately gated. | P0 |
| FR-DAT-003 | File transfer requires explicit direction, size, destination and policy checks. | P0 |
| FR-DAT-004 | Partial transfers resume using chunk hashes and transfer IDs. | P1 |
| FR-DAT-005 | Received files are marked as externally sourced and never auto-opened. | P0 |
| FR-DAT-006 | Operator and host can cancel an active transfer immediately. | P0 |
| FR-DAT-007 | Audit log stores metadata, not file content. | P0 |

## 5. Chat and session controls

| ID | Requirement | Priority |
|---|---|---|
| FR-CTL-001 | Provide in-session text chat. | P1 |
| FR-CTL-002 | Host can revoke individual scopes without ending the session. | P0 |
| FR-CTL-003 | Host and operator can terminate the session. | P0 |
| FR-CTL-004 | Session indicator shows active scopes and elapsed time. | P0 |
| FR-CTL-005 | Reconnect requires a bounded grace token and state reconciliation. | P1 |

## 6. Managed devices and unattended access

| ID | Requirement | Priority |
|---|---|---|
| FR-MGT-001 | Device enrollment binds an installation to a tenant using a one-time enrollment token. | P0 |
| FR-MGT-002 | Device private keys remain non-exportable where platform APIs support it. | P0 |
| FR-MGT-003 | Unattended access is disabled by default. | P0 |
| FR-MGT-004 | Enabling unattended access requires local admin action or centrally managed enterprise deployment. | P0 |
| FR-MGT-005 | Operator MFA and policy authorization are required for unattended sessions. | P0 |
| FR-MGT-006 | Device access can be revoked immediately from the tenant console. | P0 |
| FR-MGT-007 | Every unattended connection generates host notification and audit evidence unless a documented enterprise policy says otherwise. | P0 |
| FR-MGT-008 | An installed device refreshes and rotates its control-plane credential by proving possession of its active device key, without repeating tenant enrollment. | P0 |
| FR-MGT-009 | An installed host receives managed-session requests through an authenticated, revocable device channel and binds each launched host peer to a fresh ephemeral key. | P0 |

## 7. Administration and audit

| ID | Requirement | Priority |
|---|---|---|
| FR-ADM-001 | Tenant roles include Owner, Admin, Security Auditor, Operator and Read-only Analyst. | P0 |
| FR-ADM-002 | Access decisions evaluate tenant, operator, device, policy, time and requested scope. | P0 |
| FR-ADM-003 | Audit records are append-only at the application layer and tamper-evident. | P0 |
| FR-ADM-004 | Admin can configure retention, allowed features, file limits, and an explicit recording-disabled policy for attended GA; recording enablement is a separately released capability. | P0 |
| FR-ADM-005 | Security events can be exported through webhook/SIEM integration. | P1 |
| FR-ADM-006 | Support staff access to customer metadata uses just-in-time privileged workflow and is audited. | P0 |
| FR-ADM-007 | Tenant owners and admins can invite members, update authorized roles, suspend access, and remove memberships through audited workflows. | P0 |
| FR-ADM-008 | Tenant owners can request data export and tenant closure and can track each workflow to auditable completion. | P0 |
