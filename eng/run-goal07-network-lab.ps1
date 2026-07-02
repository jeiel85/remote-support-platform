[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$ProbePath,
    [Parameter(Mandatory)] [ValidateSet('direct', 'turn-udp', 'turn-tcp', 'turn-tls')] [string]$Route,
    [string]$TurnUrl,
    [string]$Username,
    [string]$Credential,
    [string]$EvidencePath = './artifacts/network/goal-07-route.json',
    [switch]$CheckInvalidCredential
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$probe = (Resolve-Path $ProbePath).Path
if (-not $probe.StartsWith((Resolve-Path $root).Path, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The route probe must be a build artifact from this workspace.'
}
if ($Route -ne 'direct' -and
    ([string]::IsNullOrWhiteSpace($TurnUrl) -or [string]::IsNullOrWhiteSpace($Username) -or
     [string]::IsNullOrWhiteSpace($Credential))) {
    throw 'TURN route probes require URL, username, and time-limited credential.'
}

$started = [DateTimeOffset]::UtcNow
$timer = [Diagnostics.Stopwatch]::StartNew()
try {
    if ($Route -eq 'direct') {
        & $probe normal
    } else {
        $env:RS_TEST_TURN_URL = $TurnUrl
        $env:RS_TEST_TURN_USERNAME = $Username
        $env:RS_TEST_TURN_CREDENTIAL = $Credential
        & $probe $Route
    }
    if ($LASTEXITCODE -ne 0) { throw "Route probe failed with exit code $LASTEXITCODE." }
    $timer.Stop()

    if ($CheckInvalidCredential -and $Route -ne 'direct') {
        $env:RS_TEST_TURN_CREDENTIAL = 'intentionally-invalid-credential'
        & $probe $Route *> $null
        if ($LASTEXITCODE -eq 0) { throw 'Invalid TURN credential unexpectedly established a route.' }
    }
} finally {
    Remove-Item Env:RS_TEST_TURN_URL -ErrorAction SilentlyContinue
    Remove-Item Env:RS_TEST_TURN_USERNAME -ErrorAction SilentlyContinue
    Remove-Item Env:RS_TEST_TURN_CREDENTIAL -ErrorAction SilentlyContinue
}

$target = Join-Path $root $EvidencePath
$directory = Split-Path -Parent $target
New-Item -ItemType Directory -Force $directory | Out-Null
[pscustomobject]@{
    schemaVersion = 1
    route = $Route
    passed = $true
    invalidCredentialRejected = [bool]$CheckInvalidCredential
    startedAt = $started.ToString('O')
    elapsedMilliseconds = $timer.ElapsedMilliseconds
    probeSha256 = (Get-FileHash $probe -Algorithm SHA256).Hash
    secretsPersisted = $false
} | ConvertTo-Json | Set-Content -LiteralPath $target -Encoding utf8NoBOM
Write-Host "Goal 07 route probe passed: $Route ($($timer.ElapsedMilliseconds) ms)"
