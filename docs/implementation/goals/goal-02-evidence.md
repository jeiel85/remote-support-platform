# Goal 02 Evidence

## Delivered

- ABI 1.1 runtime, handle lifecycle, prefix-compatible structs, stable status/error callbacks, and one D3D11 device per runtime.
- DisplayConfig enumeration with monitor path IDs, bounds (including negative origins), rotation, DPI, adapter LUID, primary flag, and monotonic topology generation.
- DXGI Desktop Duplication capture with selected-output validation, bounded acquisition timeout, target-FPS limiting, access-loss retry budget, and independent cursor metadata.
- Windows.Graphics.Capture display/window backend using a free-threaded frame pool; content-size changes recreate the pool and advance topology generation.
- Deterministic BGRA8 moving-grid source with injected topology change and bounded dynamic texture allocation.
- D3D11/DXGI swap chain renderer using Direct2D texture composition, FIT/ACTUAL_SIZE/STRETCH transforms, pan/zoom validation, cursor shape cache, and display-origin-aware cursor mapping.
- WPF local viewer with a child HWND render surface, DXGI/WGC/synthetic selection, display discovery, and source-generated C ABI calls.

## Automated and local hardware results

| Test | Result |
|---|---|
| C11 and C++20 ABI compile | Passed |
| Synthetic lifecycle, timestamp monotonicity, injected topology generation | Passed |
| Hidden-HWND D3D11/Direct2D renderer | Passed |
| Primary-display DXGI capture and cursor callback | Passed |
| Primary-display WGC capture | Passed |
| WPF hidden startup | Passed |
| 1080p synthetic, 10.027 s | 601 frames, 59.941 fps, +16,973,824 bytes working set, 0 timestamp regressions |
| 4K synthetic, 5.005 s | 300 frames, 59.938 fps, +66,764,800 bytes working set, 0 timestamp regressions |
| DXGI primary display, 5.016 s | 241 frames, 48.042 fps after 60 fps cap, +434,176 bytes working set, 0 timestamp regressions |
| WGC primary display, 10.019 s | 477 frames, 47.609 fps, +3,616,768 bytes working set, 0 timestamp regressions |

The large initial synthetic allocation discovered during benchmarking was corrected by replacing per-frame `UpdateSubresource` staging with a bounded dynamic texture and `WRITE_DISCARD`; the reported numbers are after that correction.

## Commands

```powershell
./build.ps1 -Target Test -Configuration Release
./build.ps1 -Target HardwareTest -Configuration Release
./artifacts/native/Release/capture_benchmark.exe synthetic 10 ./artifacts/performance/goal02-synthetic-1080p.json
./artifacts/native/Release/capture_benchmark.exe synthetic4k 5 ./artifacts/performance/goal02-synthetic-4k.json
```

## External evidence still required for release qualification

The 60-minute capture/render soak, 1,000-handle Application Verifier/sanitizer run, monitor hot-plug/dock/rotation/HDR corpus, cursor visual corpus, multi-vendor matrix, and visible WPF image comparison require the protected Windows hardware lab. The harnesses and fault path are present, but these results are not fabricated from a single development laptop.
