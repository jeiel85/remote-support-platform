# Capture and Render Capability Matrix

Validated locally on 2026-07-01 with Windows 11 Pro build 26200, AMD Radeon 780M driver 32.0.21028.2002, and NVIDIA GeForce RTX 5060 Laptop GPU driver 32.0.15.9595.

| Capability | Implementation | Local result | Remaining release evidence |
|---|---|---|---|
| Monitor capture | DXGI Desktop Duplication on the runtime adapter | Hardware smoke passed; primary display frames and cursor callbacks received | Intel/NVIDIA/AMD matrix, adapter switching, secure desktop, RDP transition |
| Display/window capture | Windows.Graphics.Capture via C++/WinRT free-threaded frame pool | Display hardware smoke passed; resize triggers frame-pool recreation and topology generation | Window picker integration, protected content, close/resize visual corpus |
| Synthetic capture | D3D11 BGRA8 dynamic texture, 16–8192 px, 1–120 fps | 1080p60 and 4K60 samples passed with monotonic timestamps | Long-duration soak on protected lab worker |
| Display discovery | DisplayConfig monitor path, friendly name, GDI output, LUID, bounds, rotation, DPI | Primary display resolved and used by DXGI/WGC tests | Hot-plug, dock/undock, negative origin, mixed DPI/HDR matrix |
| Cursor metadata | Independent Win32 cursor callback with hotspot, visibility, position, shape cache | Cursor callbacks observed during DXGI hardware smoke | Arrow/I-beam/resize/custom alpha/hidden visual comparison |
| Rendering | D3D11 swap chain with Direct2D texture/cursor composition | Hidden-HWND synthetic render test passed; FIT and bounded transform API exercised | Visible WPF visual reference tests for all modes and pointer mapping |
| Managed host | WPF `HwndHost` local viewer with source/display selection | Build and hidden startup smoke passed | UI automation and accessibility review |

“Passed” here means the listed local evidence, not commercial support. The broader compatibility matrix remains a release gate.

