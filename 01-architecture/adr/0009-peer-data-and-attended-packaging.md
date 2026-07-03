# ADR 0009 — Peer Data Safety and Attended Product Packaging

Status: accepted for Goal 08/09 implementation on 2026-07-02.

## Decision

Clipboard, chat and file bytes remain on authenticated WebRTC DataChannels and
are never sent to the control plane. The managed product layer uses the fixed
`RSP1` framing contract after the native transport has completed reciprocal
peer binding and capability negotiation. Every handler rechecks the current
permission revision; native transport readiness is not treated as continuing
authorization.

The reserved native control channel owns permission-state sequencing. ABI 1.4
accepts only a host-originated next revision whose active and revoked scopes
exactly partition the prior set. The host applies the reduction and releases
input locally first; the peer updates its gate and UI without closing unrelated
channels.

Clipboard v1 is explicit, text-only and offer/decision/content based. Content
hashes and revisions suppress feedback loops. File receive paths are selected
locally under a dedicated directory. Chunks are bounded, individually hashed,
written to a fixed temporary file and promoted only after the complete size and
SHA-256 match. Resume metadata is session/transfer/manifest/expiry bound and
existing chunks are rehashed before reuse. Windows Attachment Execution
Services and Mark-of-the-Web are applied where the filesystem supports them.

The attended release contains only a portable per-user Agent, an installed
per-user Operator Console and a payload-bearing per-user setup executable. It
contains no service, driver, scheduled task, device enrollment or unattended
code path. The setup transaction stages and verifies every declared file,
blocks downgrade, keeps one rollback directory during the atomic swap, repairs
the current sequence, and removes only its product directory on uninstall.

The x64 package is self-contained. The same packaging target accepts `arm64`
only when a matching protected-worker native runtime exists; it fails instead
of placing an x64 DLL in an Arm64 package. Production packaging also fails
closed when signing is required but no managed signing certificate is
available.

## Consequences

- Clipboard and file contents never enter product logging or diagnostic bundle
  schemas.
- A sender cannot choose an absolute destination, ADS name or traversal path.
- Large files use one bounded chunk plus native DataChannel buffering rather
  than whole-file memory.
- Operator OIDC uses system-browser authorization code with PKCE and keeps no
  refresh token or persistent device identity.
- Peer authorization now returns the remote authorized public key and a shared
  domain-separated authorization-context hash. These are public binding data,
  not bearer credentials.
- Root/manifest update metadata is verified with threshold Ed25519 signatures,
  product/channel/architecture/expiry/sequence/rollout binding and rollback
  protection before an artifact can be selected.

## Rejected alternatives

- Server-mediated clipboard/chat/file storage was rejected for privacy, cost
  and breach-radius reasons.
- Sender-provided destination paths and automatic file opening were rejected.
- Rich clipboard formats were deferred because they materially expand parser
  and content-handler attack surface.
- Shipping an Arm64-labeled package containing the x64 native DLL was rejected.
- A test or self-signed certificate is not accepted as production publisher
  evidence.
