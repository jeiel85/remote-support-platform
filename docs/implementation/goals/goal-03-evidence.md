# Goal 03 Evidence

## Delivered

- Media Foundation H.264 encoder discovery, color-bar candidate validation, hardware preference, explicit diagnostic software fallback, and a Debug-only deterministic hardware-failure hook.
- D3D11 Video Processor BGRA8-to-NV12 conversion and scaling. D3D11-aware hardware MFTs receive the NV12 DXGI surface directly; the software MFT receives a bounded mapped copy.
- Async-MFT event handling with one `ProcessOutput` per `METransformHaveOutput`; synchronous transforms drain until they require input.
- Low-latency mode, zero B-frames, bounded GOP, dynamic bitrate/frame rate, requested keyframes, atomic resolution/profile recreation, Annex-B access units, decoder reset, and pre-keyframe delta rejection.
- `RemoteSupport.Media` adaptation input/output contract, TEXT/BALANCED/MOTION policy, immediate downshift, three-sample recovery hysteresis, and a bounded latest-frame buffer.
- Runnable capability probe, software encode/decode benchmark, roundtrip/failure-injection test, and hardware-only encoder test.

## Automated and local hardware results

| Test | Result |
|---|---|
| `AT-FR-SCR-005-codec-capabilities` | 3 hardware registrations and 1 software encoder discovered |
| `AT-FR-SCR-005-codec-roundtrip` | Start/keyframe/reset/delta rejection/rate change/resize passed; 2 keyframes and 0 failures |
| Injected hardware failure | One explicit hardware→software callback during creation; 34.0 ms (limit 2,000 ms) |
| 720p30 hardware encode | AMD hardware MFT, fallback disabled: creation 311.7 ms, 90 frames, 1,569,788 bytes, 0 failures |
| Deterministic 640x360 software benchmark | 151 idle + 150 motion encoded frames; 301 decoded frames; 0 timestamp regressions/failures |
| Encode latency | p50 1.0 ms; p95 1.5 ms |
| Capture-to-decode latency | p50 10.9 ms; p95 15.0 ms |
| Idle vs motion bitrate | 252,733 bps vs 2,272,061 bps; idle/motion ratio 0.1 |
| Post-warmup working-set delta | +2,478,080 bytes across the 10-second measured interval |
| Managed adaptation tests | 4 tests: impairment/recovery, profile distinction, bounded stale drop, network/resource sweep |

The post-warmup memory result demonstrates stable short-run allocation, not the required long-soak leak qualification.

## Commands

```powershell
./build.ps1 -Target Test -Configuration Debug
./build.ps1 -Target HardwareTest -Configuration Debug
./artifacts/native/Debug/codec_capability_probe.exe
./artifacts/native/Debug/codec_benchmark.exe ./artifacts/benchmarks/goal-03-codec.json
```

`HardwareTest` requires an unlocked interactive desktop; DXGI correctly returns `CAPTURE_SECURE_DESKTOP_UNAVAILABLE` at the Windows lock screen.

## External evidence still required for release qualification

The 60-minute encode/decode soak, adapter-loss injection, Intel and discrete-NVIDIA adapter-owned runs, 1080p/4K latency and GPU-memory matrix, WebRTC network-impairment sweep, corrupt-stream corpus, and independently reviewed text/chart/video visual-quality corpus require the protected Windows lab. These release gates are kept explicit rather than inferred from a single development laptop.
