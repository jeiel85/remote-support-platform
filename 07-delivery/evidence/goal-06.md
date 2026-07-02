# Goal 06 Evidence

Date: 2026-07-02. Environment: ASP.NET Core/.NET 10, PostgreSQL migration
contract, Windows x64 test host.

Automated HTTP integration proves:

- a sample of 100,000 generated 50-bit Crockford codes is format-valid and
  collision-free;
- the required idempotency key returns the same secret-bearing create response
  without persisting raw secrets, while key reuse with another request fails;
- code-only access cannot cross the OIDC operator boundary;
- authenticated resolution binds verified operator/tenant display identity,
  requested scopes and an authorized ephemeral key into the host consent view;
- expired and unknown codes have the same generic response, while subject,
  tenant, edge, prefix and global fixed-window controls bound guessing;
- simultaneous consent decisions with one `If-Match` version and nonce yield
  exactly one success;
- P-256 low-S consent and peer proofs are domain-separated, key-bound and
  single-use;
- host and operator receive different short-lived peer tokens containing
  session, peer, role, scopes, permission revision, transport epoch and
  `cnf.jkt` binding;
- every lifecycle mutation appends a linked audit hash and one outbox record;
  serialized persistence contains no raw support code or bootstrap token.

Debug server integration tests: 4 passed. Debug and Release server builds and
the WPF consent surface build with zero warnings and zero errors. A live
PostgreSQL container is not available in this workspace; migration application
is therefore statically verified here and remains a deployment-lab check.
