# Accessibility and Localization

## 1. Scope

Accessibility is a release requirement for the consent surface, session indicator, emergency disconnect, permission changes, file decisions, device enrollment, security settings, and operator workflows. Korean and English are first-beta locales.

## 2. UI Automation contract

Every interactive control must expose:

- stable automation ID;
- accessible name, role, enabled/disabled state, and value where applicable;
- keyboard shortcut or standard keyboard navigation;
- logical focus order and visible focus indicator;
- state-change notification for connection, permission, transfer, warning, and error changes.

Custom-rendered controls require explicit UI Automation peers. Canvas-only critical controls are prohibited.

## 3. Keyboard behavior

- `Tab`/`Shift+Tab` traverse every actionable element in logical order.
- `Enter` activates the default non-destructive action; destructive actions are never the implicit default.
- `Esc` closes non-critical dialogs without approving a request.
- The local emergency-disconnect hotkey is registered and handled independently of application focus where Windows permits.
- Full-screen remote view always exposes a keyboard path to exit full screen and disconnect.
- No consent, deny, scope-revoke, file decision, or security-setting flow contains a keyboard trap.

## 4. Screen reader and live announcements

Announce without stealing focus:

- incoming support request identity and requested scopes;
- connection/reconnection and direct/relay route changes;
- permission grant/revocation;
- file offer, progress milestones, completion, cancellation, and rejection;
- remote-input paused/resumed state;
- session end and security-relevant errors.

High-frequency transport statistics and pointer motion are never live-announced.

## 5. Visual requirements

- Critical meaning uses text plus icon/shape, never color alone.
- Supported Windows high-contrast themes preserve control borders, focus, selected state, and warning hierarchy.
- Consent and disconnect controls remain visible at supported text-size and DPI settings.
- Layouts tolerate pseudo-localized strings expanded by at least 40%.
- Animation respects Windows reduced-animation preferences where available.

## 6. Localization implementation

- All user-visible strings, including error explanations and consent wording, come from versioned resources.
- Stable protocol/error codes are not translated; presentation maps codes to localized text.
- Logs and telemetry use stable invariant names, not localized strings.
- Dates, times, numbers, byte sizes, keyboard labels, and plural forms use locale-aware formatting.
- Security/legal copy supports per-market review without changing protocol behavior.

## 7. Test evidence

Required evidence is defined by `NFR-ACC-*` in `07-delivery/acceptance-test-cases.csv` and includes keyboard-only runs, Narrator/UI Automation inspection, high-contrast and scaling matrices, pseudo-localization, and Korean/English screenshots or automation reports.
