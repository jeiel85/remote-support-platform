# Supported Capability Matrix — Attended GA

Customer-facing statement of what Attended GA (Goals 01–12, Portable Agent and
Operator Console only) supports. No row here may be marketed beyond its
listed status. This consolidates the dimensional test matrices in
`06-quality/compatibility-matrix.md` plus the Goal 05/08/09 implementation
results already recorded there into a single supported/limited/unsupported
statement.

## Status legend

- **Supported** — passes the dimension's automated or lab test and is
  marketed without qualification.
- **Supported with documented limitation** — works, with a stated behavior
  difference the customer must know before relying on it.
- **Preview** — available but excluded from SLO/support commitments.
- **Unsupported** — explicitly out of scope for this release.

## Platform and session

| Capability | Status | Limitation |
|---|---|---|
| Windows 10/11 x64, standard user and administrator sessions | Supported with documented limitation | Physical Windows lab matrix run (`06-quality/compatibility-matrix.md` §1) is required before unqualified GA marketing; implementation-level coordinate/input handling is complete and automated-tested. |
| Arm64 | Unsupported | Separately gated per `06-quality/compatibility-matrix.md` §1; not part of this GA. |
| Fast user switching / RDP-present host | Supported with documented limitation | Attended portable Agent runs in the interactive session that launched it; it does not follow a session switch. Managed Host session handling is Goal 13/14 scope, not this GA. |
| UAC elevation prompt / secure desktop (Ctrl+Alt+Del, UAC consent, logon screen) | Unsupported | Stated in `06-quality/compatibility-matrix.md` Goal 05 result: "Secure desktop is intentionally unsupported" for the portable Agent. Elevated-target control requires the separately authorized installed privileged agent (Managed Host, Goal 13+) and is never claimed for portable Attended GA. |
| Multi-monitor (1–3), mixed DPI 100–200%, portrait rotation, HDR, hot-plug | Supported | Automated coordinate/topology suite (`AT-FR-INP-002` and related). |
| GPU: Intel/NVIDIA/AMD hardware encode, software fallback | Supported | Automated capture/encoder capability probe; hardware-specific lab matrix (`06-quality/compatibility-matrix.md` §3) remains a pre-GA physical-lab gate. |

## Network

| Capability | Status | Limitation |
|---|---|---|
| Direct/NAT, TURN UDP/TCP/TLS, IPv4/IPv6 | Supported | `AT-FR-NET-001` and Goal 07 evidence. |
| HTTP proxy environments | Preview | Requires the proxy compatibility lab pass in `06-quality/compatibility-matrix.md` §4 before promotion to Supported. |
| Restrictive/symmetric NAT with packet loss and 50–500ms RTT | Supported with documented limitation | Functional over TURN; qualitative call-quality SLO under sustained >5% loss is not yet published. |

## Security software

| Capability | Status | Limitation |
|---|---|---|
| Microsoft Defender default posture | Supported with documented limitation | Pending signed-RC submission in `06-quality/av-edr-false-positive-process.md`; no unresolved detection at time of GA cut is a release-gate requirement, not yet evidenced by a dated vendor verdict. |
| Enterprise EDR (partner/lab program) | Preview | Same pending-submission boundary; each partner row moves to Supported only after a dated, non-pending verdict is recorded. |
| Smart App Control / application allowlisting | Preview | Not yet in the tracked submission table. |

## Data and features

| Capability | Status | Limitation |
|---|---|---|
| Signed consent, view/control/clipboard/file/chat scopes | Supported | `AT-FR-CON-*`, `AT-FR-CLP-*`, `AT-FR-FIL-*`. |
| Session recording | Unsupported | Hard-disabled at the tenant-settings layer per Goal 10 evidence; not a GA feature. |
| Managed Host (service-installed device, enrollment, admin revocation) | Unsupported in this release | Goal 13, separate release train (`06-quality/release-gates.md` "Managed Host"). |
| Unattended access | Unsupported in this release | Goal 14, separate release train, disabled by default and absent from the portable Agent artifact. |
| Codec (H.264/AVC path) | Supported with documented limitation | Distribution is contingent on the signed legal/patent-pool review recorded per `00-product/commercialization-and-compliance.md` "Codec, cryptography, and distribution legal gate"; engineering readiness does not substitute for that signed record. |

## Maintenance

The release manager updates this table for every GA candidate. A row may not
move to **Supported** without linked automated-test or physical-lab evidence;
downgrading a row (e.g., after a regression) is a release-blocking action.
