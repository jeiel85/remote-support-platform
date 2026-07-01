# Client Solution Structure

```text
src/client/
├─ RemoteSupport.sln
├─ Directory.Build.props
├─ Directory.Packages.props
├─ global.json
├─ managed/
│  ├─ RemoteSupport.Agent.App/
│  ├─ RemoteSupport.Console.App/
│  ├─ RemoteSupport.Service/
│  ├─ RemoteSupport.Updater/
│  ├─ RemoteSupport.Application/
│  ├─ RemoteSupport.Domain/
│  ├─ RemoteSupport.Infrastructure/
│  ├─ RemoteSupport.Protocol/
│  ├─ RemoteSupport.Ipc/
│  ├─ RemoteSupport.Security/
│  ├─ RemoteSupport.Observability/
│  └─ RemoteSupport.Testing/
├─ native/
│  ├─ media-core/
│  ├─ capture-dxgi/
│  ├─ capture-wgc/
│  ├─ encoder-mf/
│  ├─ webrtc-transport/
│  ├─ d3d-renderer/
│  ├─ input-win32/
│  └─ native-bridge/
├─ installer/
│  ├─ msi/
│  ├─ bootstrapper/
│  └─ signing/
└─ tests/
   ├─ Unit/
   ├─ Integration/
   ├─ Native/
   ├─ Protocol/
   ├─ UIAutomation/
   ├─ Compatibility/
   └─ Performance/
```

## Layer rules

- `Domain` has no WPF, HTTP, Win32 or database dependencies.
- `Application` contains use cases and ports.
- `Infrastructure` implements network/storage/OS ports.
- UI projects depend on Application and presentation adapters.
- Native DLLs expose only the versioned bridge API.
- Service and Agent share protocol contracts but not privileged implementation details.

## Build targets

- `win-x64`: mandatory.
- `win-arm64`: gated by native WebRTC and Media Foundation validation.
- Self-contained .NET deployment for deterministic client runtime.
- Native binaries built with control-flow protection, ASLR, DEP and release symbols retained privately.

## Package policy

- Central package version management.
- Lock files committed.
- No package with unclear license or abandoned critical security role.
- Dependency update PRs require tests and SBOM diff.
