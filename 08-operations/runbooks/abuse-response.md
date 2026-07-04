# Abuse Response Runbook

## Scope

Reports that a session, device or account was used without authorization,
against consent, or to deliver malware/harassment through file transfer or
remote control.

## Intake

- `POST /abuse-reports` accepts unauthenticated, host-bootstrap, peer and
  operator-authenticated submissions (`02-protocol/openapi/openapi.yaml`
  `/abuse-reports`) so a remote party without a platform account can report.
- Every report is queued to the abuse operations queue defined in
  `08-operations/operations-overview.md` service ownership and triaged within
  one business day; SEV-1 abuse (active account takeover, ongoing
  unauthorized control) pages the security incident commander immediately.
- Reports never require the reporter to submit session content; the control
  plane holds no screen/clipboard/file content per ADR-0005, so triage relies
  on session metadata, device/tenant identity and policy-decision records.

## Triage

1. Correlate the report with `tenant_id`/`session_id`/`device_id` from
   `support_sessions`, `session_participants` and `policy_decisions`.
2. Confirm whether the access was policy-bound and consented
   (`AT-FR-CON-*`, `AT-FR-ADM-*` evidence) or whether a control was bypassed.
3. Classify: confirmed unauthorized access, confirmed malware/file abuse,
   unsubstantiated, or platform defect.

## Containment

- Revoke the implicated device (`DELETE /devices/{deviceId}`) or suspend the
  membership; both immediately increment `authorization_version` and reject
  cached credentials per `05-security/identity-and-access.md` revocation
  section.
- Terminate active sessions tied to the device/user; do not leave a suspected
  compromised device able to receive new managed-session requests.
- Never widen TURN, disable signature checks, or grant broad support access
  to investigate; use the time-bound JIT support grant
  (`POST /tenant/support-grants`) which audits every read.

## Investigation record

Record in the tenant audit log and a security-incident ticket: report time,
reporter channel, affected tenant/device/session IDs, classification,
containment action and time, and remediation owner. Do not persist reporter
PII beyond what the ticketing/legal retention policy requires.

## Escalation

- Confirmed malware distribution or repeated abuse from a tenant escalates to
  the security incident commander and Owner/Admin of the affected tenant.
- Suspected law-enforcement-relevant activity escalates to the security
  contact published under
  `00-product/commercialization-and-compliance.md` business prerequisites,
  which routes to legal before any data disclosure.
- Abuse patterns that indicate a product control gap (e.g., consent bypass)
  file a defect against the owning goal and block release per
  `06-quality/release-gates.md` until remediated or risk-accepted.

## Production boundary

This runbook defines the operational process and the technical containment
primitives already implemented and tested (device revocation, audit
attribution, JIT support grants). Staffing the abuse operations queue with a
real on-call rotation, a public abuse-report intake address and law-enforcement
liaison contacts are organizational commitments outside repository scope and
must be completed before commercial GA.
