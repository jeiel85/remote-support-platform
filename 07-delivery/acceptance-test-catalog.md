# Acceptance Test Catalog

The CSV is canonical. Each case below is executable only when its required evidence is retained.

### AT-FR-SES-001 — Portable Agent displays a cryptographically random, short-lived support code.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** Integration + entropy analysis
- **Preconditions:** Portable Agent connected to test control plane; deterministic test clock; 100,000 session creations
- **Procedure:** Create sessions concurrently; inspect displayed code format, uniqueness, expiry metadata, and server-side hashed lookup representation.
- **Pass criteria:** No collisions in sample; configured entropy/rate-limit budget is met; code expires at configured TTL; raw code is not persisted or logged.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-SES-002 — The code identifies a pending session but is not itself the sole authentication secret.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** API security integration
- **Preconditions:** One valid code, one attacker client with code only, host bootstrap key, and authenticated operator identity
- **Procedure:** Attempt peer authorization with only the human code; then repeat complete proof-of-possession exchange.
- **Pass criteria:** Code-only attempt is denied with stable error; only bootstrap/key proof plus approved consent yields scoped peer authorization.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-SES-003 — Operator must be authenticated for commercial SaaS sessions.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** API authorization
- **Preconditions:** Anonymous, expired-token, wrong-tenant, and valid OIDC operator clients
- **Procedure:** Call resolve/create managed-session endpoints under each identity condition.
- **Pass criteria:** Anonymous/expired/wrong-tenant calls fail without information leakage; valid member succeeds according to role and policy.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-SES-004 — Host displays operator identity, organization and requested permissions before approval.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** UI automation + API integration
- **Preconditions:** Host and operator test identities with known organization and requested scopes
- **Procedure:** Operator resolves code; capture host consent surface and accessibility tree before approval.
- **Pass criteria:** Host sees verified operator display identity, tenant organization, requested scopes, expiry, and deny/approve controls before any screen or input stream starts.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-SES-005 — Host can approve view-only, input-control, clipboard and file scopes independently.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** End-to-end authorization
- **Preconditions:** Requested view, pointer, keyboard, clipboard both directions, and file both directions
- **Procedure:** Approve selected subsets in separate cases; attempt every granted and denied capability.
- **Pass criteria:** Peer token and PermissionState contain only approved scopes; denied operations are rejected at host runtime and produce audit evidence.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-SES-006 — Session request expires automatically and cannot be replayed.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** API replay/expiry
- **Preconditions:** Controllable clock, captured code/bootstrap credential, nonce replay harness
- **Procedure:** Use request before and after expiry; replay resolve/bootstrap/peer-authorization requests and nonce.
- **Pass criteria:** Expired or replayed material is rejected atomically; no second participant/token is created; stable security event is emitted.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-SES-007 — Both peers show connection state and relay/direct route classification.
- **Priority / release:** P1 / ATTENDED_GA
- **Level:** UI + network lab
- **Preconditions:** Direct ICE route and forced TURN/TCP/TLS routes
- **Procedure:** Establish each route, transition network, and inspect both UIs and diagnostics.
- **Pass criteria:** Both peers show accurate connecting/connected/reconnecting state and direct/relay classification without exposing sensitive ICE details.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-SCR-001 — Capture the selected monitor at native resolution and current orientation.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** Windows compatibility lab
- **Preconditions:** Reference monitor set including landscape/portrait and 100/150/200% DPI
- **Procedure:** Capture each selected monitor and compare frame dimensions/orientation against Windows display topology.
- **Pass criteria:** Frame size, rotation, crop, and selected display match topology with no adjacent-monitor leakage.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-SCR-002 — Detect monitor add/remove, resolution, rotation, HDR and DPI changes.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** Windows event/recovery lab
- **Preconditions:** Multi-monitor VM/physical lab with hot-plug, resolution, rotation, HDR and DPI changes
- **Procedure:** Apply each topology change during capture and observe generation/recovery.
- **Pass criteria:** A new topology generation is published; stale input/display selection is rejected; capture resumes within bounded recovery time without leak/crash.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-SCR-003 — Allow operator to choose monitor and fit/actual-size/stretch modes.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** UI/render integration
- **Preconditions:** Known test pattern at multiple remote-window sizes
- **Procedure:** Switch display and fit/1:1/stretch/pan-zoom modes; map pointer through each transform.
- **Pass criteria:** Rendered bounds and aspect behavior match mode; operator pointer maps to expected source pixel within defined tolerance.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-SCR-004 — Preserve cursor shape, position and visibility.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** Visual compatibility
- **Preconditions:** Cursor corpus: arrow, I-beam, resize, custom alpha/color, hidden cursor
- **Procedure:** Move/change/hide cursor over static screen and compare operator rendering.
- **Pass criteria:** Shape, hotspot, visibility and position are correct; cursor updates remain responsive independently of low video frame rate.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-SCR-005 — Adapt bitrate, frame rate and scale to network and CPU/GPU pressure.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** Network impairment performance
- **Preconditions:** 1080p office-motion corpus; configurable loss/RTT/bandwidth/CPU pressure
- **Procedure:** Sweep bandwidth and loss while recording bitrate, fps, scale, queue depth and latency.
- **Pass criteria:** Adaptation respects bounded queues, avoids latency runaway, recovers after impairment, and follows profile thresholds.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-SCR-006 — Provide text-clarity and motion-priority quality profiles.
- **Priority / release:** P1 / ATTENDED_GA
- **Level:** Visual quality benchmark
- **Preconditions:** Text/chart/video reference corpus
- **Procedure:** Run TEXT, BALANCED and MOTION profiles at equal bandwidth and collect objective plus reviewer evidence.
- **Pass criteria:** Profiles measurably change frame-rate/scale/quality behavior as documented and never exceed safety/resource limits.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-SCR-007 — Mask configured windows or regions when enterprise policy requires it.
- **Priority / release:** P2 / ATTENDED_GA
- **Level:** Enterprise policy integration
- **Preconditions:** Policy masking a test window and rectangular region; rapid movement/resizing cases
- **Procedure:** Capture while masked objects move, minimize, resize, overlap, and change monitor.
- **Pass criteria:** Configured content is never visible in transmitted frames, including transition frames; policy failure fails closed and is surfaced.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-INP-001 — Remote control is disabled until host grants input scope.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** End-to-end security
- **Preconditions:** Session with view only, then dynamically granted input scope
- **Procedure:** Send pointer/keyboard events before grant, after grant, and after revocation.
- **Pass criteria:** Only events inside active scope/revision are applied; rejected events are acknowledged and audited without side effects.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-INP-002 — Map absolute coordinates correctly across mixed-DPI multi-monitor desktops.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** Windows topology automation
- **Preconditions:** Mixed-DPI monitors with negative virtual coordinates and rotation
- **Procedure:** Inject clicks at corners/centers and drag paths using current and stale topology generations.
- **Pass criteria:** Applied coordinates land within tolerance on current topology; stale-generation input is rejected and requests resynchronization.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-INP-003 — Support mouse buttons, wheel, keyboard scan codes and Unicode text input.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** Input compatibility
- **Preconditions:** Keyboard layouts KO/EN plus mouse button/wheel corpus
- **Procedure:** Send scan-code keys, modifiers, Unicode text, extended keys, buttons and vertical/horizontal wheel events.
- **Pass criteria:** Target applications receive correct down/up/text sequences with no duplicate/stuck modifiers; unsupported secure sequences are explicitly rejected.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-INP-004 — Release stuck keys/buttons on disconnect, focus loss and transport reset.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** Fault injection
- **Preconditions:** Keys/buttons held while disconnect, focus loss, permission revoke, Agent crash and transport epoch change occur
- **Procedure:** Trigger each interruption and inspect local input state.
- **Pass criteria:** Release-all executes within bounded time; no key/button remains logically pressed; late events from old epoch are rejected.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-INP-005 — Local user activity may temporarily override or pause remote input by policy.
- **Priority / release:** P1 / ATTENDED_GA
- **Level:** Policy + local activity
- **Preconditions:** Policy configured to pause on local input
- **Procedure:** Generate remote drag/key input while local mouse/keyboard activity occurs.
- **Pass criteria:** Remote input pauses/queues/drops exactly per policy, UI communicates state, and resumption requires documented condition.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-INP-006 — Local emergency disconnect hotkey is always active and cannot be disabled.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** Safety UI/keyboard
- **Preconditions:** Active controlled session in normal, full-screen and degraded-network states
- **Procedure:** Invoke configured emergency hotkey repeatedly and while UI lacks focus.
- **Pass criteria:** Session/input ends locally within bounded time; hotkey cannot be disabled by remote peer or policy; event is audited.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-INP-007 — Elevated and secure-desktop capabilities are reported explicitly, never silently assumed.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** Privilege compatibility
- **Preconditions:** Standard app, elevated app, UAC secure desktop and locked-session cases
- **Procedure:** Attempt view/control and query capability state in each case.
- **Pass criteria:** UI reports capability truthfully; unsupported actions fail explicitly; no silent UIPI bypass or insecure policy change occurs.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-DAT-001 — Clipboard sync is off until permission is granted.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** Peer protocol security
- **Preconditions:** Clipboard content present on both peers; scope absent/granted/revoked
- **Procedure:** Trigger clipboard changes in each permission state.
- **Pass criteria:** No offer/content crosses without directional scope; loop suppression works; revoke stops subsequent transfers immediately.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-DAT-002 — Initial GA supports text clipboard only; rich formats are separately gated.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** Protocol parser
- **Preconditions:** Text, HTML, RTF, image, file-drop and oversized clipboard formats
- **Procedure:** Offer each format and malformed UTF-8/text payloads.
- **Pass criteria:** Only bounded UTF-8 text is accepted for GA; all other formats receive stable rejection without parser crash or data leak.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-DAT-003 — File transfer requires explicit direction, size, destination and policy checks.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** File policy integration
- **Preconditions:** Files across allowed/denied size, direction, extension and destination policies
- **Procedure:** Offer transfers and alter metadata between offer and data phase.
- **Pass criteria:** Direction/size/policy/destination are revalidated; unauthorized data chunks are rejected; user sees final destination before acceptance.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-DAT-004 — Partial transfers resume using chunk hashes and transfer IDs.
- **Priority / release:** P1 / ATTENDED_GA
- **Level:** Transfer resilience
- **Preconditions:** Large deterministic file; disconnect at multiple chunk boundaries; corrupt selected chunks
- **Procedure:** Reconnect with same authorized transfer and resume ranges.
- **Pass criteria:** Only verified chunks are reused; corrupt/missing chunks retransmit; final SHA-256 matches source; expired session cannot resume.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-DAT-005 — Received files are marked as externally sourced and never auto-opened.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** Windows file safety
- **Preconditions:** Transfer executable, document and archive samples
- **Procedure:** Complete transfer and inspect filesystem metadata/UI behavior.
- **Pass criteria:** File is written atomically to approved path, marked externally sourced where supported, never auto-opened/executed, and surfaced for user action.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-DAT-006 — Operator and host can cancel an active transfer immediately.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** Transfer cancellation
- **Preconditions:** Active upload/download at low bandwidth with queued chunks
- **Procedure:** Cancel from each side and during reconnect.
- **Pass criteria:** Further chunks stop, memory/file handles release, partial-file policy is honored, peer receives cancellation, and audit metadata is complete.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-DAT-007 — Audit log stores metadata, not file content.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** Log/privacy inspection
- **Preconditions:** Run representative file transfers with canary filenames/content
- **Procedure:** Search application, audit, telemetry, dumps and support bundle.
- **Pass criteria:** Only approved metadata appears; no file bytes, path secrets beyond policy, or content hashes classified as secret are leaked contrary to inventory.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-CTL-001 — Provide in-session text chat.
- **Priority / release:** P1 / ATTENDED_GA
- **Level:** Peer messaging
- **Preconditions:** Connected session with CHAT scope; Unicode/emoji/limit cases
- **Procedure:** Send messages both directions, duplicate IDs, oversized text, and revoked-scope cases.
- **Pass criteria:** Valid messages deliver once with acknowledgement; duplicates are idempotent; size/scope violations are rejected without session failure.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-CTL-002 — Host can revoke individual scopes without ending the session.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** Dynamic authorization
- **Preconditions:** Session with all scopes active
- **Procedure:** Host revokes one scope at a time while operation is in flight.
- **Pass criteria:** Permission revision advances; affected operation stops at defined boundary; unrelated scopes remain active; both UIs reconcile.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-CTL-003 — Host and operator can terminate the session.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** Lifecycle integration
- **Preconditions:** Sessions in pending, connecting, connected and reconnecting states
- **Procedure:** End from host, operator and server policy; repeat duplicate end requests.
- **Pass criteria:** Session reaches terminal state idempotently, transport closes, credentials become unusable, input releases, and reason is audited.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-CTL-004 — Session indicator shows active scopes and elapsed time.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** Host/operator UI
- **Preconditions:** Active session with changing scopes and controlled clock
- **Procedure:** Inspect persistent indicator through window minimize/full-screen/topmost conflicts and scope changes.
- **Pass criteria:** Indicator remains discoverable, shows elapsed time/identity/active scopes, and exposes immediate disconnect.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-CTL-005 — Reconnect requires a bounded grace token and state reconciliation.
- **Priority / release:** P1 / ATTENDED_GA
- **Level:** Reconnect security
- **Preconditions:** Connected session, controlled network loss, valid/expired/replayed reconnect grants
- **Procedure:** Reconnect within and outside grace window and with mismatched peer key/epoch.
- **Pass criteria:** Only bound valid grant reconnects; state and permissions reconcile; replay/expired/wrong-peer grants fail and old epoch traffic is rejected.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-MGT-001 — Device enrollment binds an installation to a tenant using a one-time enrollment token.
- **Priority / release:** P0 / MANAGED_HOST
- **Level:** Enrollment integration
- **Preconditions:** Tenant admin, fresh installer, one-time token and cloned VM image
- **Procedure:** Enroll once, replay token, and attempt enrollment from clone/wrong tenant.
- **Pass criteria:** Exactly one device identity is bound; token is consumed atomically; clone/replay is rejected and audited.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-MGT-002 — Device private keys remain non-exportable where platform APIs support it.
- **Priority / release:** P0 / MANAGED_HOST
- **Level:** Key storage security
- **Preconditions:** Installed Host on supported TPM/DPAPI configurations
- **Procedure:** Generate device key, attempt export/read as standard user/admin, rotate and revoke.
- **Pass criteria:** Private key is non-exportable where supported or DPAPI machine-bound with documented fallback; raw private material is absent from files/logs.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-MGT-003 — Unattended access is disabled by default.
- **Priority / release:** P0 / UNATTENDED_GA
- **Level:** Default configuration
- **Preconditions:** Fresh install, upgrade, repair and config reset
- **Procedure:** Inspect policy/UI/service behavior before explicit enablement.
- **Pass criteria:** Unattended capability is disabled and unreachable in every default/reset path.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-MGT-004 — Enabling unattended access requires local admin action or centrally managed enterprise deployment.
- **Priority / release:** P0 / UNATTENDED_GA
- **Level:** Privilege/policy
- **Preconditions:** Standard user, local admin and centrally managed deployment contexts
- **Procedure:** Attempt to enable unattended access through UI/config/API under each context.
- **Pass criteria:** Only approved admin or authenticated enterprise deployment path succeeds; action is prominent, reversible and audited.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-MGT-005 — Operator MFA and policy authorization are required for unattended sessions.
- **Priority / release:** P0 / UNATTENDED_GA
- **Level:** MFA/policy authorization
- **Preconditions:** Managed device; operator sessions with fresh MFA, stale MFA and no MFA
- **Procedure:** Request unattended session across allow/deny policy cases.
- **Pass criteria:** No unattended peer token is issued without required MFA age and policy allow; denial reason is stable and audited.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-MGT-006 — Device access can be revoked immediately from the tenant console.
- **Priority / release:** P0 / MANAGED_HOST
- **Level:** Revocation propagation
- **Preconditions:** Online and temporarily offline managed devices with active/idle sessions
- **Procedure:** Revoke device and keys; reconnect device after offline interval.
- **Pass criteria:** New access is denied immediately server-side; active session follows policy; device rejects/receives revocation on next contact; audit chain records actor.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-MGT-007 — Every unattended connection generates host notification and audit evidence unless a documented enterprise policy says otherwise.
- **Priority / release:** P0 / UNATTENDED_GA
- **Level:** Notification/audit
- **Preconditions:** Unattended connection under default and explicitly documented enterprise policy
- **Procedure:** Connect, reconnect and terminate; inspect local notification and audit.
- **Pass criteria:** Default path shows persistent host notification and complete audit; any policy suppression is explicit, authorized and still audit-visible.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-MGT-008 — An installed device refreshes and rotates its control-plane credential by proving possession of its active device key, without repeating tenant enrollment.
- **Priority / release:** P0 / MANAGED_HOST
- **Level:** Integration/Security
- **Preconditions:** An enrolled active device with an active key and expiring credential exists.
- **Procedure:** Request challenge; sign canonical payload; exchange credential; rotate to a new key; prove new key; revoke old authorization version; retry stale credential.
- **Pass criteria:** Refresh succeeds without re-enrollment; rotation requires both key proofs; stale/revoked credentials and replayed challenges are rejected.
- **Evidence:** API traces, DB state, audit events, key-version evidence and negative-test logs.
- **Automation:** Automate API/crypto integration and replay/revocation tests; run platform-key-store test on Windows.

