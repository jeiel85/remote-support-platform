[CmdletBinding()]
param(
    [ValidateSet('Bootstrap', 'ValidateDesign', 'Restore', 'GenerateContracts', 'Build', 'Test', 'IntegrationTest', 'Package', 'VerifyRelease', 'CI')]
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
    $nativeBuild = Join-Path $root "artifacts\native\$Configuration"
    & $cmake -S (Join-Path $root 'src\client\native') -B $nativeBuild -G 'MinGW Makefiles' "-DCMAKE_BUILD_TYPE=$Configuration" "-DRS_CONTRACT_ROOT=$root"
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
        & $dotnet build (Join-Path $root 'RemoteSupport.sln') -c $Configuration --no-restore
        Invoke-NativeBuild
    }
    'Test' {
        Invoke-Restore
        Invoke-GenerateContracts
        & $dotnet test (Join-Path $root 'RemoteSupport.sln') -c $Configuration --no-restore --logger "trx;LogFilePrefix=managed" --results-directory (Join-Path $root 'artifacts\test-results')
        Invoke-NativeBuild
    }
    'IntegrationTest' {
        & $python (Join-Path $root 'tools\database\verify_schema.py') $root
    }
    'Package' {
        Invoke-Restore
        Invoke-GenerateContracts
        & $dotnet build (Join-Path $root 'RemoteSupport.sln') -c $Configuration --no-restore
        & $python (Join-Path $root 'tools\supply-chain\create_sbom.py') $root
    }
    'VerifyRelease' {
        & $python (Join-Path $root 'tools\supply-chain\verify_release.py') (Resolve-Path $Artifacts)
    }
    'CI' {
        & $python (Join-Path $root '11-bootstrap\validate-bundle.py') $root
        Invoke-Restore
        Invoke-GenerateContracts
        & git diff --exit-code -- schemas src/shared-contracts/generated src/shared-contracts/native src/shared-contracts/RemoteSupport.Contracts/Generated
        & $dotnet build (Join-Path $root 'RemoteSupport.sln') -c $Configuration --no-restore
        & $dotnet test (Join-Path $root 'RemoteSupport.sln') -c $Configuration --no-build --logger "trx;LogFilePrefix=managed" --results-directory (Join-Path $root 'artifacts\test-results')
        Invoke-NativeBuild
        & $python (Join-Path $root 'tools\security\scan_secrets.py') $root
        & $dotnet list (Join-Path $root 'RemoteSupport.sln') package --vulnerable --include-transitive
        & $python (Join-Path $root 'tools\supply-chain\create_sbom.py') $root
    }
}
