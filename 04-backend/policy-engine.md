# Policy Engine

## 1. Objective

Produce deterministic, explainable authorization decisions for session creation and capability changes. The policy engine is an in-process domain module in the initial modular monolith; it is not a network dependency.

## 2. Evaluation input

```text
PolicyInput
- tenantId
- operatorUserId
- membershipRoles[]
- operatorGroups[]
- authenticationTime
- mfaMethods[]
- stepUpTime?
- sourceIp / sourceNetworkClass?
- deviceId?
- deviceGroups[]
- deviceTags[]
- deviceStatus / appVersion / posture?
- sessionType
- requestedScopes[]
- requestedDuration
- localUserPresent
- currentUtc
```

Only normalized server-derived claims enter evaluation. Client-supplied role, tenant, MFA, device status, or group claims are ignored.

## 3. Policy document

A versioned policy contains:

- subject selector: roles, users, groups;
- resource selector: devices, groups, tags, all devices;
- session types;
- schedule and timezone;
- source network/IP conditions;
- required authentication age and MFA methods;
- allowed and denied scopes;
- maximum duration;
- local-consent rule;
- file-transfer rule;
- notification rule;
- enabled state and effective interval.

Policy JSON is validated against a schema before activation. Activated versions are immutable.

## 4. Deterministic algorithm

1. Reject inactive tenant, membership, device, or key.
2. Select active policy versions effective at `currentUtc`.
3. Match subject, resource, session type, schedule, source, posture, and authentication conditions.
4. If no allow rule matches, deny by default.
5. Union scopes from matched allow rules.
6. Union scopes from matched deny rules and subtract them from allowed scopes.
7. Intersect with requested scopes and product/plan entitlements.
8. Apply hard platform prohibitions; policy cannot enable unsupported secure-desktop or hidden access.
9. Select the strictest requirements across matched policies:
   - local consent: required wins;
   - MFA/step-up: strongest/most recent requirement wins;
   - duration/file limit: minimum wins;
   - notifications/audit: most visible/retentive rule allowed by privacy policy wins.
10. Emit a complete decision and explanation codes.

Deny rules override allow rules. Equal-priority ambiguity is resolved by the strictest result, never by document order.

## 5. Decision output

```text
PolicyDecision
- decisionId
- tenantId
- policyVersionIds[]
- allow
- grantedScopes[]
- deniedScopes[{scope, reasonCode}]
- requiresLocalConsent
- requiresStepUpMfa
- acceptedMfaMethods[]
- maxSessionDurationSeconds
- filePolicySnapshot
- notificationPolicySnapshot
- explanationCodes[]
- evaluatedAt
- inputHash
```

The session stores `decisionId` and immutable snapshots needed to enforce the authorization after policy edits.

## 6. Caching

- Cache only normalized active policy documents, not final user/device decisions by default.
- Cache key includes tenant and policy activation version.
- Membership, device, key, policy, or tenant changes increment a revocation/policy version and invalidate caches.
- A cache failure is fail-closed for new privileged/unattended sessions.

## 7. Tests

Mandatory table-driven cases:

- no matching policy;
- allow and deny conflict;
- role/group overlap;
- expired schedule;
- stale MFA and valid step-up;
- revoked device/key;
- requested scope subset/superset;
- plan entitlement restriction;
- policy changed during active session;
- daylight-saving/timezone boundary;
- tenant isolation with same identifiers in different tenants.
