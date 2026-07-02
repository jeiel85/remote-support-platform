# Control Plane Server

The Goal 06 host is `RemoteSupport.Server`. Release startup requires:

- `ConnectionStrings__ControlPlane` for PostgreSQL;
- `ControlPlane__LookupKeyBase64` and
  `ControlPlane__TokenSigningKeyBase64`, each at least 32 random bytes;
- `Oidc__Authority` and `Oidc__Audience` for the operator identity provider.

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