### AT-FR-MGT-009 — An installed host receives managed-session requests through an authenticated, revocable device channel and binds each launched host peer to a fresh ephemeral key.
- **Priority / release:** P0 / MANAGED_HOST
- **Level:** End-to-end/Security
- **Preconditions:** An enrolled online device polls with a valid device credential and a policy-authorized operator exists.
- **Procedure:** Create managed session; observe HOST_PENDING delivery; acknowledge twice; bind a fresh host key with signed decision; revoke device and repeat.
- **Pass criteria:** Exactly one host peer is bound; duplicate delivery is idempotent; revoked device cannot poll, decide or authorize a peer.
- **Evidence:** Outbox/poll traces, signed decision fixture, session state history and audit events.
- **Automation:** Automate server/device simulator; validate Windows Service path in Goal 13 lab.

### AT-FR-ADM-001 — Tenant roles include Owner, Admin, Security Auditor, Operator and Read-only Analyst.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** RBAC integration
- **Preconditions:** One account per defined role plus cross-tenant identities
- **Procedure:** Exercise every administrative/API action against authorization matrix.
- **Pass criteria:** Each role has only documented permissions; read-only/auditor cannot mutate; cross-tenant resources are indistinguishable from nonexistent as designed.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-ADM-002 — Access decisions evaluate tenant, operator, device, policy, time and requested scope.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** Policy decision table
- **Preconditions:** Combinatorial tenant/operator/device/time/MFA/scope inputs including conflicting rules
- **Procedure:** Evaluate decisions repeatedly and across nodes/cache states.
- **Pass criteria:** Deterministic deny-overrides result, matched-rule evidence and policy version are identical across executions.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-ADM-003 — Audit records are append-only at the application layer and tamper-evident.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** Audit integrity
- **Preconditions:** Known event sequence; database administrator tamper simulation in isolated lab
- **Procedure:** Append events, verify chain, modify/delete/reorder rows, then run verifier/export.
- **Pass criteria:** Normal chain verifies; each tamper is detected at or before affected sequence; application APIs expose no mutation path.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-ADM-004 — Admin can configure retention, allowed features, file limits, and an explicit recording-disabled policy for attended GA; recording enablement is a separately released capability.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** Admin policy integration
- **Preconditions:** Tenant admin and non-admin; retention/feature/file/recording policy variants
- **Procedure:** Create/activate versions, simulate sessions and inspect effective snapshot.
- **Pass criteria:** Authorized changes are versioned/audited; limits enforce at decision and runtime; attended GA cannot enable recording through policy.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-ADM-005 — Security events can be exported through webhook/SIEM integration.
- **Priority / release:** P1 / ENTERPRISE_POST_GA
- **Level:** Webhook/SIEM integration
- **Preconditions:** Signed webhook endpoint with success, timeout, 4xx/5xx and replay cases
- **Procedure:** Emit security events and drive retry/dead-letter flow.
- **Pass criteria:** Payload is tenant-scoped/minimized/signed; retries are bounded/idempotent; replay is detectable; failure is observable.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-ADM-006 — Support staff access to customer metadata uses just-in-time privileged workflow and is audited.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** Support-access governance
- **Preconditions:** Support user without grant, approved JIT grant, expired grant and break-glass case
- **Procedure:** Query customer metadata in each case and inspect approvals/audit.
- **Pass criteria:** Access requires scoped time-bounded grant or governed break-glass; every view/action is attributable; session contents remain unavailable.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-FR-ADM-007 — Tenant owners and admins can invite members, update authorized roles, suspend access, and remove memberships through audited workflows.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** End-to-end/Authorization
- **Preconditions:** Owner, Admin, Auditor and Operator identities exist in one tenant plus an unrelated tenant.
- **Procedure:** Invite a member; accept; change roles; suspend; remove; attempt each action with unauthorized roles and cross-tenant identifiers.
- **Pass criteria:** Authorized changes apply with optimistic concurrency and audit evidence; unauthorized/cross-tenant operations reveal no foreign data.
- **Evidence:** Browser/API traces, membership versions, audit events and tenant-isolation assertions.
- **Automation:** Automate API and browser role matrix.

