[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$root = Split-Path -Parent $PSScriptRoot
$version = '3.0.260520.1'
$compiler = Join-Path $root ".tools\nuget\microsoft.windows.cppwinrt\$version\bin\cppwinrt.exe"
$output = Join-Path $root '.tools\cppwinrt'
$stamp = Join-Path $output '.version'
if (-not (Test-Path $compiler)) {
    throw 'Microsoft.Windows.CppWinRT is not restored.'
}
if ((Test-Path $stamp) -and (Get-Content -Raw $stamp).Trim() -eq $version) {
    Write-Output $output
    return
}
New-Item -ItemType Directory -Force -Path $output | Out-Null
& $compiler -input local -output $output -base
Set-Content -LiteralPath $stamp -Value $version -Encoding ascii -NoNewline
Write-Output $output

