# Audit and Incident Response

## 1. Audit events

Security-relevant events include:

- sign-in success/failure and MFA step-up;
- membership/role/policy changes;
- device enrollment, key rotation and revocation;
- attended code creation/resolution/rejection;
- session authorization, scope change and termination;
- unattended session attempt/result;
- file transfer metadata and policy result;
- update publication and rollout change;
- support-employee privileged access;
- abuse case and enforcement action.

## 2. Tamper evidence

- stable event canonicalization;
- event hash includes prior event hash within a partition/stream;
- restricted write role;
- external export or immutable retention option for enterprise;
- periodic verification job and alert on chain failure.

Hash chaining does not replace database access control or backups.

## 3. Incident classes

- account takeover;
- device-key compromise;
- signing/update compromise;
- tenant data exposure;
- TURN infrastructure abuse/DDoS;
- malicious tenant/operator abuse;
- critical native vulnerability;
- privacy incident.

## 4. Incident sequence

1. Detect and declare.
2. Preserve relevant minimal evidence.
3. Contain credentials, accounts, devices, versions or regions.
4. Assess scope and customer impact.
5. Eradicate and patch.
6. Recover through staged verification.
7. Notify according to law/contract.
8. Complete retrospective and control updates.

Legal notification deadlines vary by jurisdiction. The launch checklist must revalidate current requirements; the system supports rapid event export and affected-subject identification.
