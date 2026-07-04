# Incident-Response and Update-Key-Compromise Tabletop Exercises

Goal 12 requires running these tabletop exercises before GA. A tabletop is a
facilitated walkthrough with real incident-command staff; it cannot be
executed by a repository commit. What can be done here, and is done here, is
(1) design the scenario/injects/decision points precisely enough to run
immediately, and (2) mechanically verify that every technical control the
walkthrough depends on actually behaves as the runbook claims, so the human
exercise tests judgment and communication rather than discovering a broken
control mid-drill.

## Exercise 1 — Signed-update signing-key compromise

Scenario: telemetry raises `SigningAnomaly`
(`deploy/observability/prometheus-rules.yaml`) indicating a manifest signed
with a key not on the expected active-signer list.

Roles: incident commander, client release owner, security lead, comms lead
(customer status), scribe.

Injects, in order:

1. Alert fires; on-call declares SEV-1 per `08-operations/operations-overview.md`.
2. Runbook `08-operations/runbooks/signing-key-compromise.md` step 1: confirm
   whether the anomalous manifest was actually served to any client (check
   `UpdatePublicationStore` publication log) before revoking.
3. Decision point: revoke the suspected signer vs. halt all rollout first.
   Correct order per the runbook is halt promotion immediately, then revoke.
4. Verify old-key artifacts are now rejected: this is exactly
   `UpdateMetadataVerifierTests` anti-rollback/signer-mismatch coverage, run
   live against a build during the exercise, not merely cited.
5. Comms lead drafts a customer status update distinguishing "confirmed
   scope" from "under investigation" per the operational invariant in
   `operations-overview.md`.
6. Retrospective: was the anomaly detectable earlier; does the alert
   threshold need adjustment.

Pass criteria: rollout halted before revocation attempted, no re-enabled
promotion until a clean re-signed manifest passes the promotion gate
(`evaluate_update_rollout.py`), customer comms reviewed by security lead
before sending.

## Exercise 2 — General incident response (session/tenant compromise)

Scenario: an abuse report (`08-operations/runbooks/abuse-response.md`)
indicates a device is receiving managed-session requests it should not
receive after a reported credential leak.

Roles: same as above plus the affected tenant's Owner (simulated).

Injects:

1. Report received; triage classifies as confirmed unauthorized access.
2. Containment: revoke device, confirm `authorization_version` incremented
   and a subsequent poll/heartbeat with the old credential is rejected —
   verified live against the device-credential test suite during the drill.
3. Decision point: does this require tenant-wide suspension or single-device
   revocation; runbook requires justifying the blast-radius choice in the
   incident ticket.
4. Legal/privacy escalation trigger check against
   `00-product/commercialization-and-compliance.md` release-blocking
   question 7 (abuse investigators must act without unrestricted content
   access) — confirm the investigation used only metadata/audit records.
5. Retrospective and audit-record completeness check.

Pass criteria: containment action is scoped no wider than the evidence
supports, no session content was accessed during investigation, audit trail
is complete and attributable.

## Scheduling record (fill at execution)

| Exercise | Date | Facilitator | Participants | Findings | Action items |
|---|---|---|---|---|---|
| Signing-key compromise | _pending scheduling_ | | | | |
| General incident response | _pending scheduling_ | | | | |

## Production boundary

Scenario design and the underlying technical controls (anti-rollback
rejection, device revocation, audit attribution) are implemented and
automated-tested; running each exercise live against those controls during
the walkthrough is recommended and takes minutes. Scheduling real staff,
capturing findings and signing off action items are organizational steps
this repository cannot perform; `release-gates.md` treats the Scheduling
record above as incomplete, and therefore this gate as open, until both rows
have a dated facilitator and participant list.
