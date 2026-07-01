[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$venv = Join-Path $root '.tools\python'
$python = Join-Path $venv 'Scripts\python.exe'
$stamp = Join-Path $venv '.requirements-sha256'
$lock = Join-Path $PSScriptRoot 'requirements.lock.txt'
$requiredHash = (Get-FileHash -Algorithm SHA256 $lock).Hash.ToLowerInvariant()

if (-not (Test-Path $python)) {
    python -m venv $venv
}
$installedHash = if (Test-Path $stamp) { (Get-Content -Raw $stamp).Trim() } else { '' }
if ($installedHash -ne $requiredHash) {
    & $python -m pip install --disable-pip-version-check --require-hashes -r $lock | Out-Host
    Set-Content -LiteralPath $stamp -Value $requiredHash -Encoding ascii -NoNewline
}
Write-Output $python
