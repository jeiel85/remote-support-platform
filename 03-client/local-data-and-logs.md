# Local Data and Logging

## Directory layout

```text
%ProgramData%/RemoteSupport/
  config/
  state/
  updates/
  logs/
  crash/

%LocalAppData%/RemoteSupport/
  operator-preferences/
  ui-cache/
```

## Data protection

- Machine identity secrets: machine-scoped protected storage or non-exportable key provider.
- User refresh tokens: user-scoped protected storage.
- No tokens in command line, environment variables or crash metadata.
- Files use restrictive ACLs and atomic writes.

## Logging policy

Allowed:

- timestamps, versions, module, stable error code;
- session correlation ID;
- route class and relay region;
- performance counters;
- display dimensions and non-identifying capability state.

Prohibited:

- access/refresh/TURN credentials;
- support code after resolution;
- screen pixels/OCR;
- clipboard/chat/file contents;
- keystrokes;
- private keys;
- raw authorization headers;
- full SDP in normal logs.

## Support bundle

A user-triggered support bundle:

- previews included files and categories;
- excludes content data and secrets;
- can optionally include a short, explicit diagnostic capture only with separate consent;
- encrypts upload and expires quickly;
- records who requested and accessed it.
