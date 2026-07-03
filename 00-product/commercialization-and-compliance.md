# Commercialization and Compliance Checklist

This document is an engineering checklist, not legal advice. Counsel must review the final product, target countries, contracts and data flows.

## 1. Business prerequisites

- Product and company naming/trademark search.
- Terms of service, acceptable-use policy and privacy notice.
- Data processing agreement for B2B customers.
- Subprocessor list and regional hosting disclosures.
- Security contact and vulnerability disclosure policy.
- Abuse reporting, account suspension and law-enforcement request process.
- Customer support and service-status channels.

## 2. Privacy-by-design data classes

| Data class | Examples | Default retention |
|---|---|---|
| Account identity | name, email, tenant, MFA state | account life + legal retention |
| Device identity | device ID, public key, OS/build, app version | enrollment life + short tombstone |
| Session metadata | start/end, participants, scopes, route, bytes | tenant-configured, e.g. 30–180 days |
| Security events | failed auth, policy denial, revocation | longer security retention, access restricted |
| Content data | screen, input, clipboard, files | not stored by control plane |
| Support diagnostics | crash dumps, traces | explicit collection and short retention |

### Goal 10 governance inventory

| Persisted field group | Classification and purpose | Access owner | Retention / deletion / export behavior |
|---|---|---|---|
| Tenant profile and settings | Customer metadata required for tenancy, regional routing, feature limits and retention policy | Owner/Admin; limited read for authorized tenant roles | Tenant life; included in owner export; deleted after closure evidence and required legal hold |
| Membership subject, display name, email and roles | Personal/account data required for authentication, authorization and invitations | Owner/Admin; Auditor read | Membership life plus tenant-configured audit evidence; removed access is immediate; included in owner export |
| Invitation email, role and HMAC token lookup | Contact/account bootstrap data | Owner/Admin; Auditor read metadata | Until acceptance, revocation or expiry; raw token is never persisted |
| Device ID, public key, versions and health metadata | Device/security metadata required for enrollment and revocation | Owner/Admin; authorized read roles | Enrollment life plus short revocation tombstone; private keys and reusable raw credentials are not stored |
| Policy documents, immutable versions and decisions | Security configuration and authorization evidence | Owner/Admin; Auditor read | Tenant life and configured audit retention; decision inputs contain normalized server-derived metadata only |
| Audit actor/target/action metadata and hashes | Restricted security evidence | Owner/Admin/Security Auditor | Tenant-configured retention with a hash checkpoint; no session content or secrets; JSONL export is audited |
| Export workflow ID, artifact hash and expiry | Restricted privacy-workflow metadata | Owner only | Artifact expires after 24 hours or first download; workflow evidence follows audit retention |
| Closure workflow state and timestamps | Restricted privacy-workflow metadata | Owner only | Cooling-off and completion evidence follows audit/legal retention; free-form reason is validated but not persisted |
| Support JIT grant subject, case reason code and expiry | Highly restricted privileged-access evidence | Tenant Owner and governed platform security staff | Maximum one-hour grant; every metadata read is attributed and audited; session content remains unavailable |

All Goal 10 records use the tenant data region as the processing boundary. A
production deployment must map that logical region to its PostgreSQL, backup and
export-storage locations. The file export sink is an operator-configured hook;
object paths and raw invitation, enrollment and download secrets are never
returned in audit details.

## 3. Korea launch considerations

For a Korea-based commercial service, validate the current Personal Information Protection Act and subordinate rules at launch time. The design should support:

- documented purpose and minimum collection;
- access control, authentication failure restrictions and encrypted internet transmission;
- access-log generation and review;
- retention/deletion controls and data-subject request workflows;
- breach detection, escalation and legally required reporting/notification workflow;
- current privacy-policy disclosures and subprocessors.

The current official guidance baseline should be rechecked before launch because regulations and guidance can change.

## 4. Security evidence package for customers

- Architecture and data-flow diagram.
- Encryption and key-management statement.
- Penetration-test executive summary.
- SBOM and vulnerability-management policy.
- Secure development lifecycle statement.
- Incident-response and business-continuity overview.
- Data retention and deletion statement.
- Subprocessor and region list.
- Code-signing and update security description.

## 5. Release-blocking compliance questions

1. Does any telemetry capture screen text, clipboard content or filenames beyond necessity?
2. Are IP addresses, device names and operator identities classified and retained intentionally?
3. Can a tenant export and delete its data?
4. Can an end user identify who connected and what permissions were granted?
5. Is session recording disabled unless separately consented and contracted?
6. Are cross-border transfers and subprocessors documented?
7. Can abuse investigators act without unrestricted customer-content access?
8. Are all privileged support-employee actions audited?

## Codec, cryptography, and distribution legal gate

Before any paid or broadly distributed release, qualified counsel and the release owner must review H.264/AVC patent-pool or other licensing obligations for the actual encoder/decoder distribution and usage model, all third-party media binaries and notices, cryptography/export and sanctions requirements for target markets, privacy terms, remote-support consent/disclosure wording, and code-signing certificate terms. Engineering documentation is not a legal opinion. A release is blocked until the decision, applicable territories, obligations, owner, renewal date, and evidence are recorded.
