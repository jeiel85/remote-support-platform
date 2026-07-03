# Goal 10 evidence

Date: 2026-07-03. Local validation environment: Windows x64, .NET SDK
10.0.301, ASP.NET Core 10.0.9, in-memory deterministic test persistence and
production PostgreSQL repository/migration paths.

## Implemented deliverables

- authenticated `X-Tenant-Id` middleware resolves an active membership from the
  server store; client-supplied roles and tenant claims are not accepted as the
  authorization context;
- Owner, Admin, Security Auditor, Operator and Read-only Analyst boundaries,
  optimistic membership versions, invitations, suspension/removal and the
  last-owner invariant;
- one-time HMAC-indexed device enrollment, P-256 proof-of-possession binding,
  tenant-scoped inventory and immediate credential/authorization revocation;
- immutable policy versions, explicit activation, deterministic deny-overrides
  evaluation, fresh-MFA requirements, local-consent output, scope/duration/file
  limits, policy decision IDs and input hashes;
- append-only PostgreSQL audit rows and stable tenant hash chains with
  modification/gap detection, JSONL export and retention checkpoints;
- tenant settings with recording hard-disabled, owner-only privacy exports,
  24-hour one-time downloads, closure cooling-off/cancellation/completion and
  retention/expiry maintenance;
- time-bounded platform-support JIT grants with owner approval and an audit
  event for each customer-metadata read;
- independently deployable Blazor server-rendered BFF with OIDC code/PKCE,
  server-only access tokens, secure session cookies, CSRF/origin validation,
  CSP and the eight required Korean admin routes.

## Automated evidence

`GovernanceApiTests` maps to AT-FR-ADM-001/002/003/004/006/007/008,
AT-NFR-SEC-006/010 and AT-NFR-PRV-005 and covers:

- unrelated tenant and foreign-resource indistinguishability;
- role-aware reads and denied mutations;
- invitation acceptance, policy MFA/deny precedence and deterministic hashes;
- audit modification and sequence-gap detection;
- signed device enrollment and revocation;
- export creation/download, closure completion and post-retention verification;
- JIT support grant subject/expiry enforcement and attributable reads.

`PortalRouteTests` renders every required route, verifies CSP/framing/content
headers, anti-forgery tokens, and absence of browser bearer-token storage.

```powershell
./build.ps1 -Target ValidateDesign
$dotnet = ./eng/bootstrap-dotnet.ps1
& $dotnet test tests/RemoteSupport.Server.IntegrationTests -c Release
& $dotnet test tests/RemoteSupport.AdminPortal.Tests -c Release
```

## Production boundary

The PostgreSQL migration enables tenant RLS and the repository calls
`set_config('app.tenant_id', ..., true)` within each serializable transaction.
Production uses a separate migration owner and a non-owner application role so
table ownership cannot bypass RLS. Export storage must point to an encrypted,
region-approved volume or object-storage mount with backup and deletion policy.

Goal 10 does not enable unattended access. Device credentials introduced here
authorize enrollment inventory only; managed command delivery, credential
renewal/rotation and service lifecycle remain Goal 13. SIEM webhooks are the
P1 enterprise-post-GA item FR-ADM-005 and are not claimed complete by this goal.
