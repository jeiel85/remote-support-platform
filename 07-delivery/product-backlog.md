# Prioritized Product Backlog by Release Train

## Attended GA — Goals 01–12

- stable Windows capture/render and H.264 adaptation;
- direct and TURN connectivity with mandatory signed transport binding;
- authenticated operator, one-time host code, signed consent and scoped peer authorization;
- multi-monitor/DPI/cursor/input safety and emergency disconnect;
- text clipboard, safe file transfer and chat;
- tenant RBAC, Admin Portal, policy, audit, privacy/export/closure workflows and abuse reporting;
- portable Agent and per-user Operator Console packaging with signed updater;
- accessibility, Korean/English resources, SLO monitoring, runbooks and commercial/legal gates.

## Managed Host — Goal 13

- installed Windows Service and authenticated typed IPC;
- one-time device enrollment, active-key storage, credential renewal/rotation/revocation;
- authenticated managed request delivery and `HOST_PENDING` lifecycle;
- managed-attended local consent/notification;
- reboot continuity, service recovery, device health and Managed Host MSI;
- dedicated Windows/EDR/security qualification.

## Unattended GA — Goal 14

- unattended disabled by default and separately enabled through local admin or managed deployment;
- operator MFA and conditional policy;
- required notification/disclosure, audit and rapid revocation;
- schedules/posture extensions only after baseline unattended security is proven.

## Enterprise post-GA candidates

- SSO/provisioning integrations, webhooks/SIEM, dedicated relays, regional pinning;
- optional explicitly consented recording;
- privacy masking, multi-operator view-only collaboration;
- audio, printer/USB or other redirection only through separate architecture/security decisions.

## Explicitly forbidden or deferred

Hidden monitoring, portable persistence, arbitrary remote shell, credential harvesting, kernel driver without a separate necessity/security case, public plugin execution and any secure-desktop bypass technique.
