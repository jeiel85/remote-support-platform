# Authorization Matrix

## Roles

- Owner
- Admin
- SecurityAuditor
- Operator
- ReadOnlyAnalyst
- PlatformSupport is an internal just-in-time role, never a tenant membership role.

## Tenant actions

| Action | Owner | Admin | SecurityAuditor | Operator | ReadOnlyAnalyst |
|---|---:|---:|---:|---:|---:|
| Read tenant profile | Yes | Yes | Yes | Limited | Yes |
| Change security/retention settings | Yes | Yes | No | No | No |
| Manage memberships/roles | Yes | Yes, except owner boundary | No | No | No |
| Create/activate policy | Yes | Yes | Review only | No | No |
| Enroll/revoke device | Yes | Yes | Read | No | Read |
| Start attended session | Yes | Yes | No | Yes | No |
| Start managed attended session | policy | policy | No | policy | No |
| Start unattended session | policy + MFA | policy + MFA | No | policy + MFA | No |
| Read audit metadata | Yes | Yes | Yes | Own sessions only | policy-limited |
| Export audit/SIEM | Yes | Yes | Yes | No | No |
| Publish client update | No tenant role; platform release role only | | | | |

## Session actions

Every action also requires current session membership, token audience, epoch, granted scope, and policy revision.

| Action | Host | Operator | Server policy |
|---|---:|---:|---:|
| Approve/reject attended request | Yes | No | may expire/deny |
| Revoke host-granted scope | Yes | No | may revoke |
| Request additional scope | No direct grant | Yes | local consent/policy required |
| End session | Yes | Yes | Yes |
| Obtain signaling ticket | current peer only | current peer only | may deny |
| Obtain TURN credential | current peer only | current peer only | may deny |
| Request reboot | approve/policy | requires scope | may deny |
| Issue reconnect epoch | request | request | authoritative CAS |

## Enforcement layers

1. API endpoint policy.
2. Application command authorization.
3. Aggregate invariant.
4. Repository tenant context and optional PostgreSQL RLS.
5. Peer/local runtime scope enforcement.

Tests must demonstrate that bypassing one presentation layer does not bypass the remaining authorization layers.

## Tenant context processing

For tenant-scoped operator endpoints, the request must include `X-Tenant-Id`. Middleware validates UUID syntax, resolves the authenticated subject, loads active membership and authorization version, creates an immutable authorization context, and only then sets `SET LOCAL app.tenant_id` inside the database transaction. Background/platform jobs use a separate explicitly authorized execution context and cannot reuse an end-user connection with stale tenant state.
