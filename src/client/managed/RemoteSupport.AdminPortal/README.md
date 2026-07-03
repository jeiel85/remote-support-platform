# Admin Portal

The Goal 10 portal is an independently deployable ASP.NET Core Blazor BFF. It
uses server-rendered components; OIDC authorization-code/PKCE and the reusable
API access token terminate on the server. The browser receives only secure,
HTTP-only authentication and tenant-selection session cookies.

Required production configuration:

- `ControlPlane__BaseUrl`
- `Oidc__Authority`
- `Oidc__ClientId`
- `Oidc__ClientSecret` from a secret provider

The selected tenant ID is stored in the encrypted server-side session and is
revalidated through the API before use. Every state-changing form carries an
anti-forgery token and is protected by same-origin validation, CSP and role/MFA
checks in the control plane.

```powershell
$dotnet = ./eng/bootstrap-dotnet.ps1
& $dotnet run --project src/client/managed/RemoteSupport.AdminPortal
```
