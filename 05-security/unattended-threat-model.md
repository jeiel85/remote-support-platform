# Unattended Access Threat Model

Goal 14 requires a dedicated threat model for unattended access as "a
separate high-risk product capability" (`07-delivery/goals/goal-14-unattended.md`).
This extends, and does not replace, `05-security/threat-model.md`; every
control there (signed transport binding, DPoP, audit hash chain, tenant
isolation) still applies. This document covers what is different when no
local human is present to grant or witness access.

## 1. What changes without a present local human

In Attended and Managed Attended sessions, a local person sees the
consent/notification prompt and can refuse or later report abuse. Unattended
removes that real-time witness, so every control that human normally
provided must be replaced by a technical or process control:

| Removed human control | Replacement control |
|---|---|
| Refuses an unrecognized operator | Policy evaluation (role, schedule, device) plus mandatory fresh step-up MFA on every session, enforced server-side and not overridable by policy configuration |
| Notices and reports the access | Persistent local notification is still shown (no hidden indicator); tenant audit log records every session; tenant can enumerate all unattended-enabled devices |
| Physically stops the session | Device/account revocation and explicit session termination, both server-authoritative and immediately effective for any subsequent server-mediated call |
| Approves the device joining the program in the first place | Two-party enrollment: a tenant admin request plus a separate confirmation that must originate from the device's own identity key, so enrollment cannot complete silently inside a normal attended session |

## 2. New/changed assets

- the `unattendedEnabled` flag on a device — a single boolean whose
  unauthorized flip is equivalent to granting standing access;
- the enrollment confirmation code — short-lived, single-use, bound to one
  device and one requester.

## 3. New/changed threats and controls

| Threat | Attack | Controls |
|---|---|---|
| Silent enablement | a compromised attended session or a malicious admin flips `unattendedEnabled` without the device's owner knowing | enrollment requires a request (tenant OWNER/ADMIN, fresh MFA) *and* a separate confirmation call authenticated by the device's own DPoP-bound key using an out-of-band code; neither party alone can complete it |
| Stolen enrollment code | an attacker who reads the code from the Admin Portal completes confirmation before the legitimate device does | code is single-use, expires quickly, and confirmation is still bound to the specific enrolled device's key — an attacker without that device's private key cannot confirm even with the code |
| Step-up bypass | a session is created without a fresh authentication event, relying only on a possibly-stale token | `UNATTENDED` session-type evaluation requires fresh step-up MFA unconditionally, independent of any policy document's `requireMfa` rule content |
| Policy misconfiguration grants standing global access | an over-broad `allDevices` ALLOW rule quietly includes unattended | the schema and evaluator both hard-require `UNATTENDED_SESSION` in requested and granted scopes, and unattended remains denied unless the target device's `unattendedEnabled` flag is independently true — a policy rule alone cannot enable it |
| Compromised device account continues unattended access after detection | attacker retains a valid device credential after compromise is discovered | device revocation immediately increments `authorizationVersion` (blocking new credential exchange, polling and session decisions) and proactively terminates any currently AUTHORIZED session tied to that device |
| Covert/hidden session | operator or malware suppresses the local indicator | Service never renders UI itself and the Agent's notification path is the same code path used for Managed Attended, so there is no separate "quiet" code path; a session that cannot reach the Agent to notify is a Service/Agent IPC failure, not a silent-success path |
| Pilot scope creep | unattended is enabled tenant-wide before it is validated | Goal 14 acceptance requires piloting only with approved tenants before general availability; this is an operational gate, not a code gate, and is tracked in the release-gate-approval process alongside Goal 12's |

## 4. Abuse cases specific to unattended

### Stalkerware / covert monitoring conversion

The general threat model already lists this; unattended sharpens it because
there is no per-session human witness. Controls: enrollment cannot be silent
(§3), the capability is absent from the portable Agent build entirely, every
unattended-enabled device is enumerable by the tenant, and every session is
audited with the same non-repudiable hash chain as every other action.

### Insider re-enabling a revoked device

A former admin or a compromised admin account re-requests enrollment on a
device it no longer legitimately administers. The two-party design still
requires that device's own key to confirm; if the device itself is not
complicit, the request expires unconfirmed. If the device *is* compromised,
this reduces to the "compromised device" threat above, whose control is
revocation plus proactive session termination.

## 5. Security test cases (in addition to the general threat model's list)

- attempt enrollment confirmation with a valid code but the wrong device key;
- attempt session creation with stale (non-fresh) MFA and confirm denial;
- attempt session creation against a device with `unattendedEnabled=false`
  and confirm denial regardless of policy;
- revoke a device mid-AUTHORIZED-session and confirm the session transitions
  to TERMINATED and no further signaling ticket/TURN credential is issued;
- confirm the portable Agent build contains no unattended session-type
  handling path.

## 6. Production boundary

This threat model, the enrollment/session/revocation controls it describes,
and the listed test cases are implemented and covered by automated tests
(`07-delivery/evidence/goal-14.md`). A dedicated third-party penetration test
and social-abuse review of the unattended capability specifically, and the
approved-tenant pilot itself, are organizational steps outside what a
repository commit can perform and remain open per that evidence document's
production boundary.
