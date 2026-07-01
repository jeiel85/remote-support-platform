# Identity and Access Control

## 1. Operator authentication

- OIDC Authorization Code with PKCE.
- System browser or brokered authentication; no embedded password collection.
- MFA required for tenant admins and unattended sessions.
- Short access token, rotated refresh token or provider-managed session.
- Local token cache protected per user.
- Device-code flow is not used for ordinary interactive sign-in unless reviewed.

## 2. Roles

| Role | Core permissions |
|---|---|
| Owner | tenant lifecycle, billing, admin assignment |
| Admin | users, devices, groups and policies |
| Security Auditor | read audit/security configuration, no session control |
| Operator | attended sessions and authorized devices |
| Read-only Analyst | session metadata and reports |

Avoid permission logic hard-coded only to roles. Policy evaluates actions and resources.

## 3. Step-up authentication

Require fresh MFA for:

- enabling unattended access;
- starting unattended session;
- changing security policy;
- exporting sensitive audit data;
- rotating/revoking device or signing-related keys;
- high-risk support-employee access.

## 4. Device enrollment

- admin creates single-use enrollment token;
- token has tenant, group, expiry and allowed installer channel;
- endpoint generates key locally;
- enrollment request proves key possession;
- server returns device identity/certificate and policy;
- token is invalidated transactionally;
- duplicate installation ID or key conflicts are reviewed.

## 5. Revocation

Revocation sources:

- user disabled;
- membership removed;
- tenant suspended;
- device revoked;
- unattended feature disabled;
- policy changed;
- suspected compromise.

Control plane pushes live termination when possible and denies all new credential issuance. Endpoint caches have short TTL and fail closed for new sessions.

## Explicit tenant context

Operator/admin API requests that act on tenant resources carry `X-Tenant-Id`. The BFF derives it from the server-side selected-tenant session; native clients select from memberships returned after login. The header is routing context only. Authorization independently loads the caller's active membership and privilege version, sets transaction-local `app.tenant_id`, and rejects absent, suspended or mismatched membership with a non-enumerating response. Tokens are not minted with permanently trusted client-selected roles.

## Peer and device proof

Bootstrap credentials request a single-use peer challenge before authorization exchange. Peer/device access tokens are sender-constrained with RFC 9449 DPoP, while operator OIDC bearer tokens are protected through the BFF or normal API audience/issuer/MFA policy. Token renewal never changes the bound key implicitly.
