# Security Architecture

## 1. Security objectives

- only an authorized operator can request access;
- host consent and policy define exact session capabilities;
- signaling or relay compromise must not expose session content;
- device/account compromise can be rapidly revoked;
- update compromise requires multiple independent control failures;
- all privileged decisions produce useful, privacy-minimized audit evidence;
- the product is visibly user-controlled and resists conversion into a stealth tool.

## 2. Identity layers

| Identity | Credential | Stored where |
|---|---|---|
| Operator user | OIDC session, MFA claim | identity provider + protected local token cache |
| Tenant membership | server-side role/group state | PostgreSQL |
| Portable host session | ephemeral key + short-lived host token | memory only |
| Installed device | device key pair/certificate | private key on endpoint, public identity in control plane |
| Service/agent IPC peer | local challenge + OS ACL/SID | endpoint only |
| Workload | workload identity or short-lived secret | runtime secret system |

## 3. Session authentication

1. Host creates ephemeral key and pending session.
2. Operator authenticates and resolves code.
3. Host receives verified operator/tenant identity and requested scopes.
4. Host signs consent and selected scopes.
5. Server issues peer-specific authorization.
6. Peers exchange signed ephemeral handshake material through signaling.
7. Session transcript binds session ID, peer identities, scopes and WebRTC fingerprints.
8. Optional short authentication string is displayed for high-assurance verification.

This prevents the support code from being treated as a password and reduces signaling-layer impersonation risk.

## 4. Cryptography profile

Exact algorithms are finalized by security review and platform requirements. Baseline:

- TLS 1.2 minimum, TLS 1.3 preferred for control plane.
- WebRTC DTLS-SRTP/SCTP security profile from the pinned native stack.
- SHA-256 or stronger for artifact/file hashes.
- AEAD for any application-layer encrypted envelope.
- Device signing keys protected through Windows cryptographic storage where practical.
- Random values from OS cryptographic RNG.
- Key derivation through a standard HKDF construction.

Never implement custom cryptographic primitives.

## 5. Authorization

Authorization checks occur:

- when resolving a code;
- when requesting managed-device access;
- when issuing peer/TURN credentials;
- when adding scopes;
- before every local privileged broker action;
- on policy/account/device changes during a live session.

The host endpoint independently enforces session scopes. Server approval alone is insufficient to inject input or transfer content.

## 6. Update security

- signed binaries;
- signed update metadata;
- hash verification;
- anti-rollback sequence;
- channel and architecture binding;
- staged rollout;
- independent signing audit;
- emergency key rotation.

## 7. Data protection

- minimize metadata;
- encrypt in transit and at rest;
- restrict production access;
- no content logging;
- retention jobs and deletion evidence;
- backup access and restore auditing;
- support diagnostics require user action and expiration.

## Cryptographic decision and transport binding

Host consent and managed-host decisions are signed according to `../02-protocol/canonicalization-and-signatures.md`; bearer possession alone is insufficient for approval. WebRTC content is gated on reciprocal signed transport binding. This prevents a compromised or misrouted signaling path from silently substituting a DTLS endpoint without detection by the authorized peers.

## Sender-constrained API credentials

Peer and device access tokens are DPoP-bound under RFC 9449. API middleware reconstructs the public target URI only from an allowlisted trusted-proxy chain, validates `htm`, `htu`, `iat`, `jti`, `ath`, nonce and `cnf.jkt`, and places accepted proof IDs in a distributed replay cache. A plain bearer presentation is rejected. Bootstrap credentials are not general access tokens and can only request their bounded challenge/decision operations.
