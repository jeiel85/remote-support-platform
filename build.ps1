[CmdletBinding()]
param(
    [ValidateSet('Bootstrap', 'ValidateDesign', 'Restore', 'GenerateContracts', 'Build', 'Test', 'HardwareTest', 'IntegrationTest', 'Package', 'VerifyRelease', 'CI')]
    [string]$Target = 'Build',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$Artifacts = './artifacts'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = Join-Path $root '.tools\nuget'
$dotnet = & (Join-Path $root 'eng\bootstrap-dotnet.ps1')
$python = & (Join-Path $root 'eng\ensure-python-tools.ps1')

function Invoke-Restore {
    & $dotnet restore (Join-Path $root 'RemoteSupport.sln') --locked-mode
}

function Invoke-GenerateContracts {
    & $python (Join-Path $root 'tools\contracts\generate_contracts.py') $root
}

function Invoke-NativeBuild {
    $cmake = Join-Path (Split-Path -Parent $python) 'cmake.exe'
    $cppwinrt = & (Join-Path $root 'eng\generate-cppwinrt.ps1')
    $webrtc = & (Join-Path $root 'eng\bootstrap-webrtc.ps1')
    $iceBackend = if ([string]::IsNullOrWhiteSpace($env:RS_ICE_BACKEND)) { 'libjuice' } else { $env:RS_ICE_BACKEND }
    if ($iceBackend -notin @('libjuice', 'libnice')) { throw 'RS_ICE_BACKEND must be libjuice or libnice.' }
    $nativeBuild = Join-Path $root "artifacts\native\$Configuration"
    & $cmake -S (Join-Path $root 'src\client\native') -B $nativeBuild -G 'MinGW Makefiles' "-DCMAKE_BUILD_TYPE=$Configuration" "-DCMAKE_CXX_USE_RESPONSE_FILE_FOR_OBJECTS=ON" "-DRS_CONTRACT_ROOT=$root" "-DRS_CPPWINRT_ROOT=$cppwinrt" "-DRS_LIBDATACHANNEL_ROOT=$($webrtc.LibDataChannelRoot)" "-DRS_MBEDTLS_ROOT=$($webrtc.MbedTlsRoot)" "-DRS_ICE_BACKEND=$iceBackend"
    & $cmake --build $nativeBuild --config $Configuration
    & $cmake --build $nativeBuild --target test
}

switch ($Target) {
    'Bootstrap' {
        & $dotnet --info
        & $python --version
    }
    'ValidateDesign' {
        & $python (Join-Path $root '11-bootstrap\validate-bundle.py') $root
    }
    'Restore' { Invoke-Restore }
    'GenerateContracts' { Invoke-GenerateContracts }
    'Build' {
        Invoke-Restore
        Invoke-GenerateContracts
        Invoke-NativeBuild
        & $dotnet build (Join-Path $root 'RemoteSupport.sln') -c $Configuration --no-restore
    }
    'Test' {
        Invoke-Restore
        Invoke-GenerateContracts
        Invoke-NativeBuild
        & $dotnet test (Join-Path $root 'RemoteSupport.sln') -c $Configuration --no-restore --logger "trx;LogFilePrefix=managed" --results-directory (Join-Path $root 'artifacts\test-results')
    }
    'HardwareTest' {
        Invoke-Restore
        Invoke-NativeBuild
        $nativeBuild = Join-Path $root "artifacts\native\$Configuration"
        & (Join-Path $nativeBuild 'dxgi_capture_test.exe')
        & (Join-Path $nativeBuild 'wgc_capture_test.exe')
        & (Join-Path $nativeBuild 'hardware_encoder_test.exe')
    }
    'IntegrationTest' {
        & $python (Join-Path $root 'tools\database\verify_schema.py') $root
        & $python (Join-Path $root 'tools\network\verify_goal07.py') $root
        & $dotnet run --project (Join-Path $root 'tools\fuzz\RemoteSupport.ProtocolFuzz\RemoteSupport.ProtocolFuzz.csproj') -c $Configuration --no-restore -- --iterations 10000
    }
    'Package' {
        Invoke-Restore
        Invoke-GenerateContracts
        Invoke-NativeBuild
        & $dotnet build (Join-Path $root 'RemoteSupport.sln') -c $Configuration --no-restore
        & $python (Join-Path $root 'tools\supply-chain\create_sbom.py') $root
        & (Join-Path $root 'eng\package-attended.ps1') -Configuration $Configuration -Architectures x64
    }
    'VerifyRelease' {
        & $python (Join-Path $root 'tools\supply-chain\verify_release.py') (Resolve-Path $Artifacts)
    }
    'CI' {
        & $python (Join-Path $root '11-bootstrap\validate-bundle.py') $root
        Invoke-Restore
        Invoke-GenerateContracts
        & git diff --exit-code -- schemas src/shared-contracts/generated src/shared-contracts/native src/shared-contracts/RemoteSupport.Contracts/Generated
        Invoke-NativeBuild
        & $dotnet build (Join-Path $root 'RemoteSupport.sln') -c $Configuration --no-restore
        & $dotnet test (Join-Path $root 'RemoteSupport.sln') -c $Configuration --no-build --logger "trx;LogFilePrefix=managed" --results-directory (Join-Path $root 'artifacts\test-results')
        & $python (Join-Path $root 'tools\security\scan_secrets.py') $root
        & $dotnet list (Join-Path $root 'RemoteSupport.sln') package --vulnerable --include-transitive
        & $python (Join-Path $root 'tools\supply-chain\create_sbom.py') $root
    }
}