### AT-FR-ADM-008 — Tenant owners can request data export and tenant closure and can track each workflow to auditable completion.
- **Priority / release:** P0 / ATTENDED_GA
- **Level:** End-to-end/Privacy
- **Preconditions:** An Owner identity and tenant with representative data exist.
- **Procedure:** Request export; complete worker flow; validate expiring download metadata; request closure; exercise reauthentication, cooling-off, cancellation and completion.
- **Pass criteria:** Workflows are trackable, idempotent, auditable, access-controlled and apply documented retention/deletion behavior.
- **Evidence:** Workflow records, audit chain, export inventory/hash and closure evidence.
- **Automation:** Automate state machine and authorization; manually approve legal/operations evidence.

### AT-NFR-PERF-001 — Capture-to-display median latency is below 150 ms on a healthy direct connection in the reference lab.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Performance lab
- **Preconditions:** Reference direct-network profile and synchronized capture/display instrumentation
- **Procedure:** Run idle, office-motion and scrolling workloads for defined sample count after warm-up.
- **Pass criteria:** Median capture-to-display latency is <150 ms and raw distribution/equipment/build are retained.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-PERF-002 — Input event round-trip p95 is below 200 ms on a healthy direct connection.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Performance lab
- **Preconditions:** Reference direct-network profile and input loopback marker
- **Procedure:** Inject representative pointer/button/key events and measure host apply-to-operator acknowledgement RTT.
- **Pass criteria:** p95 is <200 ms with no rejected/merged reliable events; distribution and clock method are retained.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-PERF-003 — Connection establishment p95 is below 10 seconds when either a direct or TURN route is available.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Connection benchmark
- **Preconditions:** Direct, TURN/UDP, TURN/TCP and TURN/TLS available profiles
- **Procedure:** Create repeated cold/warm sessions per route and measure from user action to usable media/control.
- **Pass criteria:** p95 is <10 s for each claimed available route; failures are classified by phase.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-PERF-004 — Idle-desktop bitrate converges below 300 Kbps at 1080p when no meaningful visual change occurs.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Media benchmark
- **Preconditions:** 1080p static desktop after motion settles
- **Procedure:** Measure encoded/network bitrate over defined convergence and observation windows.
- **Pass criteria:** Sustained mean after convergence is <300 Kbps with readable text and bounded heartbeat overhead.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-PERF-005 — Active office-work bitrate remains adaptive, with a reference target range of 1–5 Mbps at 1080p.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Media benchmark
- **Preconditions:** 1080p office workload and bandwidth sweep
- **Procedure:** Measure bitrate, fps, scale, quality and latency across network capacities.
- **Pass criteria:** Controller adapts rather than pinning bitrate; normal reference cases remain within 1–5 Mbps and preserve latency limits.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-PERF-006 — Client working-set target is below 500 MB for one 1080p session; exceptions require profiling evidence and release approval.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Resource benchmark
- **Preconditions:** One 1080p session, repeated connect/disconnect and feature toggles
- **Procedure:** Record private bytes/working set/native heaps/GPU allocations after warm-up and soak.
- **Pass criteria:** Steady-state working set is <500 MB or approved exception exists; no monotonic leak appears across cycles.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-REL-001 — Attended-GA control-plane monthly availability objective is at least 99.9%, excluding declared maintenance under the published policy.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** SLO verification
- **Preconditions:** Production-like synthetic probes and monthly SLI query
- **Procedure:** Inject planned maintenance tag and unplanned failures; aggregate successful eligible control-plane requests.
- **Pass criteria:** SLI calculation and exclusions match catalog; rolling monthly objective is >=99.9% or release/incident process triggers.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-REL-002 — Each production TURN region has at least two independently restartable relay nodes and a regional availability objective of at least 99.9%.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** TURN resilience
- **Preconditions:** Two relay nodes in one region with health/load routing
- **Procedure:** Establish relayed sessions, terminate one node, saturate another, and restore service.
- **Pass criteria:** New allocations shift to healthy node; existing behavior matches documented limits; regional SLI and capacity alerts fire.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-REL-003 — Client crash-free session objective is at least 99.5% before beta and 99.9% before GA, measured with a documented denominator and privacy-safe telemetry.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Client reliability telemetry
- **Preconditions:** Privacy-safe session/crash telemetry with deterministic denominator
- **Procedure:** Run beta/GA soak and forced-crash validation; reconcile client and backend counts.
- **Pass criteria:** Crash-free metric is calculated correctly and meets stage threshold; crash dumps respect consent/redaction policy.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-REL-004 — Active peer media continues during transient signaling loss when the established WebRTC transport remains valid, and state reconciles after signaling recovery.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Network fault injection
- **Preconditions:** Established session with signaling WebSocket proxied independently from peer transport
- **Procedure:** Drop signaling while media/data transport remains alive, then recover and force renegotiation.
- **Pass criteria:** Media continues where transport valid; no unauthorized change occurs; state reconciles using current epoch after recovery.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-REL-005 — Every resource-owning native module supports deterministic teardown, bounded shutdown, device-loss recovery, and watchdog-observable failure states.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Native soak/fault injection
- **Preconditions:** Debug counters and GPU/device-loss injection
- **Procedure:** Run 1,000 lifecycle cycles plus device reset, timeout and cancellation during each native module operation.
- **Pass criteria:** Shutdown is bounded; handles/threads/allocations return to baseline; watchdog receives stable failure state and recovery succeeds.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-REL-006 — Database backups, point-in-time recovery, and restore verification meet the RPO/RTO objectives in the disaster-recovery plan.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Disaster-recovery drill
- **Preconditions:** Encrypted backups, PITR archive and isolated restore environment
- **Procedure:** Restore to selected timestamp, run integrity/tenant/audit checks, and measure timings.
- **Pass criteria:** Measured data loss and recovery time meet DR objectives; restored system passes verification before promotion.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-REL-007 — Client update failure cannot leave the last known-good signed version unlaunchable; rollback is automatic or explicitly recoverable.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Update recovery
- **Preconditions:** Last-known-good signed version plus corrupt, killed, incompatible and rollback update cases
- **Procedure:** Interrupt install at each phase and simulate launch-health failure.
- **Pass criteria:** System launches last-known-good or enters documented repair path; unsigned/rollback artifacts never become active.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-SEC-001 — All internet traffic uses authenticated encryption with approved protocol versions and cipher policy.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Transport security
- **Preconditions:** TLS/DTLS scanner and downgrade proxy
- **Procedure:** Test API, signaling, update, TURN/TLS and WebRTC handshakes with valid and deprecated protocol/cipher variants.
- **Pass criteria:** Only approved authenticated encryption succeeds; certificate/fingerprint validation failures fail closed.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-SEC-002 — Device identity, operator identity, tenant membership, and ephemeral peer identity are distinct trust subjects and are bound explicitly during authorization.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Identity binding
- **Preconditions:** Operator/device/peer keys across two tenants and swapped signaling artifacts
- **Procedure:** Attempt token/key/SDP/ticket substitution between subjects.
- **Pass criteria:** Every substitution is rejected; audit identifies attempted subject mismatch without leaking other tenant data.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-SEC-003 — Session authorization, signaling tickets, reconnect grants, and TURN credentials are short-lived, purpose-bound, audience-bound, and scope-bound where applicable.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Credential lifecycle
- **Preconditions:** Controllable clock and captured tokens/tickets/grants/credentials
- **Procedure:** Use each credential for wrong endpoint, audience, scope, session, peer, epoch and after expiry.
- **Pass criteria:** Credential is accepted only for exact bound purpose during TTL and cannot be replayed beyond documented semantics.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-SEC-004 — Long-lived TURN credentials are never distributed to clients.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** TURN credential review
- **Preconditions:** Client memory/config/network capture and TURN auth service
- **Procedure:** Inspect issued username/password TTL and search packages/config/logs for static credentials.
- **Pass criteria:** Only ephemeral REST-derived credentials are exposed; no reusable long-lived TURN secret leaves server/secret store.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-SEC-005 — Secrets, bearer tokens, clipboard/chat contents, keystrokes, screen contents, and transferred-file contents never appear in application logs or telemetry.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Sensitive-data canary
- **Preconditions:** Unique canaries in secrets, tokens, screen, keys, clipboard, chat and file contents
- **Procedure:** Exercise normal/error/crash paths; scan logs, traces, metrics, dumps and support bundles.
- **Pass criteria:** No prohibited canary appears; allowed identifiers are redacted/hashed according to data inventory.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-SEC-006 — Every privileged or security-relevant state transition emits a stable audit event with actor, tenant, target, outcome, reason, correlation, and tamper-evident sequence data.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Audit coverage
- **Preconditions:** Catalog-driven privileged action harness
- **Procedure:** Execute success, denial and failure for every cataloged action; compare emitted schema and chain sequence.
- **Pass criteria:** Required actor/tenant/target/outcome/reason/correlation/sequence fields exist and validate; no unaudited privileged transition remains.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-SEC-007 — Dependency, container, secret, static-analysis, and artifact-signature checks block release according to the secure-SDLC exception policy.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** CI/release policy
- **Preconditions:** Known vulnerable dependency test fixture, secret canary, failing analyzer and invalid signature
- **Procedure:** Run protected pipeline and exception workflow.
- **Pass criteria:** Pipeline blocks according to severity; only approved time-bounded exception can proceed; evidence and SBOM are retained.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-SEC-008 — LocalSystem/service IPC authenticates the connecting process and user session, authorizes each command, rejects replay, and exposes no generic command-execution primitive.
- **Priority / release:** P0 / MANAGED_HOST
- **Level:** Local privilege-boundary test
- **Preconditions:** Untrusted same-user process, wrong-session process, stale child, replayed IPC frames and fuzzed commands
- **Procedure:** Connect to service pipe and invoke allowlisted/non-allowlisted operations under each identity.
- **Pass criteria:** Process/session/signature/nonce/command checks fail closed; no generic execution or arbitrary path/argument primitive is reachable.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-SEC-009 — Update metadata and artifacts are signature-verified, hash-verified, product/channel/architecture-bound, expiry-checked, and protected against rollback.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Update-chain security
- **Preconditions:** Valid, expired, wrong-product, wrong-channel, wrong-arch, downgraded, hash-mismatched and wrongly signed manifests/artifacts
- **Procedure:** Feed each case to updater before and after download.
- **Pass criteria:** Only valid fresh authorized version activates; every failure preserves current version and emits stable diagnostic/audit event.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-SEC-010 — Tenant isolation is enforced and tested at API, application, database-policy, background-job, cache-key, webhook, and audit-query boundaries.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Tenant isolation
- **Preconditions:** Two tenants with colliding human names and seeded resources across all data paths
- **Procedure:** Attempt direct-ID, list/filter, background-job, cache, webhook and audit-query cross-tenant access.
- **Pass criteria:** No cross-tenant data/action occurs; database/RLS and application checks agree; negative tests pass under admin/support roles too.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-SEC-011 — Before any screen, input, clipboard, chat, or file payload is accepted, both peers verify a signed transport binding covering the session, peer identities, granted scopes, permission revision, transport epoch, and negotiated DTLS fingerprints.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Security/Integration
- **Preconditions:** Two authorized peers, current session authorization and a controllable signaling test harness exist.
- **Procedure:** Establish DTLS; substitute one fingerprint, replay an old epoch, alter scopes, and then execute the valid reciprocal binding flow.
- **Pass criteria:** All invalid bindings close or quarantine channels before content; valid current bindings enable only granted capabilities.
- **Evidence:** Peer logs without secrets, packet trace, signed binding fixtures and audit events.
- **Automation:** Automate protocol negative/positive tests against the actual WebRTC build.

