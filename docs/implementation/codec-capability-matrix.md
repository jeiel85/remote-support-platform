# Codec Capability Matrix

Validated locally on 2026-07-01 with Windows 11 Pro build 26200, AMD Radeon 780M driver 32.0.21028.2002, and NVIDIA GeForce RTX 5060 Laptop GPU driver 32.0.15.9595.

| Capability | Implementation | Local result | Remaining release evidence |
|---|---|---|---|
| H.264 discovery | Media Foundation H.264/NV12 `MFTEnumEx` discovery; no vendor-name assumptions | Three hardware registrations and Microsoft `H264 Encoder MFT` discovered | De-duplicate registrations by adapter with `MFTEnum2` where the SDK exposes adapter-LUID filtering |
| Candidate validation | Requested type, low-latency mode, zero B-frames, bitrate/GOP settings, then encoded color bars before selection | AMD candidate accepted and emitted a color-bar access unit | Intel matrix and discrete-NVIDIA adapter-owned runtime |
| GPU conversion/scaling | D3D11 Video Processor converts captured BGRA8 texture to scaled NV12 | 640x360 software path and 1280x720 hardware path passed | HDR/tone-map and colorimetry corpus |
| Hardware surface input | `IMFDXGIDeviceManager` plus NV12 texture carrying `D3D11_BIND_VIDEO_ENCODER` | AMD hardware encoder produced 90/90 submitted 720p frames with software fallback disabled | Adapter loss/recovery and multi-vendor long soak |
| Software fallback | Microsoft H.264 MFT with GPU-converted NV12 copied to bounded system memory | Injected hardware-selection failure emitted a diagnostic fallback event and completed in under two seconds | OS N/codec-missing diagnostics |
| Decoder | Microsoft H.264 decoder, Annex-B input, NV12-to-BGRA8 output texture | Start, explicit keyframe, decoder reset, delta rejection, and 320x192→160x96 change passed | Hardware decoder preference and corrupt-stream corpus |
| Dynamic control | CodecAPI rate/keyframe control; transform recreation for atomic resolution/profile change | 1,000→600 kbps and 30→20 fps update, forced keyframe, and resize passed | WebRTC estimator integration under protected network impairment lab |

Discovery is not treated as proof of usability. The hardware row is marked passed only because selection used `allow_software_fallback = 0` and completed real frame submission; rejected registrations remain discovery-only.
