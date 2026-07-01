# Release Gates

A release may ship only when every P0 requirement whose `release_train` is the target train or `ALL_RELEASES` has passing acceptance evidence, and all applicable legal/security/operations gates are approved. P1/P2 exceptions require a recorded owner, risk acceptance, expiry, and customer impact statement; security or consent invariants cannot be waived by product management alone.

## Common gates

- Contracts validate; schema/API/Proto/SQL/ABI compatibility suites pass.
- Clean protected workers build from lock files and produce SBOM, provenance and signed artifacts.
- Threat model, abuse review, privacy/data inventory, dependency/license review, codec/patent review, cryptography/export review and incident runbooks are current.
- External penetration testing has no unresolved critical/high finding; medium findings have approved remediation dates.
- Accessibility/localization, supported Windows matrix, performance/SLO, backup/restore and rollback evidence are attached.
- Update root/manifest/artifact signatures, expiry and rollback tests pass.

## Attended GA

Goals 01–12 complete. Portable Agent and Operator Console only. Signed consent, peer proof, TURN authorization and signed transport binding cannot be bypassed. Managed Host and unattended code are absent or release-disabled and excluded from installers.

## Managed Host

Attended GA is stable, Goal 13 completes, Service/IPC/device-key external review passes, EDR/AV and Windows lifecycle lab evidence is approved, revocation works within the documented bound, and unattended policy remains disabled.

## Unattended GA

Managed Host production evidence meets the defined observation period, Goal 14 completes, MFA/policy/local authorization/notification and emergency revocation tests pass, and a separate legal/privacy/abuse/security approval is signed.
