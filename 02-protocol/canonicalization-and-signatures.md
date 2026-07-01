# Canonicalization and Signature Contract

## 1. Common rules

All signed JSON uses UTF-8, RFC 8785 JSON Canonicalization Scheme (JCS), and base64url without padding. Endpoint identity proofs support Ed25519 and ECDSA P-256 with SHA-256; P-256 signatures use fixed-width IEEE P1363 `r || s` encoding and verifiers require a low-S value. Update metadata uses Ed25519 in the v1 trust root unless a future root version adds another algorithm. A verifier rejects duplicate JSON object names, invalid UTF-8, unknown canonicalization versions, expired nonces, a reused nonce, a key not active for the claimed subject, and any signature whose domain separator does not match the operation.

The signed byte sequence is:

```text
ASCII(domain-separator) || 0x00 || JCS(payload)
```

Timestamps in signed payloads are RFC 3339 UTC with `Z`. UUID text is lowercase canonical form. Scope arrays are sorted by ASCII code point before JCS. JWK thumbprints use RFC 7638 SHA-256 in unpadded base64url. Other SHA-256 fields use the representation declared by their schema—normally lowercase hexadecimal in JSON and raw 32-byte values in Protobuf.

## 2. Consent decision

Domain: `RSP-CONSENT-DECISION-V1`.

Payload fields: `sessionId`, `consentRequestId`, `consentNonce`, `approved`, sorted `grantedScopes`, `hostPeerId`, `hostEphemeralKeyThumbprint`, `stateVersion`, and `expiresAt`. The nonce is single-use and stored only as a keyed or cryptographic digest. Approval is invalid if the displayed operator identity or requested scopes differ from the signed payload.

## 3. Managed-host decision

Domain: `RSP-MANAGED-HOST-DECISION-V1`.

Payload fields: `tenantId`, `deviceId`, `sessionId`, `sessionType`, `operatorUserId`, `policyDecisionHash`, `consentNonce`, `approved`, sorted `grantedScopes`, `hostEphemeralPublicKey`, `deviceAuthorizationVersion`, `stateVersion`, and `expiresAt`. The active device key signs the decision. The server issues a host bootstrap credential only after verification.

## 4. Device credential refresh and key rotation

Domains: `RSP-DEVICE-CREDENTIAL-V1` and `RSP-DEVICE-KEY-ROTATION-V1`.

A `CREDENTIAL_REFRESH` challenge signs `deviceId`, `challengeId`, `nonce`, `purpose`, `keyVersion`, `authorizationVersion`, and `expiresAt`. A `KEY_ROTATION` challenge additionally binds the new public key and the current credential subject. Challenge purpose and canonicalization version must match the called operation. A successful rotation retires the old key only after the new key is proven usable; emergency revocation invalidates both device credentials and pending session requests.

## 5. WebRTC transport binding

Domain: `RSP-TRANSPORT-BINDING-V1`.

Each peer signs the Protobuf-equivalent canonical payload using the algorithm registered with its authorized peer key containing `sessionId`, `senderPeerId`, `senderRole`, `transportEpoch`, `permissionRevision`, sorted `grantedScopes`, local and remote DTLS certificate fingerprints, `authorizationContextSha256`, and `bindingId`. Both peers exchange and verify `TransportBinding` and `TransportBindingAck` before enabling video, input, clipboard, chat, or file channels. A reconnect or ICE restart that changes the epoch or either fingerprint requires a new binding.

## 6. Update metadata

Update root and manifest signed objects use JCS. Domains are `RSP-UPDATE-ROOT-V1` and `RSP-UPDATE-MANIFEST-V1`. The client embeds a bootstrap root. Root rotation requires threshold signatures valid under both the currently trusted root role and the proposed new root role. The manifest is accepted only when its `rootVersion`, product, channel, architecture, expiry, release sequence, minimum allowed sequence, artifact hash, and Authenticode signer match local policy. The baseline does not support delta updates.

## 7. Audit hash chain

Domain: `RSP-AUDIT-EVENT-V1`. The event hash is SHA-256 over the domain-separated JCS object containing all persisted event fields except `eventHash`, plus `previousHash` and `chainSequence`. Optional fields are represented as explicit JSON `null`; database-specific JSON ordering is never used as the hash input. A daily sealed checkpoint is signed by the audit-seal key and exported to independently controlled storage.

## 8. HTTP DPoP proof

Peer and device access-token requests implement RFC 9449. The access token carries `cnf.jkt`; the proof JWT uses the same public key and includes `typ=dpop+jwt`, `alg`, public `jwk`, `jti`, `htm`, normalized `htu`, `iat`, `ath`, and the latest server nonce when challenged. The verifier applies a bounded clock skew, short replay-cache TTL at least as long as the acceptance window, trusted-proxy URI reconstruction rules, and rejection on duplicate `jti` for the same key.
