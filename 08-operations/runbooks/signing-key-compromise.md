# Runbook — Signing or Update Key Compromise

## Trigger

- unauthorized signing event;
- private key exposure suspicion;
- accepted update with unknown provenance;
- metadata signature anomaly.

## Immediate containment

1. Declare SEV-1 and stop all update publication.
2. Disable compromised online key/role.
3. Block affected manifests/artifacts at CDN and update service.
4. Preserve signing logs, CI evidence and artifacts.
5. Determine whether Authenticode, update metadata or both are affected.
6. Rotate/revoke according to trust-root design.

## Recovery

- publish new signed metadata through uncompromised threshold/root process;
- release known-good client with higher update sequence;
- notify customers and provide verification steps;
- assess whether OS/vendor certificate revocation is required;
- monitor for malicious installed versions and revoke sessions if needed.

## Prevention evidence

- signing roles separated;
- offline/root key available and tested;
- immutable release evidence;
- two-person approval;
- periodic key inventory and drill.
