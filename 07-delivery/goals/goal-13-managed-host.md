# Goal 13 — Managed Host Foundation

## Objective

Add the separately released installed-host capability required for enrolled devices, authenticated command delivery, reboot continuity, and centrally revocable management. This goal starts only after attended GA.

## Deliverables

- Windows Service, interactive Agent launcher, typed authenticated IPC, session/process validation, replay protection, and no generic command primitive.
- One-time enrollment, non-exportable device key where supported, credential challenge/exchange, renewal, rotation, revocation, and authorization-version reconciliation.
- Authenticated bounded-long-poll managed command channel with transactional outbox, at-least-once delivery, idempotent acknowledgement, and offline expiry.
- `HOST_PENDING` state and managed-attended local consent/notification workflow.
- Reboot request approval and bounded reconnect continuity without persisting reusable peer/device access tokens or peer private keys across reboot.
- Managed Host MSI, service recovery policy, updater, rollback, diagnostics, device health, admin portal enrollment/revocation views.
- Windows CI/lab evidence for Session 0 isolation, fast user switching, RDP, UAC boundary, reboot, sleep/resume, EDR, proxy, and upgrade/downgrade.

## Exit criteria

All `MANAGED_HOST` and applicable `ALL_RELEASES` P0 requirements pass; external security review covers service/IPC/device identity; no unattended session is enabled by this goal.
