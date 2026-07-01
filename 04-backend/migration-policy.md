# Database Migration Policy

## Ownership

Each backend module owns its tables and migrations. Cross-module foreign keys are allowed only when documented; modules do not issue ad-hoc queries against another module's tables.

## Rules

- PostgreSQL migrations are immutable after release.
- Every migration is forward-compatible with the previous server version during rolling deployment.
- Destructive changes use expand/migrate/contract over separate releases.
- Long index creation uses online/concurrent strategy where PostgreSQL permits it.
- Backfills are bounded, resumable, observable, and idempotent.
- Application code tolerates both old and new representations during the transition window.
- Production migration jobs use a dedicated role and explicit approval.
- Schema drift is checked in CI and at deployment.

## Tenant isolation

Application repositories require an explicit tenant context. Defense-in-depth RLS uses the transaction-local setting `app.tenant_id` for tenant requests. Platform workers use separately authorized roles and must never inherit end-user tenant context.

Every tenant isolation integration test runs the same query/action with:

- valid tenant/resource;
- different tenant/resource ID;
- absent tenant context;
- elevated worker role;
- malformed or reused transaction context.

## Recovery

Before a risky migration:

- verify point-in-time recovery;
- capture migration plan and estimated locks;
- test on production-shaped data;
- define abort thresholds;
- ensure the previous application remains operable or document forward-fix procedure.
