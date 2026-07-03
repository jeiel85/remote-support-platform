[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [ValidateSet('x64', 'arm64')][string[]]$Architectures = @('x64'),
    [string]$Version = '0.9.0-beta.1',
    [long]$ReleaseSequence = 900001,
    [string]$OutputDirectory = './artifacts/packages/attended',
    [switch]$RequireSigning
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
$dotnet = & (Join-Path $root 'eng/bootstrap-dotnet.ps1')
$python = & (Join-Path $root 'eng/ensure-python-tools.ps1')
$config = Join-Path $root 'deploy/client/client-config.beta.json'
$signer = $env:RS_SIGN_CERT_THUMBPRINT
if ($RequireSigning -and [string]::IsNullOrWhiteSpace($signer)) {
    throw 'RS_SIGN_CERT_THUMBPRINT is required for a signed beta package.'
}

New-Item -ItemType Directory -Force -Path $output | Out-Null
$releaseArtifacts = @()
foreach ($architecture in $Architectures) {
    $rid = "win-$architecture"
    $work = Join-Path $root "artifacts/package-work/$architecture"
    if (-not ([IO.Path]::GetFullPath($work)).StartsWith(([IO.Path]::GetFullPath((Join-Path $root 'artifacts/package-work')) + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Package work directory escaped the workspace artifact root.'
    }
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
    $agentPublish = Join-Path $work 'agent'
    $operatorPublish = Join-Path $work 'operator'
    New-Item -ItemType Directory -Force -Path $agentPublish, $operatorPublish | Out-Null

    $native = if ($architecture -eq 'x64') {
        Join-Path $root "artifacts/native/$Configuration/remote_support_native.dll"
    } else {
        Join-Path $root "artifacts/native/arm64/$Configuration/remote_support_native.dll"
    }
    if (-not (Test-Path -LiteralPath $native)) {
        throw "Native $architecture runtime is missing: $native. Build it on the matching protected worker."
    }

    & $dotnet publish (Join-Path $root 'src/client/managed/RemoteSupport.Agent.App/RemoteSupport.Agent.App.csproj') `
        -c $Configuration -r $rid --self-contained true -p:PublishSingleFile=false -p:Version=$Version -o $agentPublish
    & $dotnet publish (Join-Path $root 'src/client/managed/RemoteSupport.LocalViewer.App/RemoteSupport.LocalViewer.App.csproj') `
        -c $Configuration -r $rid --self-contained true -p:PublishSingleFile=false -p:Version=$Version -o $operatorPublish
    Copy-Item -LiteralPath $native -Destination (Join-Path $agentPublish 'remote_support_native.dll') -Force
    Copy-Item -LiteralPath $native -Destination (Join-Path $operatorPublish 'remote_support_native.dll') -Force
    Copy-Item -LiteralPath $config -Destination (Join-Path $agentPublish 'client-config.json') -Force
    Copy-Item -LiteralPath $config -Destination (Join-Path $operatorPublish 'client-config.json') -Force
    if (-not [string]::IsNullOrWhiteSpace($signer)) {
        $binaries = @(Get-ChildItem -LiteralPath $agentPublish, $operatorPublish -Recurse -File |
            Where-Object { $_.Extension -in @('.exe', '.dll') -and ($_.Name -like 'RemoteSupport*' -or $_.Name -eq 'remote_support_native.dll') } |
            Select-Object -ExpandProperty FullName)
        & (Join-Path $root 'eng/sign-artifacts.ps1') -Paths $binaries -CertificateThumbprint $signer
    }

    $agentZip = Join-Path $output "RemoteSupport-Agent-$Version-$architecture.zip"
    $agentManifest = Join-Path $output "RemoteSupport-Agent-$Version-$architecture.manifest.json"
    & $python (Join-Path $root 'tools/packaging/make_payload.py') $root $agentPublish $agentZip $agentManifest `
        --product PORTABLE_AGENT --version $Version --sequence $ReleaseSequence --architecture $architecture

    $operatorZip = Join-Path $work 'operator-payload.zip'
    $operatorManifest = Join-Path $work 'operator-payload.json'
    & $python (Join-Path $root 'tools/packaging/make_payload.py') $root $operatorPublish $operatorZip $operatorManifest `
        --product OPERATOR_CONSOLE --version $Version --sequence $ReleaseSequence --architecture $architecture
    $setupPublish = Join-Path $work 'setup'
    & $dotnet publish (Join-Path $root 'src/client/managed/RemoteSupport.Operator.Setup/RemoteSupport.Operator.Setup.csproj') `
        -c $Configuration -r $rid --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
        -p:Version=$Version "-p:PayloadZip=$operatorZip" "-p:PayloadManifest=$operatorManifest" -o $setupPublish
    $setup = Join-Path $setupPublish 'RemoteSupport.Operator.Setup.exe'
    $setupOutput = Join-Path $output "RemoteSupport-Operator-Setup-$Version-$architecture.exe"
    Copy-Item -LiteralPath $setup -Destination $setupOutput -Force

    if (-not [string]::IsNullOrWhiteSpace($signer)) {
        & (Join-Path $root 'eng/sign-artifacts.ps1') -Paths @($setupOutput) -CertificateThumbprint $signer
    }
    $releaseArtifacts += @($agentZip, $agentManifest, $setupOutput)
}

$commit = (& git -C $root rev-parse HEAD).Trim()
$dirty = -not [string]::IsNullOrWhiteSpace((& git -C $root status --porcelain))
if ($dirty -and $RequireSigning) { throw 'Signed packaging requires a clean worktree.' }
$provenance = [ordered]@{
    schemaVersion = 1
    productVersion = $Version
    releaseSequence = $ReleaseSequence
    sourceCommit = $commit
    sourceDirty = $dirty
    architectures = $Architectures
    signed = -not [string]::IsNullOrWhiteSpace($signer)
    artifacts = @($releaseArtifacts | Sort-Object | ForEach-Object {
        [ordered]@{ name = Split-Path -Leaf $_; size = (Get-Item -LiteralPath $_).Length; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
}
$provenance | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'provenance.json') -Encoding utf8NoBOM
Write-Host "Attended packages written to $output"
