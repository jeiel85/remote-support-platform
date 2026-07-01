[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$sdkVersion = '10.0.301'
$archiveHash = '38456e992c4df0ff0ac9fc5f28ff09a88543c0fc4e4deedffda9c4ebaf852c4519addacf28814ea77ea42ce2d37db812fae5ba1fe25f06364ca5a6027036387f'
$root = Split-Path -Parent $PSScriptRoot
$installDir = Join-Path $root '.tools\dotnet'
$dotnet = Join-Path $installDir 'dotnet.exe'

if (Test-Path $dotnet) {
    $installed = (& $dotnet --version).Trim()
    if ($installed -eq $sdkVersion) {
        Write-Output $dotnet
        return
    }
    throw "Unexpected local .NET SDK $installed in $installDir; expected $sdkVersion."
}

$downloadDir = Join-Path $root '.tools\downloads'
$archive = Join-Path $downloadDir "dotnet-sdk-$sdkVersion-win-x64.zip"
New-Item -ItemType Directory -Force -Path $downloadDir | Out-Null
if (-not (Test-Path $archive)) {
    $uri = "https://builds.dotnet.microsoft.com/dotnet/Sdk/$sdkVersion/dotnet-sdk-$sdkVersion-win-x64.zip"
    Invoke-WebRequest -UseBasicParsing $uri -OutFile $archive
}

$actualHash = (Get-FileHash -Algorithm SHA512 $archive).Hash.ToLowerInvariant()
if ($actualHash -ne $archiveHash) {
    throw "SHA-512 mismatch for $archive."
}

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Expand-Archive -LiteralPath $archive -DestinationPath $installDir
$installed = (& $dotnet --version).Trim()
if ($installed -ne $sdkVersion) {
    throw "Installed .NET SDK $installed; expected $sdkVersion."
}
Write-Output $dotnet

