# Product Risk Register

| ID | Risk | Likelihood | Impact | Primary mitigation | Exit evidence |
|---|---|---:|---:|---|---|
| R-001 | AV/EDR flags remote-control binaries | High | High | visible UX, signed binaries, no obfuscation, vendor false-positive program | lab report across target engines |
| R-002 | TURN bandwidth cost exceeds revenue | Medium | High | direct route preference, adaptive bitrate, quotas, per-tenant metering | load and cost model |
| R-003 | Signaling compromise enables session impersonation | Low/Med | Critical | authenticated peers, mandatory signed DTLS transport binding, optional out-of-band SAS for high-assurance policy | security review and attack test |
| R-004 | Update-chain compromise | Low | Critical | offline/root signing separation, signed metadata, rollback protection, staged rollout | updater red-team test |
| R-005 | UAC/secure-desktop behavior inconsistent | High | High | explicit capability detection, local approval fallback, compatibility gate | Windows matrix results |
| R-006 | Multi-monitor/DPI input mismatch | Medium | High | canonical desktop coordinate space and exhaustive VM/device tests | automated coordinate suite |
| R-007 | File transfer used for malware delivery | Medium | High | explicit consent, policy, MOTW, AV integration, rate limits, audit | abuse and malware-handling test |
| R-008 | Unattended access account takeover | Medium | Critical | MFA, device-bound authorization, conditional policy, rapid revocation | penetration test |
| R-009 | Tenant data leakage | Low | Critical | tenant-scoped repository layer, DB constraints/RLS where practical, tests | isolation test suite |
| R-010 | Native memory corruption | Medium | Critical | narrow C ABI, sanitizers, fuzzing, hardened compiler flags | fuzz and sanitizer reports |
| R-011 | Session quality poor on restrictive networks | Medium | High | TURN UDP/TCP/TLS, regional relays, route diagnostics | network impairment lab |
| R-012 | Hidden-access perception damages trust | Medium | Critical | mandatory indicators, notifications, consent, transparent docs | UX review and policy audit |

| R-013 | Codec/patent or redistributed media dependency obligations are misunderstood before commercial distribution. | Medium | High | Legal/IP review of actual codec path, territories, binaries and business model; record obligations and renewal owner as a release gate. | Signed legal/dependency review record |
