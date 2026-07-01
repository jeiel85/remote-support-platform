# Implementation Order and Release Trains

## Attended GA

1. Goal 01 — Foundation and contracts
2. Goal 02 — Capture and render
3. Goal 03 — Encoding
4. Goal 04 — LAN transport and mandatory transport binding spike
5. Goal 05 — Input and display topology
6. Goal 06 — Control plane and signed consent
7. Goal 07 — Internet ICE/TURN and transport binding
8. Goal 08 — Clipboard, file transfer and chat
9. Goal 09 — Attended product packaging
10. Goal 10 — Tenancy, Admin Portal, policy and audit
11. Goal 11 — Signed updates, observability and operations
12. Goal 12 — Attended GA qualification

Goal 12 gates only `ATTENDED_GA` plus applicable `ALL_RELEASES` requirements.

## Managed Host release

13. Goal 13 — Managed Host Foundation

This release adds service, device identity, command delivery, managed attended sessions, and reboot continuity. It does not enable unattended access.

## Unattended release

14. Goal 14 — Unattended Access

This release is separately approved after Managed Host production evidence and gates `UNATTENDED_GA` plus applicable `ALL_RELEASES` requirements.

No later goal may be pulled into an earlier release by hiding it behind a default-on flag. Experimental code must be compile-time or server-side disabled and excluded from the release threat model unless explicitly approved.
