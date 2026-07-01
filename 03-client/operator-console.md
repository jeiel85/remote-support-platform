# Operator Console

## Modules

- authentication and tenant selection;
- support-code entry;
- device browser for authorized managed hosts;
- remote viewport and input router;
- monitor selector and quality controls;
- permission/status bar;
- file transfer and chat panels;
- session diagnostics;
- audit-safe session notes metadata;
- update and support bundle tools.

## Viewport coordinate pipeline

```text
WPF pointer position
→ viewport content rectangle
→ inverse zoom/pan transform
→ selected display pixel coordinate
→ display topology offset
→ canonical virtual desktop coordinate
→ protocol PointerEvent
```

Every mapping function is pure and unit-tested with negative virtual origins, rotated monitors and mixed DPI.

## Input capture rules

- Input is captured only while viewport focus and control scope are active.
- Global operator keyboard hooks are prohibited unless a reviewed feature requires them.
- Reserved local shortcuts remain local.
- Clipboard sync is user-triggered or policy-controlled; avoid feedback loops through content hashes/revisions.
- On viewport focus loss, send remote release-all-input-state.

## Diagnostics panel

Show without exposing secrets:

- direct vs relay route;
- relay region/transport;
- RTT, packet loss, jitter;
- send/receive bitrate;
- capture/encode/decode/render latency estimates;
- selected codec and resolution;
- current scopes and capability limitations;
- session correlation ID.

## Operator safety

- Unattended sessions show a stronger visual state.
- High-risk actions such as reboot require confirmation and permission.
- Console never provides arbitrary hidden script execution in initial product.
- Session termination is always available even when the viewport is unresponsive.
