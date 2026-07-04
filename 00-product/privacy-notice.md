# Privacy Notice (Draft)

**Status: engineering draft pending qualified counsel review and signature.
Not published or binding. See
`00-product/commercialization-and-compliance.md` — this document is not legal
advice.**

## What this product does not do

- The control plane does not store screen content, keystrokes, clipboard
  content or file contents. Session data is relayed or, where policy allows
  direct connection, exchanged peer-to-peer; see ADR-0005
  (`01-architecture/adr/0005-no-content-storage.md`).
- Session recording is hard-disabled at the platform level for this release;
  it is not an available feature to enable, per Goal 10 evidence.

## What we collect and why

Reflects the data classes in
`00-product/commercialization-and-compliance.md` §2 and the Goal 10
governance inventory:

| Data | Purpose | Retention |
|---|---|---|
| Account identity (name, email, tenant, MFA state) | Authentication, authorization, support | Account life plus legal retention |
| Device identity (device ID, public key, OS/app version) | Enrollment, security, revocation | Enrollment life plus short tombstone |
| Session metadata (start/end, participants, scopes, route, bytes) | Billing, support, security | Tenant-configured, default 30–180 days |
| Security events (auth failures, policy denials, revocations) | Fraud/abuse prevention, audit | Extended security retention, access-restricted |
| Support diagnostics (crash dumps, traces) submitted through the in-product
  support-bundle tool | Troubleshooting | Explicit user action required; short retention |

## Your rights

Tenant Owners can export the tenant's account/device/audit metadata
(`POST` export workflow, Goal 10) and request tenant closure, which begins a
cooling-off period before data deletion. Individual data-subject rights
(access, deletion, correction) are honored per the data-processing agreement
executed with the tenant and applicable law in the tenant's data region.

## Subprocessors and region

_Pending: final subprocessor list (cloud hosting, TURN relay provider, error-
telemetry/collector operator if third-party, payment processor) and the
data-region mapping per tenant, to be completed by the release owner before
publication._

## Security contact

_Pending: published security contact address and vulnerability-disclosure
policy link, required by `00-product/commercialization-and-compliance.md`
§1 before this notice may be published._

## Changes

_Pending: change-notification process (e.g., advance notice period for
material changes) to be defined with counsel._
