# File Transfer Protocol

## 1. Security posture

File transfer is a consented content-delivery feature, not a remote execution feature.

- No directory traversal.
- No automatic opening or execution.
- Destination defaults to a dedicated received-files folder.
- Executable/script/archive types can be blocked by tenant policy.
- Received files receive Mark-of-the-Web/Zone information where Windows supports it.
- Local antivirus/attachment handling is invoked when available.

## 2. Manifest

```text
transferId: UUID
name: display filename only
size: uint64
sha256: 32 bytes
mimeHint: optional, untrusted
chunkSize: negotiated, bounded
chunkCount
sourceRole
destinationRole
createdAt
```

The receiver normalizes the filename, chooses the actual path and returns a transfer grant. Sender cannot provide an absolute destination path.

## 3. Chunk protocol

```text
FileChunk
- transferId
- chunkIndex
- offset
- data
- chunkSha256
```

Receiver writes to a temporary file opened with safe flags. It verifies chunk bounds, total size and final hash, then atomically renames into the chosen destination.

## 4. Backpressure

- Maximum in-flight bytes per transfer.
- Maximum concurrent transfers per session.
- DataChannel buffered amount high/low water marks.
- Pause when disk free space or policy threshold is insufficient.
- Quotas enforced at both peers and control plane metadata layer.

## 5. Resume

Receiver returns a compact set/range of verified chunks. Resume grant is bound to session, transfer ID, hashes and expiry. Changed manifest starts a new transfer.

## 6. Audit

Audit only:

- transfer direction;
- normalized filename or policy-dependent redacted filename;
- size;
- hash optionally retained only when justified;
- result and policy decision;
- participants and session.

Never log file bytes.
