# Operations Overview

## Service ownership

- Control plane on-call.
- Relay/TURN on-call.
- Client release owner.
- Security incident commander rotation.
- Privacy/legal escalation contact.
- Abuse operations queue.

## Severity

- SEV-1: active compromise, malicious update, broad tenant exposure, global outage.
- SEV-2: regional/significant outage, major session failures, TURN capacity emergency.
- SEV-3: degraded feature or limited tenant impact.
- SEV-4: minor defect/documentation/support issue.

## Operational invariants

- Never disable security validation to restore service without incident command and documented containment.
- Do not turn TURN into anonymous/open relay during outage.
- Do not collect customer screen/content as default troubleshooting.
- Every emergency production access is time-bound and audited.
- Customer status communication distinguishes confirmed facts from investigation.
