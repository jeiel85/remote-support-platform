# Portable Attended Agent

## Responsibilities

- create pending attended session;
- display short-lived code and expiry;
- show operator identity and requested scopes;
- collect explicit consent;
- maintain persistent session banner/indicator;
- capture selected monitor/window;
- receive authorized input;
- mediate clipboard, files and chat;
- expose local pause, scope revoke and disconnect;
- remove ephemeral secrets on exit.

## Startup sequence

1. Verify own embedded resources and native dependency load.
2. Check OS/build, GPU and capture capability.
3. Generate ephemeral session identity key.
4. Create pending session through HTTPS.
5. Display code; never copy automatically to clipboard without user action.
6. Await consent request.
7. Show identity and scope dialog.
8. On approval, initialize media and peer transport.
9. On exit, terminate session and zero ephemeral secrets.

## UI states

- `Initializing`
- `CodeReady`
- `OperatorRequestPending`
- `ConsentDialog`
- `Connecting`
- `ConnectedViewOnly`
- `ConnectedControlled`
- `Paused`
- `LocalActionRequired`
- `Disconnected`
- `Error`

## Mandatory UX

- Product publisher and support organization are visible.
- Access scopes use plain language.
- The disconnect control remains reachable on every monitor.
- Local pointer/keyboard use is never blocked unless a narrowly defined enterprise policy exists.
- Emergency hotkey: configurable but defaults to a collision-resistant chord; it must always terminate or pause remote control.
- A portable agent cannot enable unattended access.

## Local storage

Portable mode stores only:

- non-sensitive UI preferences;
- crash consent preference;
- last used language.

It must not store refresh tokens, organization credentials or persistent device identity.
