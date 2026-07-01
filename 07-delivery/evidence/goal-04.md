# Goal 04 Evidence

Date: 2026-07-02. Environment: Windows x64, LLVM-MinGW, Debug.

Automated evidence:

- `AT-FR-NET-001-lan-webrtc`: two native peers exchange SDP/ICE, establish
  ICE/DTLS/SCTP, validate reciprocal bindings against the actual SHA-256 DTLS
  fingerprints, exchange capabilities, open an application channel and deliver
  a payload.
- `AT-SEC-TRN-001`: authorization-context, scope, epoch, fingerprint
  substitution and binding replay cases fail closed.
- Unsupported protocol major and an oversized application message fail with
  stable protocol/status results.
- Release build passed all 11 applicable native tests; Debug passed all 14,
  including the three fault-injection-only transport mutations. Managed build
  completed with zero warnings and zero errors.
- 100 consecutive actual ICE/DTLS/SCTP connect, reciprocal-binding and clean
  disconnect cycles passed in Debug in 50.302 seconds. The machine-readable
  report is `artifacts/evidence/goal-04/lifecycle.json`.

Commands:

```powershell
./build.ps1 -Target Test -Configuration Debug
./.tools/python/Scripts/ctest.exe --test-dir artifacts/native/Debug -R 'AT-(FR-NET|SEC-TRN)' --output-on-failure
```

Packet-capture evidence is produced by `eng/capture-goal04-packets.ps1`. The
current non-elevated development session was denied by Packet Monitor with
Windows error 5 before capture began; this is an explicit lab-evidence gap, not
a bypass. Run the script from elevated PowerShell on each clean-machine LAN
qualification host and retain its `lan-session.pcapng` with the release record.

The required repeated lifecycle run is deterministic and emits a machine-readable
report:

```powershell
./eng/test-goal04-lifecycle.ps1 -Cycles 100 -Configuration Release
```
