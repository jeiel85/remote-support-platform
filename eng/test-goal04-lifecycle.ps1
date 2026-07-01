[CmdletBinding()]
param(
    [ValidateRange(1, 1000)]
    [int]$Cycles = 100,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Output = './artifacts/evidence/goal-04/lifecycle.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$test = Join-Path $root "artifacts/native/$Configuration/transport_integration_test.exe"
if (-not (Test-Path $test)) { throw "Build transport_integration_test first: $test" }
$started = [DateTimeOffset]::UtcNow
for ($cycle = 1; $cycle -le $Cycles; $cycle++) {
    & $test
    if ($LASTEXITCODE -ne 0) { throw "LAN transport lifecycle cycle $cycle failed with exit code $LASTEXITCODE." }
}
$finished = [DateTimeOffset]::UtcNow
$reportPath = [IO.Path]::GetFullPath((Join-Path $root $Output))
New-Item -ItemType Directory -Force (Split-Path -Parent $reportPath) | Out-Null
[ordered]@{
    schemaVersion = 1
    testId = 'AT-FR-NET-001-lifecycle'
    configuration = $Configuration
    cycles = $Cycles
    startedAt = $started.ToString('O')
    finishedAt = $finished.ToString('O')
    durationSeconds = [Math]::Round(($finished - $started).TotalSeconds, 3)
    passed = $true
} | ConvertTo-Json | Set-Content -Encoding utf8 $reportPath
Write-Output $reportPath
