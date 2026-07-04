# Data Processing Agreement Template (Draft)

**Status: engineering draft pending qualified counsel review and signature.
Not a binding agreement. Required for B2B customers per
`00-product/commercialization-and-compliance.md` §1.**

## Roles

Customer (tenant Owner organization) is the data controller for its account,
device and session metadata. The service provider is the data processor,
processing only as instructed through the product's documented tenant
administration functions (Admin Portal, API) and this agreement.

## Processing description

Reflects `00-product/privacy-notice.md` data classes and Goal 10 governance
inventory: account identity, device identity, session metadata, security
events, and support diagnostics submitted through the explicit support-bundle
workflow. Screen, keystroke, clipboard and file *content* passing through an
authorized session is not processed or stored by the control plane (ADR-0005)
and is therefore out of scope of this DPA's processing description, subject
to counsel confirming that characterization for the customer's regulatory
context.

## Subprocessors

_Pending: final subprocessor list (hosting, TURN relay, telemetry/collector
operator) matching `00-product/privacy-notice.md` "Subprocessors and region"
— must be completed before this DPA can be executed._

## Data subject requests

The tenant Owner export and closure workflows (Goal 10) are the mechanism for
fulfilling data-subject access/deletion requests routed through the customer;
the processor does not respond directly to individuals absent customer
instruction, except where law requires otherwise.

## Security measures

References `00-product/commercialization-and-compliance.md` §4 security
evidence package: architecture/data-flow diagram, encryption/key-management
statement, penetration-test executive summary
(`06-quality/penetration-test-scope-and-closure.md`), SBOM, SDLC statement
(`05-security/secure-sdlc.md`), incident-response overview
(`08-operations/operations-overview.md`), retention/deletion statement.

## Breach notification

_Pending: counsel-defined notification timeline (commonly 24–72 hours from
confirmed determination) consistent with the customer's regulatory regime
and `08-operations/runbooks/security-incident.md` internal escalation
timeline._

## Cross-border transfer mechanism

_Pending: counsel to select the applicable transfer mechanism (e.g., standard
contractual clauses) per customer region; `00-product/commercialization-and-
compliance.md` §3 flags this specifically for Korea-based launch._

## Term and audit rights

_Pending: standard DPA term/termination and customer audit-right clauses to
be finalized by counsel._
