# Attended client configuration

Copy `client-config.beta.json` beside each packaged Agent and Operator Console
executable as `client-config.json`, then replace every `.example` endpoint and
the OIDC client ID with values from the target environment. HTTPS/WSS endpoints
are mandatory outside loopback test configurations.

The Operator OIDC application is a public native client. Register the loopback
redirect URI selected by the system-browser PKCE flow, require authorization
code plus PKCE, and do not issue a client secret to the desktop application.
The sample update channel is `internal`; production metadata must be signed by
the threshold keys in the separately provisioned bootstrap root.

The Operator payload includes `RemoteSupport.Updater.exe`. Provision the
approved bootstrap `update-root.json` and a random stable rollout identity in
the product's protected data directory. Download metadata/artifacts over bounded
HTTPS into a private staging directory, then invoke the updater with the exact
product/channel/architecture binding. On startup, call `recover` before the next
check if update state contains a pending transaction. Do not copy a development
root or signing key into a production package.

Build runnable development artifacts with:

```powershell
./build.ps1 -Target Package -Configuration Release
./eng/test-attended-package.ps1 -Architecture x64
```

Production packaging additionally requires `RS_SIGN_CERT_THUMBPRINT` and the
release signing mode. The build deliberately fails if a requested architecture
does not have its matching native runtime.
