param(
    [Parameter(Mandatory = $true)]
    [string] $RepositoryRoot,

    [Parameter(Mandatory = $true)]
    [string] $TestProject
)

$ErrorActionPreference = 'Stop'

function Invoke-DiagnosticTest {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Framework,

        [Parameter(Mandatory = $true)]
        [bool] $ContinuousIntegrationBuild
    )

    $projectDirectory = Split-Path -Parent $TestProject
    Remove-Item (Join-Path $projectDirectory 'CoverletOutput') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $projectDirectory 'MSTestResults') -Recurse -Force -ErrorAction SilentlyContinue

    Write-Host ''
    Write-Host '============================================================'
    Write-Host "Framework: $Framework"
    Write-Host "ContinuousIntegrationBuild: $ContinuousIntegrationBuild"
    Write-Host '============================================================'

    $arguments = @(
        'test',
        $TestProject,
        '-c', 'Release',
        '--framework', $Framework,
        '--no-restore',
        '-p:Stage=test',
        '-p:Configuration=Release',
        '-p:Platform=AnyCPU',
        '-v:minimal',
        '-p:Deterministic=true',
        "-p:ContinuousIntegrationBuild=$($ContinuousIntegrationBuild.ToString().ToLowerInvariant())",
        '-p:UseSharedCompilation=false',
        '-m:1'
    )

    & dotnet @arguments
    $exitCode = $LASTEXITCODE
    Write-Host "RESULT framework=$Framework CIB=$ContinuousIntegrationBuild exitCode=$exitCode"
    return $exitCode
}

Push-Location $RepositoryRoot
try {
    Write-Host 'dotnet --info'
    & dotnet --info
    if ($LASTEXITCODE -ne 0) { throw "dotnet --info failed with exit code $LASTEXITCODE." }

    Write-Host 'dotnet --list-runtimes'
    & dotnet --list-runtimes
    if ($LASTEXITCODE -ne 0) { throw "dotnet --list-runtimes failed with exit code $LASTEXITCODE." }

    & dotnet restore $TestProject
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }

    $net8WithoutCib = Invoke-DiagnosticTest -Framework 'net8.0' -ContinuousIntegrationBuild $false
    $net10WithCib = Invoke-DiagnosticTest -Framework 'net10.0' -ContinuousIntegrationBuild $true
    $net10WithoutCib = Invoke-DiagnosticTest -Framework 'net10.0' -ContinuousIntegrationBuild $false

    Write-Host ''
    Write-Host '================ DIAGNOSTIC SUMMARY ================'
    Write-Host "net8.0  CIB=false exit=$net8WithoutCib"
    Write-Host "net10.0 CIB=true  exit=$net10WithCib"
    Write-Host "net10.0 CIB=false exit=$net10WithoutCib"
    Write-Host '===================================================='

    if ($net8WithoutCib -ne 0) { throw 'Expected net8.0 with ContinuousIntegrationBuild=false to pass.' }
    if ($net10WithoutCib -ne 0) { throw 'Expected net10.0 with ContinuousIntegrationBuild=false to pass.' }

    if ($net10WithCib -eq 0) {
        Write-Warning 'ContinuousIntegrationBuild=true unexpectedly passed; the local Coverlet failure did not reproduce on this runner.'
    }
    else {
        Write-Host 'ContinuousIntegrationBuild=true reproduced the failing coverage path as expected.'
    }
}
finally {
    Pop-Location
}
