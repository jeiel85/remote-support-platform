# Admin Portal

## Technology and trust boundary

The portal is an ASP.NET Core Blazor Web App on .NET 10 using server-side rendering and interactive server components where justified. It is a backend-for-frontend (BFF): OIDC authorization-code flow with PKCE terminates on the server, the browser receives only secure `HttpOnly`, `SameSite=Lax/Strict` cookies, and reusable API bearer tokens are never written to browser storage. Anti-forgery validation is required on every state-changing browser request.

The portal and control-plane API are independently deployable processes sharing generated OpenAPI clients, authorization policy names, design tokens, localization resources, and telemetry conventions. The portal does not access PostgreSQL directly.

## Required routes

| Route | Roles | Workflow |
|---|---|---|
| `/overview` | all tenant roles | service health, active sessions, security notices |
| `/members` | Owner/Admin; read for Auditor | invite, role update, suspend, remove |
| `/devices` | Owner/Admin/Operator; read for Auditor | enrollment, revoke, group, health, update state |
| `/policies` | Owner/Admin; read for Auditor | draft, validate, version, activate |
| `/audit` | Owner/Admin/Auditor | filter, verify chain status, export metadata |
| `/settings` | Owner/Admin | retention, feature policy, transfer limits |
| `/privacy/export` | Owner | request and track data export |
| `/tenant/close` | Owner | reauthentication, cooling-off, closure status |

## Security requirements

- Content Security Policy uses nonces and denies object/embed; framing is denied except explicitly approved support surfaces.
- CSRF tokens, origin validation, secure cookies, session rotation after privilege change, short idle timeout for administrators, and step-up authentication protect privileged actions.
- The portal renders security-relevant values from server-authorized view models and never trusts hidden fields for tenant, role, version, or policy identity.
- All mutating requests use idempotency keys where supported and `If-Match` resource versions for lost-update protection.
- Audit export links are short-lived, one-time where feasible, and never expose object-store paths.

## Accessibility and localization

Korean and English ship together. Navigation, dialogs, tables, consent-related settings, destructive confirmations, and live operation states meet the accessibility requirements in `accessibility-localization.md`. End-to-end tests run with keyboard-only navigation and automated accessibility analysis; destructive tenant workflows require an explicit text confirmation and reauthentication.

## Testing

Component tests cover authorization-aware rendering. Contract tests use the generated OpenAPI client against a disposable API. Browser tests cover OIDC/BFF session lifecycle, CSRF, role changes, optimistic concurrency, invitation revocation, export, closure, localization, and accessibility. No test bypass may be enabled in production builds.

## Tenant selection

For a user with multiple memberships, tenant selection is stored in the encrypted server-side BFF session. The BFF adds `X-Tenant-Id` to generated API calls after revalidating that the membership remains active. The browser cannot override tenant context through a hidden field, query string or local storage value.
