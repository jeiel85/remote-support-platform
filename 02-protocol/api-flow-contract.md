# Control-Plane API Flow Contract

## Credential classes

| Credential | Holder | Purpose | Maximum default lifetime | Reusable |
|---|---|---|---:|---|
| Host bootstrap token | Portable/installed host | pending event, signed decision, peer exchange | 10 minutes | bounded/single-purpose |
| Operator bootstrap token | Operator | wait for host decision and peer exchange | 5 minutes | bounded/single-purpose |
| Device credential | Installed Service | poll, heartbeat, managed decision, key lifecycle | 24 hours | renewable by active-key proof |
| Peer token | Host/operator peer | scoped session control, signaling/TURN exchange | 15 minutes | renewable while valid |
| Signaling ticket | One peer | one signaling connection | 60 seconds | single use |
| TURN credential | One peer/session/region | relay allocation | 10 minutes | bounded |
| Reconnect grant | Same peer and epoch successor | transport restart | 90 seconds | single use |
| Reboot reconnect grant | installed host/operator pair | bounded post-reboot resume | 10 minutes | single use |

Server policy may shorten lifetimes but cannot exceed hard maximums without a reviewed contract version. Credentials carry or reference tenant, subject, audience, session, peer, role, scopes, permission revision, transport epoch, key thumbprint, authorization version, issue/expiry and revocation state.

## Human support code

The attended code contains ten Crockford Base32 symbols grouped `XXXXX-XXXXX` and 50 bits of CSPRNG entropy. It is only a locator. Operator authentication, signed host consent, bootstrap credentials, peer key proof and transport binding remain mandatory. The server stores `HMAC-SHA-256(canonical-code, versioned lookup key)`, applies account/tenant/edge/prefix/global abuse limits, returns generic errors and expires codes after 10 minutes by default with a 15-minute hard maximum.

## Canonical proofs

All consent, device, peer and update proofs follow `canonicalization-and-signatures.md`. Nonces are single-use and expire. Public keys are registered before the operation whose proof they verify; a caller cannot supply a replacement verification key inside the same signed request unless the key-rotation contract explicitly binds old and new keys.

## Attended flow

1. Host creates an attended session with a fresh ephemeral public key and receives support code, host peer ID and host bootstrap credential.
2. Authenticated operator resolves the code with requested scopes and fresh operator key. The server returns the operator's bootstrap credential and queues a consent request for the host.
3. Host fetches the consent request, verifies displayed identity/scopes, and signs the canonical consent payload. Approval records only the granted subset.
4. Each peer independently uses its bootstrap credential to request a single-use peer-authorization challenge, then exchanges the challenge ID and key proof for its own scoped peer token. No caller can retrieve the other peer's token.
5. Each peer obtains one-time signaling and short-lived TURN credentials.
6. After DTLS, peers exchange and verify reciprocal signed `TransportBinding`. Only then are media and application channels enabled.

## Managed Host flow

1. Authenticated operator requests `MANAGED_ATTENDED` or, in the later release, `UNATTENDED`. Policy evaluation creates an immutable decision snapshot and `HOST_PENDING` session; the operator receives its peer/bootstrap identity but no host credential.
2. The transaction commits an outbox delivery item. An enrolled device polls with a short-lived device credential bound to active device key and authorization version.
3. The Service/Agent displays local consent/notification when required, generates a fresh host peer key, and signs the managed-host decision over device/session/operator/policy/nonce/scopes/key/version data.
4. The server atomically consumes the nonce, binds the host peer and returns a host bootstrap credential only on approval.
5. Both peers use the same peer-authorization challenge, signaling/TURN and transport-binding flow as attended support.

Delivery is at-least-once. Session ID, state version and single-use nonce make decisions idempotent. Device revocation or authorization-version change invalidates credentials, poll cursors and pending requests.

## Device credential lifecycle

The device requests a challenge for an existing active key version, signs the canonical challenge, and exchanges it for a short-lived device credential. Rotation signs the new key under the current key and requires subsequent proof by the new key before retirement of the old key. Suspected compromise uses revocation and re-enrollment rather than normal rotation.

## Signaling and TURN

A signaling ticket binds socket identity to session, peer, role, epoch, authorized key thumbprint, scopes, permission revision and protocol range. TURN credentials are issued only to a current authorized peer for a current session/epoch/region; long-term TURN secrets never reach clients. Signaling cannot waive reciprocal transport binding.

## Tenant administration

The Admin Portal uses server-side OIDC/BFF sessions and generated OpenAPI clients for tenant creation/settings, membership and invitation lifecycle, device/policy/audit operations, data exports and tenant closure. State changes require role authorization, optimistic concurrency where applicable, idempotency for retried commands and audit emission.

## Revocation and privacy

Membership suspension/removal, device/key revocation, tenant suspension, session end, policy invalidation and abuse action publish immediate revocation/termination. Cached authorization may not outlive the shorter of token expiry and the documented revocation SLO. Codes, tokens, proofs, SDP credentials, TURN credentials and content are never logged. Cross-tenant denial never reveals resource existence.

## Sender-constrained access tokens

Peer and device access tokens contain an RFC 9449 `cnf.jkt` confirmation thumbprint and are presented with `Authorization: DPoP <access-token>` plus a `DPoP` proof JWT. The API validates method, normalized target URI, issue time, unique `jti`, access-token hash (`ath`), server nonce when required, and proof-key thumbprint. Replayed proofs, bearer-only presentation, proxy-rewritten URI ambiguity and key mismatch are rejected. Operator OIDC tokens remain normal bearer tokens at the BFF/API boundary unless the identity deployment separately enables sender-constrained tokens.
