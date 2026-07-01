# Windows Compatibility Matrix

The release manager maintains exact supported builds. Test at minimum:

## 1. OS dimensions

- Windows 10 supported releases still in Microsoft support at GA date.
- Windows 11 supported releases, Home/Pro/Enterprise.
- x64 mandatory; Arm64 separately gated.
- local account, Microsoft account and domain/Azure/Entra-managed profiles where applicable.
- standard user and administrator sessions.
- fast user switching and RDP-present scenarios.

## 2. Display dimensions

- 1/2/3 monitors;
- negative virtual origins;
- mixed resolutions: 1080p, 1440p, 4K;
- mixed DPI: 100/125/150/200%;
- portrait rotation;
- HDR on/off;
- display hot-plug and GPU reset;
- laptop docking/undocking.

## 3. GPU/encoder dimensions

- Intel integrated;
- NVIDIA discrete;
- AMD integrated/discrete;
- Microsoft Basic Display/software fallback;
- hybrid graphics and adapter changes;
- hardware encoder enabled/disabled/failure.

## 4. Network dimensions

- direct public/normal home NAT;
- symmetric/restrictive NAT;
- UDP blocked;
- TURN UDP;
- TURN TCP/TLS;
- HTTP proxy environment where supported;
- 1–10% packet loss;
- 50–500 ms RTT;
- bandwidth limits and rapid change;
- IPv4, IPv6 and dual stack.

## 5. Security software

- Microsoft Defender default and controlled folder access scenarios;
- representative enterprise EDR products through partner/lab programs;
- Smart App Control test devices;
- application control/allowlisting enterprise environments.

## 6. Result statuses

- Supported.
- Supported with documented limitation.
- Preview.
- Unsupported.

No capability is marketed as supported until it passes the matrix with a documented result.

## Goal 05 implementation result

Automated coordinate tests cover negative origins, 1080p/4K virtual extents,
portrait post-rotation dimensions, corners, center, outside bounds, renderer
letterboxing, stretch, zoom, pan and explicit 150/200% per-axis logical-to-
physical conversion. Debug no-op injection tests cover view-only scope denial,
pointer/button and scan-code state, Korean/ASCII Unicode input, stale topology,
strict sequence rejection, revocation/emergency disable and release-all.

Status: implementation-supported; physical compatibility qualification remains
required for Windows 10/11, KO/EN/JA IME, mixed-DPI hardware, elevated foreground
applications, UAC secure desktop, RDP transitions and vendor security software.
Secure desktop is intentionally unsupported. Higher-integrity targets are
supported only by the separately authorized installed-mode privileged agent.
