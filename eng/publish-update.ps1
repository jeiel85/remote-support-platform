[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('PORTABLE_AGENT', 'OPERATOR_CONSOLE', 'MANAGED_HOST')][string]$Product,
    [Parameter(Mandatory)][ValidateSet('internal', 'canary', 'stable')][string]$Channel,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][long]$ReleaseSequence,
    [long]$MinimumAllowedSequence = 0,
    [Parameter(Mandatory)][ValidateSet('x64', 'arm64')][string]$Architecture,
    [Parameter(Mandatory)][string]$Artifact,
    [Parameter(Mandatory)][uri]$ArtifactUrl,
    [Parameter(Mandatory)][int]$RootVersion,
    [Parameter(Mandatory)][string]$KeyId,
    [ValidateRange(0, 100)][int]$RolloutPercentage = 5,
    [ValidateRange(1, [int]::MaxValue)][int]$RolloutSeedVersion = 1,
    [string]$RolloutEvidence,
    [Parameter(Mandatory)][string]$Output
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = Split-Path -Parent $PSScriptRoot
$dotnet = & (Join-Path $root 'eng/bootstrap-dotnet.ps1')
$artifactPath = (Resolve-Path -LiteralPath $Artifact).Path
if ($ArtifactUrl.Scheme -ne 'https') { throw 'Update artifact URL must use HTTPS.' }
if ([string]::IsNullOrWhiteSpace($env:RS_UPDATE_SIGNING_KEY_BASE64URL)) {
    throw 'RS_UPDATE_SIGNING_KEY_BASE64URL is required from a protected metadata-signing job.'
}
if ($Channel -ne 'internal') {
    if ([string]::IsNullOrWhiteSpace($RolloutEvidence)) { throw 'Canary/stable publication requires rollout evidence.' }
    $python = & (Join-Path $root 'eng/ensure-python-tools.ps1')
    $gateArguments = @((Join-Path $root 'tools/operations/evaluate_update_rollout.py'),
        (Resolve-Path -LiteralPath $RolloutEvidence).Path, '--requested', $RolloutPercentage)
    if ($RolloutPercentage -eq 0) { $gateArguments += '--halt' }
    & $python @gateArguments
}
$signature = Get-AuthenticodeSignature -LiteralPath $artifactPath
if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate) {
    throw 'Update artifact must have a valid Authenticode signature before metadata publication.'
}
$packageType = if ($Product -eq 'OPERATOR_CONSOLE') { 'OPERATOR_INSTALLER' } elseif ($Product -eq 'MANAGED_HOST') { 'MANAGED_HOST_INSTALLER' } else { 'PORTABLE_AGENT' }
$now = [DateTimeOffset]::UtcNow
$body = [ordered]@{
    schemaVersion = 1
    product = $Product
    channel = $Channel
    version = $Version
    releaseSequence = $ReleaseSequence
    minimumAllowedSequence = $MinimumAllowedSequence
    rollout = [ordered]@{ percentage = $RolloutPercentage; seedVersion = $RolloutSeedVersion }
    issuedAt = $now.ToString('O')
    expiresAt = $now.AddDays(7).ToString('O')
    artifacts = @([ordered]@{
        architecture = $Architecture
        packageType = $packageType
        url = $ArtifactUrl.AbsoluteUri
        size = (Get-Item -LiteralPath $artifactPath).Length
        sha256 = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
        authenticodeSignerThumbprint = $signature.SignerCertificate.Thumbprint.ToUpperInvariant()
        minimumOsBuild = '10.0.19045'
    })
    rootVersion = $RootVersion
}
$temporary = Join-Path ([IO.Path]::GetTempPath()) ("rsp-update-" + [guid]::NewGuid().ToString('N') + '.json')
try {
    $body | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $temporary -Encoding utf8NoBOM
    & $dotnet run --project (Join-Path $root 'tools/update/RemoteSupport.UpdateTool/RemoteSupport.UpdateTool.csproj') `
        -c Release --no-restore -- sign --input $temporary --output ([IO.Path]::GetFullPath($Output)) --key-id $KeyId --type manifest
} finally {
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
}
