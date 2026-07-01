# Backend Domain Model

## Aggregates

### Tenant

- `TenantId`
- plan/entitlements
- security settings
- data region
- retention policy
- status: Active/Suspended/Closed

### Membership

- user, tenant, role set
- group memberships
- lifecycle status
- last privilege change

### Device

- installation/device IDs
- public keys/certificate references
- display name and group
- OS/build/app version
- enrollment and last-seen state
- unattended-access state
- revocation status

### SupportSession

- type: Attended/ManagedAttended/Unattended
- host identity/device
- operator identity
- requested/granted scopes
- state/version/epochs
- start/end and reason
- route/quality summary
- no content payload

### Policy

- subject selectors: role/group/operator
- resource selectors: device/group/tag
- conditions: time, network, MFA, session type
- allowed/denied scopes
- approval requirements
- version and activation state

## Policy evaluation result

```text
Decision
- allow: bool
- grantedScopes[]
- deniedScopes[] with reason
- requiresLocalConsent: bool
- requiresStepUpMfa: bool
- maxSessionDuration
- recordingPolicy
- filePolicy
- decisionId
- policyVersion
```

Every session stores the decision ID/version used at authorization.

## Invariants

- Suspended tenant cannot create new sessions.
- Revoked device cannot receive session authorization.
- Unattended session requires enrolled device, enabled policy and MFA claim.
- Granted scopes are subset of requested scopes and policy scopes.
- Ended/expired sessions cannot issue new TURN credentials.
- Device key rotation invalidates prior active device authorization according to policy.

## Managed-host invariants

A managed session is not host-authorized until a current device key signs the host decision and binds a fresh host peer key. Device credentials are short-lived projections of device key and authorization version; they are renewable without tenant re-enrollment but immediately invalid after device revocation. Tenant-scoped foreign keys use `(resource_id, tenant_id)` where redundant tenant columns exist.

## Final-audit entities

- `DeviceCredentialChallenge`: single-use active-key proof challenge with purpose, key version and expiry.
- `TenantInvitation`: role-limited, expiring invitation whose raw token is never persisted.
- `DataExportRequest`: asynchronous privacy export state machine with inventory/hash/expiry evidence.
- `TenantClosureRequest`: reauthenticated cooling-off workflow with state version and auditable cancellation/completion.
- `ManagedSessionDelivery`: represented by the support session, immutable policy decision and transactional outbox delivery; the device poll is a projection, not a second source of truth.
- `TransportBinding`: ephemeral peer evidence retained only as hashes/metadata required for security audit, not SDP or content.

Tenant-bearing relationships use composite resource/tenant foreign keys where the same tenant is redundantly stored. Repository methods still require explicit tenant context; database constraints and RLS are defense in depth rather than substitutes for authorization.
