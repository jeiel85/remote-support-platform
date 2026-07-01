[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Destination
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$destinationPath = [System.IO.Path]::GetFullPath($Destination)
if (Test-Path $destinationPath) {
    $existing = Get-ChildItem -Force $destinationPath
    if ($existing.Count -gt 0) {
        throw "Destination must be empty: $destinationPath"
    }
} else {
    New-Item -ItemType Directory -Path $destinationPath | Out-Null
}

$dotnetVersion = (& dotnet --version).Trim()
if ([string]::IsNullOrWhiteSpace($dotnetVersion)) {
    throw '.NET SDK was not found.'
}

$directories = @(
    'docs/architecture', 'docs/security', 'docs/runbooks', 'docs/adr',
    'src/client/managed', 'src/client/native', 'src/client/installer',
    'src/server', 'src/portal', 'src/shared-contracts',
    'deploy/local', 'deploy/staging', 'deploy/production', 'deploy/turn', 'deploy/observability',
    'tests/e2e', 'tests/compatibility', 'tests/performance', 'tests/security',
    'tools/lab-controller', 'tools/protocol-fuzzer', 'tools/support-bundle-validator', 'tools/release-verifier',
    'schemas/openapi', 'schemas/protobuf', 'schemas/config', 'schemas/events',
    'artifacts', 'LICENSES'
)
foreach ($dir in $directories) {
    New-Item -ItemType Directory -Force -Path (Join-Path $destinationPath $dir) | Out-Null
}

Push-Location $destinationPath
try {
    & dotnet new globaljson --sdk-version $dotnetVersion --roll-forward latestPatch | Out-Null
    & dotnet new sln -n RemoteSupport | Out-Null

    $projects = @(
        @{ Template = 'classlib'; Name = 'RemoteSupport.Domain'; Path = 'src/client/managed/RemoteSupport.Domain' },
        @{ Template = 'classlib'; Name = 'RemoteSupport.Application'; Path = 'src/client/managed/RemoteSupport.Application' },
        @{ Template = 'classlib'; Name = 'RemoteSupport.Infrastructure'; Path = 'src/client/managed/RemoteSupport.Infrastructure' },
        @{ Template = 'classlib'; Name = 'RemoteSupport.Protocol'; Path = 'src/client/managed/RemoteSupport.Protocol' },
        @{ Template = 'classlib'; Name = 'RemoteSupport.Ipc'; Path = 'src/client/managed/RemoteSupport.Ipc' },
        @{ Template = 'classlib'; Name = 'RemoteSupport.Security'; Path = 'src/client/managed/RemoteSupport.Security' },
        @{ Template = 'classlib'; Name = 'RemoteSupport.Observability'; Path = 'src/client/managed/RemoteSupport.Observability' },
        @{ Template = 'wpf'; Name = 'RemoteSupport.Agent.App'; Path = 'src/client/managed/RemoteSupport.Agent.App' },
        @{ Template = 'wpf'; Name = 'RemoteSupport.Console.App'; Path = 'src/client/managed/RemoteSupport.Console.App' },
        @{ Template = 'worker'; Name = 'RemoteSupport.Service'; Path = 'src/client/managed/RemoteSupport.Service' },
        @{ Template = 'console'; Name = 'RemoteSupport.Updater'; Path = 'src/client/managed/RemoteSupport.Updater' },
        @{ Template = 'webapi'; Name = 'RemoteSupport.ApiHost'; Path = 'src/server/RemoteSupport.ApiHost' },
        @{ Template = 'classlib'; Name = 'RemoteSupport.Server.Modules'; Path = 'src/server/RemoteSupport.Server.Modules' },
        @{ Template = 'blazor'; Name = 'RemoteSupport.AdminPortal'; Path = 'src/portal/RemoteSupport.AdminPortal'; Extra = @('--interactivity', 'Server') },
        @{ Template = 'xunit'; Name = 'RemoteSupport.UnitTests'; Path = 'tests/RemoteSupport.UnitTests' },
        @{ Template = 'xunit'; Name = 'RemoteSupport.ContractTests'; Path = 'tests/RemoteSupport.ContractTests' },
        @{ Template = 'xunit'; Name = 'RemoteSupport.IntegrationTests'; Path = 'tests/RemoteSupport.IntegrationTests' }
    )

    foreach ($project in $projects) {
        $newArgs = @('new', $project.Template, '-n', $project.Name, '-o', $project.Path)
        if ($project.ContainsKey('Extra')) { $newArgs += $project.Extra }
        & dotnet @newArgs | Out-Null
    }

    Get-ChildItem -Recurse -Filter *.csproj | ForEach-Object {
        & dotnet sln RemoteSupport.sln add $_.FullName | Out-Null
    }

    & dotnet add 'src/client/managed/RemoteSupport.Application/RemoteSupport.Application.csproj' reference `
        'src/client/managed/RemoteSupport.Domain/RemoteSupport.Domain.csproj' | Out-Null
    & dotnet add 'src/client/managed/RemoteSupport.Infrastructure/RemoteSupport.Infrastructure.csproj' reference `
        'src/client/managed/RemoteSupport.Application/RemoteSupport.Application.csproj' `
        'src/client/managed/RemoteSupport.Protocol/RemoteSupport.Protocol.csproj' `
        'src/client/managed/RemoteSupport.Ipc/RemoteSupport.Ipc.csproj' `
        'src/client/managed/RemoteSupport.Security/RemoteSupport.Security.csproj' `
        'src/client/managed/RemoteSupport.Observability/RemoteSupport.Observability.csproj' | Out-Null
    & dotnet add 'src/client/managed/RemoteSupport.Agent.App/RemoteSupport.Agent.App.csproj' reference `
        'src/client/managed/RemoteSupport.Application/RemoteSupport.Application.csproj' `
        'src/client/managed/RemoteSupport.Infrastructure/RemoteSupport.Infrastructure.csproj' | Out-Null
    & dotnet add 'src/client/managed/RemoteSupport.Console.App/RemoteSupport.Console.App.csproj' reference `
        'src/client/managed/RemoteSupport.Application/RemoteSupport.Application.csproj' `
        'src/client/managed/RemoteSupport.Infrastructure/RemoteSupport.Infrastructure.csproj' | Out-Null
    & dotnet add 'src/client/managed/RemoteSupport.Service/RemoteSupport.Service.csproj' reference `
        'src/client/managed/RemoteSupport.Ipc/RemoteSupport.Ipc.csproj' `
        'src/client/managed/RemoteSupport.Security/RemoteSupport.Security.csproj' `
        'src/client/managed/RemoteSupport.Observability/RemoteSupport.Observability.csproj' | Out-Null
    & dotnet add 'src/server/RemoteSupport.ApiHost/RemoteSupport.ApiHost.csproj' reference `
        'src/server/RemoteSupport.Server.Modules/RemoteSupport.Server.Modules.csproj' | Out-Null

    $contractSource = Join-Path $PSScriptRoot '..\02-protocol'
    Copy-Item (Join-Path $contractSource 'openapi\openapi.yaml') 'schemas/openapi/openapi.yaml'
    Copy-Item (Join-Path $contractSource 'protobuf\remote_support.proto') 'schemas/protobuf/remote_support.proto'
    Copy-Item (Join-Path $contractSource 'ipc\service_ipc.proto') 'schemas/protobuf/service_ipc.proto'
    Copy-Item (Join-Path $contractSource 'schemas\*.json') 'schemas/config/'

    @'
<Project>
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
    <Deterministic>true</Deterministic>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
    <NuGetAudit>true</NuGetAudit>
  </PropertyGroup>
</Project>
'@ | Set-Content -Encoding UTF8 'Directory.Build.props'

    @'
param(
    [ValidateSet("Restore", "Build", "Test")]
    [string]$Target = "Build",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)
$ErrorActionPreference = "Stop"
switch ($Target) {
    "Restore" { dotnet restore RemoteSupport.sln --use-lock-file }
    "Build"   { dotnet build RemoteSupport.sln -c $Configuration --no-restore }
    "Test"    { dotnet test RemoteSupport.sln -c $Configuration --no-build }
}
'@ | Set-Content -Encoding UTF8 'build.ps1'

    @'
# Remote Support Platform

Generated repository skeleton. Before feature work, copy the full design bundle into `docs/design-bundle` and execute Goal 01.
'@ | Set-Content -Encoding UTF8 'README.md'

    & dotnet restore RemoteSupport.sln --use-lock-file | Out-Null
    & dotnet build RemoteSupport.sln -c Debug --no-restore | Out-Null
    Write-Host "Repository initialized and baseline build completed: $destinationPath"
}
finally {
    Pop-Location
}
