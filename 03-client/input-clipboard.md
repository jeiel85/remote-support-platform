# Input and Clipboard

## Input architecture

```text
Remote event
→ protocol validation
→ permission check
→ desktop/topology generation check
→ coordinate normalization
→ secure-desktop/integrity capability check
→ Win32 injection
→ result telemetry
```

## Coordinate model

Use canonical physical pixel coordinates in the Windows virtual desktop space. Coordinates may be negative. Each pointer event carries the display-topology generation used by the operator. Events against stale topology are rejected or remapped only when safe.

For absolute mouse injection, normalize to the Windows absolute input range using the complete virtual desktop bounds and the virtual-desktop flag. Validate off-by-one behavior with edge/corner tests.

## Keyboard model

- Prefer scan-code events for physical key semantics.
- Send extended-key flag where required.
- Unicode text insertion is a separate explicit message for typed text, not a replacement for shortcut keys.
- Track remotely pressed keys.
- Release all remote keys on disconnect/revoke/focus loss.
- IME behavior requires Korean/English/Japanese compatibility tests.

## UIPI and elevation

`SendInput` is subject to Windows integrity restrictions. The client must expose capability status and not pretend an event succeeded when target integrity prevents it. Installed mode may use an authorized elevated helper, but the helper validates session and permission on every request.

## Clipboard

Initial supported format: Unicode text.

Controls:

- separate direction scopes;
- maximum byte length;
- content hash and revision to prevent loops;
- optional enterprise regex/DLP hook;
- no logging of content;
- clear error when clipboard is locked by another application;
- never sync credentials automatically from protected password controls.

Rich text, images and file-drop clipboard formats are post-GA capabilities with separate threat review.
