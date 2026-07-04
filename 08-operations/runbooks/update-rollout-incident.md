# Runbook — Update Rollout Incident

## Trigger and ownership

The client-release owner takes command for `UpdateFailureSpike`, boot-loop or
health-check rollback signals. Escalate any signature, unexpected signer or
sequence anomaly immediately to the security incident commander and the
signing-key-compromise runbook.

## Containment

1. Freeze channel promotion and preserve the signed manifest, immutable artifact, approvals and telemetry window.
2. Set the affected rollout percentage to zero in a newly signed manifest with a higher release sequence; never edit published metadata in place.
3. Confirm clients reject the bad/corrupt artifact and retain the last-known-good launchable installation.
4. Segment failure rate by bounded channel, version, architecture, OS-build and GPU-vendor buckets; do not add tenant or device IDs to metrics.
5. If code execution or key compromise is plausible, stop publication and follow the signing-key-compromise procedure.

## Recovery and closure

Build a fixed artifact from an approved commit, repeat clean-worker signature,
hash, compatibility and rollback verification, then publish with a sequence
higher than every rejected release. Resume internal, canary, 5%, 25% and 100%
stages only after the defined observation window. Attach cohort counts,
success/failure/crash metrics, rollback count and incident approval to release
evidence.
