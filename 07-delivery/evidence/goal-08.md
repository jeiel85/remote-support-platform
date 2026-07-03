# Goal 08 evidence

Date: 2026-07-03. Environment: Windows x64, .NET 10.0.9, NTFS temporary test
volumes, pinned native transport from Goals 02–07.

Implemented modules:

- strict 24-byte `RSP1` frame encoder/parser, channel/type binding, negotiated
  allocation limits, sequence replay rejection and pre-content session gate;
- directional text clipboard offers, explicit acceptance, strict UTF-8 size
  and SHA-256 verification, revision/hash loop suppression and immediate scope
  recheck;
- peer chat with bounded UTF-8, scope recheck, stable IDs, acknowledgements and
  duplicate suppression;
- file policy, filename FormKC normalization, traversal/root/ADS/control/
  reserved-name rejection, locally selected collision-safe destination,
  extension/size/concurrency/free-space controls and cancellation;
- bounded async chunk reader, native backpressure retry, per-chunk hash,
  sparse temporary receive file, content-free resume state, stored-chunk
  revalidation, final streaming SHA-256 and atomic rename;
- Windows Attachment Execution Services inspection plus Zone.Identifier on
  NTFS/ReFS, with no automatic open or execute path;
- metadata-only audit records and allowlisted support bundles;
- reusable Agent/Console peer controllers over bound native DataChannels;
- host-only monotonic permission reduction over ABI 1.4, immediate local input
  release, peer UI reconciliation and cancellation of affected file transfers.

Automated evidence maps to AT-FR-DAT-001 through AT-FR-DAT-007,
AT-FR-CTL-001/002 and AT-NFR-SEC-005/011. Unit coverage includes scope grant and
revocation, unsupported clipboard types, content mismatch, echo suppression,
chat idempotency, traversal/absolute/ADS/reserved names, atomic final hash
failure, resume revalidation, bounded sender chunks, support-bundle exclusion
and direct native bound peer-data exchange. The standalone deterministic fuzz
target executes malformed frames without an unhandled parser failure.

```powershell
./build.ps1 -Target Test
./build.ps1 -Target IntegrationTest
$dotnet = ./eng/bootstrap-dotnet.ps1
& $dotnet run --project tools/fuzz/RemoteSupport.ProtocolFuzz -c Release -- --iterations 100000
```

The Windows attachment test uses a fake safety adapter for deterministic CI;
the release implementation invokes the OS adapter and the physical
Defender/third-party AV matrix remains part of Goal 09 promotion evidence. No
test claims that external vendor qualification ran on this workstation.
