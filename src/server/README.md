# Control Plane Server

The Goal 06 host is `RemoteSupport.Server`. Release startup requires:

- `ConnectionStrings__ControlPlane` for PostgreSQL;
- `ControlPlane__LookupKeyBase64` and
  `ControlPlane__TokenSigningKeyBase64`, each at least 32 random bytes;
- `Oidc__Authority` and `Oidc__Audience` for the operator identity provider.
- `ControlPlane__SignalingPublicUrl`, an absolute `wss` endpoint;
- `ControlPlane__TurnSharedSecretBase64` and
  `ControlPlane__TurnMeteringKeyBase64`, each containing at least 32 random
  bytes as base64 text;
- `ControlPlane__TurnRegion` and indexed `ControlPlane__TurnUrls` entries for
  TURN/UDP, TURN/TCP, and TURN/TLS.
- `Governance__ExportDirectory`, an encrypted region-approved filesystem or
  object-storage mount used for short-lived tenant export artifacts.
- `UpdatePublication__Directory`, an absolute protected directory containing
  `roots/root-N.json` and `manifests/PRODUCT.CHANNEL.ARCH.json` outputs from the
  protected release pipeline;
- `Observability__MetricsBearerToken`, a high-entropy secret supplied only to
  the Prometheus scraper for `/internal/metrics`.

The server applies immutable SQL files from `Migrations` before accepting
traffic. The attended module writes its state, hash-chained audit record and
outbox record in one serializable transaction. It never logs or stores raw
support codes, bootstrap/peer tokens, proof nonces, signatures or session
content. Create retries require `Idempotency-Key`; server-keyed derivation
reconstructs the original one-time response while persistence retains only
request and lookup hashes.

Local deterministic testing:

```powershell
$dotnet = ./eng/bootstrap-dotnet.ps1
& $dotnet test tests/RemoteSupport.Server.IntegrationTests -c Debug
```

The header-based OIDC fixture and in-memory store exist only in Debug and only
activate in the `Testing` environment. They are absent from Release builds.

Peer credential endpoints require RFC 9449 DPoP. Signaling tickets are
single-use and expire within 60 seconds; TURN credentials expire within ten
minutes and are additionally bounded by peer/session expiry. `/internal/v1/turn-usage`
is for the coturn collector only and must also be restricted to the management
network; it requires a timestamped HMAC body signature.

Goal 10 tenant endpoints require authenticated OIDC plus `X-Tenant-Id`. The
middleware resolves an active stored membership and ignores client role claims.
PostgreSQL governance transactions set `app.tenant_id` locally before accessing
RLS-protected aggregate and audit rows. Production must run the API through a
non-owner database role; the migration owner is separate.

The governance worker completes privacy exports, expires one-time artifacts and
invitations, advances closure cooling-off workflows, revokes tenant access on
completion and creates audit retention checkpoints. Raw invitation, enrollment,
device credential and export-download secrets are never persisted.

Update clients receive only the next sequential root and a newer manifest
matching the requested product/channel/architecture. Publication storage is not
a trust anchor: clients independently enforce metadata thresholds, expiry,
sequence, artifact hash and Authenticode publisher. Metrics use route templates
and bounded status/error labels; a missing or wrong scrape token receives 404.
