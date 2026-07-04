[CmdletBinding()]
param(
    [string]$PackageDirectory = './artifacts/packages/attended',
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$packages = [IO.Path]::GetFullPath((Join-Path $root $PackageDirectory))
$testRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) "remote-support-package-test/$Architecture"))
$allowed = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'remote-support-package-test')) + [IO.Path]::DirectorySeparatorChar
if (-not $testRoot.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package test root escaped artifacts.' }
if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$setup = Get-ChildItem -LiteralPath $packages -Filter "RemoteSupport-Operator-Setup-*-$Architecture.exe" | Select-Object -First 1
if ($null -eq $setup) { throw "Operator setup for $Architecture was not found." }
$previousTestRoot = $env:RS_SETUP_TEST_ROOT
try {
    $env:RS_SETUP_TEST_ROOT = $testRoot
    & $setup.FullName install
    if ($LASTEXITCODE -ne 0) { throw 'Operator setup install failed.' }
    $operator = Join-Path $testRoot 'Programs/RemoteSupport/Operator/RemoteSupport.Operator.Console.exe'
    if (-not (Test-Path -LiteralPath $operator)) { throw 'Installed Operator Console is missing.' }
    $operatorProcess = Start-Process -FilePath $operator -ArgumentList '--smoke-test' -WindowStyle Hidden -PassThru
    if (-not $operatorProcess.WaitForExit(15000)) { $operatorProcess.Kill(); throw 'Installed Operator Console smoke test timed out.' }
    if ($operatorProcess.ExitCode -ne 0) { throw "Installed Operator Console smoke test failed: $($operatorProcess.ExitCode)" }
    if (-not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $operator) 'RemoteSupport.Updater.exe'))) {
        throw 'Installed secure updater is missing.'
    }
    $statePath = Join-Path $testRoot 'RemoteSupport/operator-install.json'
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $originalSequence = $state.releaseSequence
    $state.releaseSequence = [long]$originalSequence + 1
    $state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statePath -Encoding utf8NoBOM
    & $setup.FullName install
    if ($LASTEXITCODE -eq 0) { throw 'Operator setup allowed a downgrade.' }
    $state.releaseSequence = $originalSequence
    $state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statePath -Encoding utf8NoBOM
    & $setup.FullName stage
    if ($LASTEXITCODE -ne 0) { throw 'Transactional update staging failed.' }
    $pending = Join-Path $testRoot 'RemoteSupport/operator-update-pending.json'
    if (-not (Test-Path -LiteralPath $pending)) { throw 'Transactional update did not persist pending health state.' }
    & $setup.FullName rollback
    if ($LASTEXITCODE -ne 0 -or (Test-Path -LiteralPath $pending)) { throw 'Transactional rollback failed.' }
    & $setup.FullName stage
    if ($LASTEXITCODE -ne 0) { throw 'Second transactional update staging failed.' }
    & $setup.FullName commit
    if ($LASTEXITCODE -ne 0 -or (Test-Path -LiteralPath $pending)) { throw 'Transactional update commit failed.' }
    & $setup.FullName repair
    if ($LASTEXITCODE -ne 0) { throw 'Operator setup repair failed.' }
    & $setup.FullName uninstall
    if ($LASTEXITCODE -ne 0) { throw 'Operator setup uninstall failed.' }
    if (Test-Path -LiteralPath (Split-Path -Parent $operator)) { throw 'Operator setup did not uninstall cleanly.' }
    $agentArchive = Get-ChildItem -LiteralPath $packages -Filter "RemoteSupport-Agent-*-$Architecture.zip" | Select-Object -First 1
    $agentRoot = Join-Path $testRoot 'agent-smoke'
    Expand-Archive -LiteralPath $agentArchive.FullName -DestinationPath $agentRoot
    $agentProcess = Start-Process -FilePath (Join-Path $agentRoot 'RemoteSupport.Agent.exe') -ArgumentList '--smoke-test' -WindowStyle Hidden -PassThru
    if (-not $agentProcess.WaitForExit(15000)) { $agentProcess.Kill(); throw 'Portable Agent smoke test timed out.' }
    if ($agentProcess.ExitCode -ne 0) { throw "Portable Agent smoke test failed: $($agentProcess.ExitCode)" }
} finally {
    $env:RS_SETUP_TEST_ROOT = $previousTestRoot
}
Write-Host 'Operator install, downgrade protection, repair, and clean uninstall passed in an isolated per-user profile.'
