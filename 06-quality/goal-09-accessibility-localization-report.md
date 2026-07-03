# Goal 09 Accessibility and Localization Report

The Agent and Operator Console load either `ko-KR` or `en-US` resource
dictionaries from the current UI culture. Consent identity, permission names,
session state, disconnect, file acceptance, diagnostics and primary error/status
flows use resource keys. Controls have accessible names, logical keyboard focus,
default/cancel consent actions and live status regions. High-contrast-friendly
system controls are used for interactive elements; the permanent red disconnect
action also has text and does not rely on color alone.

Automated build and smoke checks verify both applications can initialize and
shut down from packaged output. Source inspection rejects remaining hard-coded
security-facing labels except technical tokens such as DXGI/WGC, protocol codes
and file paths. The Agent exposes Ctrl+Alt+Shift+F12 with an F11 collision
fallback for emergency disconnect.

Physical qualification is not claimed by this report. Before signed beta, the
release evidence must include Windows Narrator and one representative third-party
screen reader, 200% text scaling, high contrast, keyboard-only consent/install/
update/error journeys, Korean IME and English locale runs on the supported
Windows matrix. Any failure is a promotion blocker, not a documented success.
