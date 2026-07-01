[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$toolRoot = Join-Path $root '.tools'
$dataChannelRoot = Join-Path $toolRoot 'libdatachannel-src'
$dataChannelCommit = 'c6696d157b5612df2a741d9a03b192b47ab6cefb'
$mbedVersion = '3.6.6'
$mbedHash = '8FB65FAE8DCAE5840F793C0A334860A411F884CC537EA290CE1C52BB64CA007A'
$mbedRoot = Join-Path $toolRoot "mbedtls-$mbedVersion"
$downloadRoot = Join-Path $toolRoot 'downloads'
$mbedArchive = Join-Path $downloadRoot "mbedtls-$mbedVersion.tar.bz2"

New-Item -ItemType Directory -Force $toolRoot, $downloadRoot | Out-Null

if (-not (Test-Path (Join-Path $dataChannelRoot '.git'))) {
    & git clone --branch v0.24.3 --depth 1 --recurse-submodules --shallow-submodules `
        https://github.com/paullouisageneau/libdatachannel.git $dataChannelRoot | Out-Null
}
$actualCommit = (& git -C $dataChannelRoot rev-parse HEAD).Trim()
if ($actualCommit -ne $dataChannelCommit) {
    throw "libdatachannel revision mismatch. Expected $dataChannelCommit, found $actualCommit."
}
& git -C $dataChannelRoot submodule update --init --recursive --depth 1 | Out-Null

if (-not (Test-Path (Join-Path $mbedRoot 'CMakeLists.txt'))) {
    if (-not (Test-Path $mbedArchive) -or (Get-FileHash $mbedArchive -Algorithm SHA256).Hash -ne $mbedHash) {
        Invoke-WebRequest `
            -Uri "https://github.com/Mbed-TLS/mbedtls/releases/download/mbedtls-$mbedVersion/mbedtls-$mbedVersion.tar.bz2" `
            -OutFile $mbedArchive
    }
    $actualHash = (Get-FileHash $mbedArchive -Algorithm SHA256).Hash
    if ($actualHash -ne $mbedHash) {
        throw "Mbed TLS archive hash mismatch. Expected $mbedHash, found $actualHash."
    }
    & tar -xjf $mbedArchive -C $toolRoot | Out-Null
}

[pscustomobject]@{
    LibDataChannelRoot = $dataChannelRoot
    MbedTlsRoot = $mbedRoot
}
