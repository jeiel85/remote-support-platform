[CmdletBinding()]
param(
    [Parameter(Mandatory)][string[]]$Paths,
    [Parameter(Mandatory)][string]$CertificateThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
if ([string]::IsNullOrWhiteSpace($signtool)) { throw 'Windows SDK signtool.exe was not found.' }
if ($CertificateThumbprint -notmatch '^[A-Fa-f0-9]{40,128}$') { throw 'Certificate thumbprint is invalid.' }
foreach ($path in $Paths) {
    $resolved = (Resolve-Path -LiteralPath $path).Path
    if ([IO.Path]::GetExtension($resolved) -in @('.exe', '.dll')) {
        & $signtool sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $resolved
        & $signtool verify /pa /all $resolved
    }
}
