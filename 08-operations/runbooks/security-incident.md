# Runbook — Security Incident

## First hour checklist

- assign incident commander, security lead, operations lead and communications lead;
- record confirmed facts and hypotheses separately;
- protect evidence and restrict access;
- identify affected identities, devices, tenants, versions and regions;
- revoke/rotate tokens, device keys, users or releases as necessary;
- disable only affected capability when possible;
- consult legal/privacy notification requirements.

## Evidence sources

- audit event stream;
- identity provider logs;
- session metadata;
- signaling and TURN operational metadata;
- release/signing evidence;
- endpoint support bundle only with authorized collection;
- cloud/network access logs.

## Customer content

Do not expand collection to screen, clipboard or files merely for investigation. Any exceptional collection requires lawful basis, necessity review, explicit authorization and documented handling.

## Closure

- root cause and timeline;
- affected population methodology;
- remediation and validation;
- notifications;
- new tests/alerts/controls;
- executive and technical retrospective.