### AT-NFR-PRV-001 — The control plane stores only the documented session, identity, device, security, billing, and operational metadata required by an approved data inventory.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Data inventory verification
- **Preconditions:** Schema, telemetry catalog, logs and production-like database snapshot
- **Procedure:** Map every persisted/transmitted field to approved inventory and purpose.
- **Pass criteria:** No undocumented field exists; classification, retention and access owner are recorded for each field.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-PRV-002 — Screen frames, keystrokes, clipboard contents, chat contents, and transferred-file contents are not persisted by the SaaS control plane by default.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Content non-persistence
- **Preconditions:** Canary content in all session channels and relay/control-plane traffic
- **Procedure:** Complete sessions, backups and incident diagnostics; search stores/object buckets/queues/logs.
- **Pass criteria:** No session content is persisted by SaaS control plane; only approved metadata remains.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-PRV-003 — Any future session recording is a separately released feature with explicit policy, local disclosure/consent, encryption, retention, access control, and deletion behavior.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Feature gate review
- **Preconditions:** Attended GA build and policy/API clients attempting recording enablement
- **Procedure:** Search binaries/contracts/UI and attempt to enable/trigger recording.
- **Pass criteria:** Recording cannot be enabled or started; future implementation is blocked behind separate capability/version/consent design.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-PRV-004 — Every persisted field has classification, purpose, lawful/contractual basis review, retention period, regional processing rule, and deletion/export behavior.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Governance review
- **Preconditions:** Current schema/event/metric/config catalogs
- **Procedure:** Compare every field with classification, purpose, basis review, retention, region and deletion/export mappings.
- **Pass criteria:** Coverage is 100%; missing or changed fields block release until review is completed.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-PRV-005 — Tenant deletion, user deletion, device revocation, data export, and retention expiry are testable workflows with auditable completion evidence.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Data-subject/tenant lifecycle
- **Preconditions:** Tenant with users/devices/audit/billing/security data and retention clock
- **Procedure:** Run export, user deletion, device revocation, tenant closure and retention expiry.
- **Pass criteria:** Workflow completes within policy, preserves legally required/audit-protected records correctly, and provides auditable completion evidence.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-PRV-006 — Diagnostic bundles apply allowlisted collection and redaction, require user/admin intent, and never include session content or reusable credentials.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Support-bundle privacy
- **Preconditions:** Normal/error session containing content canaries and credentials
- **Procedure:** Generate bundle under standard and admin paths; inspect manifest and files.
- **Pass criteria:** Only allowlisted diagnostics are collected; redaction succeeds; explicit intent is recorded; bundle contains no reusable credential/session content.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-MNT-001 — Public HTTP, peer, IPC, native ABI, configuration, event, and update interfaces are versioned and compatibility-tested.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Contract compatibility CI
- **Preconditions:** Current contract and latest released fixtures/clients
- **Procedure:** Run OpenAPI, Protobuf, IPC, ABI, config/event/update compatibility checks with additive and breaking test mutations.
- **Pass criteria:** Additive compatible changes pass; field reuse/removal/type break/version mismatch fails before merge.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-MNT-002 — Native C++ components expose a stable C ABI so managed code does not depend directly on unstable C++ layouts or exceptions.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** ABI compilation/lifecycle
- **Preconditions:** C and C++ consumers built with supported MSVC toolsets
- **Procedure:** Compile header, load DLL, negotiate version, exercise callbacks/ownership/error paths.
- **Pass criteria:** No C++ layout/exception crosses boundary; struct-size/version rules and ownership behavior match contract.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-MNT-003 — Every production module has an owner, tests, telemetry, failure-mode documentation, dependency inventory, and operational runbook linkage.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Architecture governance
- **Preconditions:** Module inventory and ownership/runbook/test/telemetry metadata
- **Procedure:** Run repository policy checker against all production modules.
- **Pass criteria:** Every module has required owner/evidence links; orphaned module or missing operational metadata blocks release.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-MNT-004 — A clean protected worker restores from lock files, builds reproducibly, and emits dependency inventory, provenance, and an SBOM.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Reproducible build
- **Preconditions:** Two clean protected workers with lock files and no shared caches
- **Procedure:** Restore, build, test, package and compare provenance/SBOM/artifact normalized hashes.
- **Pass criteria:** Dependencies match locks; provenance/SBOM complete; deterministic artifacts match or documented nondeterministic fields are attested.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-MNT-005 — Architecture-significant decisions and deviations are recorded in ADRs; undocumented boundary changes fail review.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Architecture rule CI
- **Preconditions:** Introduce prohibited dependency/boundary change in test branch
- **Procedure:** Run architecture tests and review policy.
- **Pass criteria:** Violation fails CI and requires linked ADR/approved boundary update before merge.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-MNT-006 — Database changes use forward-only versioned migrations with compatibility windows, rollback/roll-forward procedures, and production rehearsal for destructive changes.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Migration rehearsal
- **Preconditions:** Production-scale anonymized dataset and previous/current app versions
- **Procedure:** Apply expand migration, run mixed-version traffic, backfill, contract, restore/roll-forward drill.
- **Pass criteria:** No incompatible lock/data loss; compatibility window works; checksum/version recorded; recovery procedure meets target.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-ACC-001 — Consent, session control, emergency disconnect, file decisions, and security settings are fully operable by keyboard.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Keyboard accessibility
- **Preconditions:** Windows keyboard-only environment with mouse unavailable
- **Procedure:** Complete consent, grant/revoke, file decision, settings and emergency disconnect flows.
- **Pass criteria:** All actions reachable with logical focus order and visible focus; no keyboard trap; emergency disconnect works regardless of focus.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-ACC-002 — Operator and host UI expose accessible names, roles, states, focus order, live announcements, and high-contrast behavior for supported assistive technologies.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Assistive technology
- **Preconditions:** Narrator/UI Automation and high-contrast themes
- **Procedure:** Inspect roles/names/states/live announcements and operate changing session UI.
- **Pass criteria:** Critical controls/status are correctly exposed and announced; contrast/focus remain usable in supported themes.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-ACC-003 — Korean and English resources are present from the first beta, with no user-visible security text hard-coded in implementation code.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Localization CI/UI review
- **Preconditions:** ko-KR and en-US resources with pseudo-localization build
- **Procedure:** Scan source for user-visible literals; run core flows in both locales and pseudo-locale.
- **Pass criteria:** No security text is hard-coded; translations exist; placeholders/plurals/layout remain correct.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-ACC-004 — Security-relevant state is never communicated by color, animation, or iconography alone.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Non-color communication
- **Preconditions:** Color-blind simulation, monochrome/high-contrast display
- **Procedure:** Inspect permission, warning, connection, error and destructive-action states.
- **Pass criteria:** Each state has text/shape/accessibility semantics in addition to color/animation/icon.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-ACC-005 — At supported Windows text scaling and DPI settings, critical consent/disconnect controls remain visible, readable, and operable without clipping.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** DPI/text scaling compatibility
- **Preconditions:** 100–225% DPI and supported Windows text-size settings
- **Procedure:** Run consent, session indicator, file prompt and disconnect flows at minimum window sizes.
- **Pass criteria:** Critical text/control is not clipped or obscured and remains operable without unsafe horizontal scrolling.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-CST-001 — A secure direct peer route is preferred when reachable; relay fallback does not weaken authorization or encryption.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Route selection
- **Preconditions:** Direct route and relay route both available, then direct blocked
- **Procedure:** Compare ICE selection and authorization/security properties.
- **Pass criteria:** Direct route is preferred when policy/reachability allow; relay fallback preserves identity, encryption and scopes.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-CST-002 — TURN usage is metered by tenant, region, route, transport, session, and byte direction with bounded-cardinality identifiers.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Metering accuracy
- **Preconditions:** Known byte streams in both directions across UDP/TCP/TLS relay routes
- **Procedure:** Reconcile coturn/network counters with metering records by tenant/region/session.
- **Pass criteria:** Measured usage falls within documented tolerance; dimensions are bounded-cardinality and tenant-correct.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-CST-003 — Rate limits, quotas, credential TTLs, allocation limits, and abuse controls prevent unbounded relay and control-plane consumption.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Abuse/load test
- **Preconditions:** Malicious clients exceeding API, allocation, bandwidth and credential rates
- **Procedure:** Drive limits from distributed identities and tenants.
- **Pass criteria:** Requests/allocations are throttled or denied predictably; legitimate tenants retain service; alerts and abuse evidence fire.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-CST-004 — Video adaptation reduces idle and low-motion traffic without violating the latency and text-readability targets.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Adaptive efficiency
- **Preconditions:** Idle, typing, scrolling and video workloads under fixed quality constraints
- **Procedure:** Measure bitrate and readability/latency as motion changes.
- **Pass criteria:** Traffic drops materially in idle/low motion and recovers promptly without violating readability/latency thresholds.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-CST-005 — Server-side storage defaults to metadata only; any new content-bearing storage requires an approved architecture, privacy, security, and cost review.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Storage architecture gate
- **Preconditions:** Proposed content-bearing storage change in design/review fixture
- **Procedure:** Run architecture/privacy/security/cost policy checks and inspect default deployment.
- **Pass criteria:** Default stores metadata only; content storage cannot merge/deploy without all required approvals and retention/encryption design.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.

### AT-NFR-CST-006 — Capacity models are validated by load tests before each production tier increase, and alert thresholds are derived from measured saturation behavior.
- **Priority / release:** P0 / ALL_RELEASES
- **Level:** Capacity validation
- **Preconditions:** Production topology in load environment with staged tenant/session growth
- **Procedure:** Load until each component saturation point, validate autoscaling/alerts and compare model.
- **Pass criteria:** Capacity model error is within approved tolerance; thresholds precede exhaustion; tier increase has signed evidence.
- **Evidence:** build and contract version; environment/lab profile; timestamped result; relevant logs/metrics/traces with redaction; artifact or report URI; reviewer for manual evidence
- **Automation:** Automate in CI/lab unless OS, hardware, accessibility, legal, or independent-review evidence is explicitly required.
