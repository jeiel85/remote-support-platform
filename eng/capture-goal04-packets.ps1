[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = './artifacts/evidence/goal-04'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
$test = Join-Path $root "artifacts/native/$Configuration/transport_integration_test.exe"
if (-not (Test-Path $test)) { throw "Build transport_integration_test first: $test" }

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Packet Monitor requires an elevated PowerShell session. No capture was started.'
}

New-Item -ItemType Directory -Force $output | Out-Null
$etl = Join-Path $output 'lan-session.etl'
$pcap = Join-Path $output 'lan-session.pcapng'
& pktmon start --capture --comp all --pkt-size 0 --file-name $etl --file-size 64
try {
    & $test
    if ($LASTEXITCODE -ne 0) { throw "Transport integration test failed with exit code $LASTEXITCODE." }
} finally {
    & pktmon stop | Out-Null
}
& pktmon etl2pcap $etl --out $pcap
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $pcap)) { throw 'Packet capture conversion failed.' }
Write-Output $pcap
