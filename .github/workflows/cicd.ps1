param (
    [string]$GitHubToken,
    [string]$NuGetApiKey,
    [string]$IntTestNuGetApiKey,
    [string]$PowerShellGalleryApiKey
)

# Fail-fast defaults for reliable CI/local runs:
# - StrictMode 3: treat uninitialized variables, unknown members, etc. as errors.
# - ErrorActionPreference='Stop': convert non-terminating errors into terminating ones (catchable).
# Error-handling guidance:
# - In catch{ }, prefer Write-Error or 'throw' to preserve fail-fast behavior.
#   * Write-Error (with ErrorActionPreference='Stop') is terminating and bubbles to the caller 'throw' is always terminating and keeps stack context.
# - Using Write-Host in catch{ } only logs and SWALLOWS the exception; execution continues, use a sentinel value (e.g., $null) explicitly.
# - Note: native tool exit codes on PS5 aren’t governed by ErrorActionPreference; use the Invoke-Exec or Invoke-ProcessTyped wrapper to enforce policy.
Set-StrictMode -Version 3
$ErrorActionPreference     = 'Stop'   # errors become terminating
$Global:ConsoleLogMinLevel = 'INF'    # gate: TRC/DBG/INF/WRN/ERR/FTL

# Keep this script compatible with PowerShell 5.1 and PowerShell 7+
# Lean, pipeline-friendly style—simple, readable, and easy to modify, failfast on errors.
Write-Host "Powershell script $(Split-Path -Leaf $PSCommandPath) has started."

# Provides lightweight reachability guards for external services.
# Detection only—no installs, imports, network changes, or pushes. (e.g Test-PSGalleryConnectivity)
# Designed to short-circuit local and CI/CD workflows when dependencies are offline (e.g., skip a push if the Git host is unreachable).
. "$PSScriptRoot\cicd.bootstrap.ps1"

$PowerShellGalleryAvailable = Test-PSGalleryConnectivity
$null = Test-GitHubConnectivity

# Module installation depends on PSGallery only; GitHub connectivity is diagnostic here.
if ($PowerShellGalleryAvailable)
{
    Update-ModuleIfNeeded2 -ModuleName 'Eigenverft.Manifested.Drydock'
}

# A freshly installed module is not guaranteed to auto-load in the same process.
Import-Module -Name 'Eigenverft.Manifested.Drydock' -Force -ErrorAction Stop
$null = Test-ModuleAvailable -Name 'Eigenverft.Manifested.Drydock' -IncludePrerelease -ExitIfNotFound -Quiet

# Required for updating PowerShellGet and PackageManagement providers in local PowerShell 5.x environments
Initialize-PowerShellMiniBootstrap

# Test TLS, NuGet, PackageManagement, PowerShellGet, and PSGallery publish endpoint
Test-PsGalleryPublishPrereqsOffline -ExitOnFailure

# Clean up previous versions of the module to avoid conflicts in local PowerShell environments
Uninstall-PreviousModuleVersions -ModuleName 'Eigenverft.Manifested.Drydock'

# Verify required commands are available, even a windows update could remove them temporarily
$null = Test-CommandAvailable -Command "dotnet" -ExitIfNotFound
$null = Test-CommandAvailable -Command "git" -ExitIfNotFound

# In the case the secrets are not passed as parameters, try to get them from the secrets file, local development or CI/CD environment
Test-VariableValue -Variable { $GitHubToken } -WarnIfNullOrEmpty -HideValue
Test-VariableValue -Variable { $NuGetApiKey } -WarnIfNullOrEmpty -HideValue
Test-VariableValue -Variable { $IntTestNuGetApiKey } -WarnIfNullOrEmpty -HideValue
Test-VariableValue -Variable { $PowerShellGalleryApiKey } -WarnIfNullOrEmpty -HideValue
$GitHubToken = Get-ConfigValue -Check $GitHubToken -FilePath (Join-Path $PSScriptRoot 'cicd.secrets.json') -Property 'NUGET_GITHUB_PUSH'
$NuGetApiKey = Get-ConfigValue -Check $NuGetApiKey -FilePath (Join-Path $PSScriptRoot 'cicd.secrets.json') -Property 'NUGET_PAT'
$IntTestNuGetApiKey = Get-ConfigValue -Check $IntTestNuGetApiKey -FilePath (Join-Path $PSScriptRoot 'cicd.secrets.json') -Property 'NUGET_TEST_PAT'
$PowerShellGalleryApiKey = Get-ConfigValue -Check $PowerShellGalleryApiKey -FilePath (Join-Path $PSScriptRoot 'cicd.secrets.json') -Property 'PsGalleryApiKey'
Test-VariableValue -Variable { $GitHubToken } -ExitIfNullOrEmpty -HideValue
Test-VariableValue -Variable { $NuGetApiKey } -ExitIfNullOrEmpty -HideValue
Test-VariableValue -Variable { $IntTestNuGetApiKey } -ExitIfNullOrEmpty -HideValue
Test-VariableValue -Variable { $PowerShellGalleryApiKey } -ExitIfNullOrEmpty -HideValue

# Preload environment information
$RunEnvironment = Get-RunEnvironment
$GitRepositoryRoot = Get-GitTopLevelDirectory
$GitCurrentBranch = Get-GitCurrentBranch
$GitBranchRootDirectory = Get-GitCurrentBranchRoot
$GitRepositoryName = Get-GitRepositoryName
$GitRemoteUrl = Get-GitRemoteUrl

# Failfast / guard if any of the required preloaded environment information is not available
Test-VariableValue -Variable { $RunEnvironment } -ExitIfNullOrEmpty
Test-VariableValue -Variable { $GitRepositoryRoot } -ExitIfNullOrEmpty
Test-VariableValue -Variable { $GitCurrentBranch } -ExitIfNullOrEmpty
Test-VariableValue -Variable { $GitBranchRootDirectory } -ExitIfNullOrEmpty
Test-VariableValue -Variable { $GitRepositoryName } -ExitIfNullOrEmpty
Test-VariableValue -Variable { $GitRemoteUrl } -ExitIfNullOrEmpty

# Generate deployment info based on the current branch name
$BranchDeploymentConfig = Convert-BranchToDeploymentInfo -BranchName "$GitCurrentBranch"

# Generates a version based on the current date time to verify the version functions work as expected
$GeneratedVersion = Convert-DateTimeTo64SecVersionComponents -VersionBuild 0 -VersionMajor 1
#$GeneratedVersion.VersionFull = "0.1.20256.30636"
$GeneratedVersionAsDateTime = Convert-64SecVersionComponentsToDateTime -VersionBuild $GeneratedVersion.VersionBuild -VersionMajor $GeneratedVersion.VersionMajor -VersionMinor $GeneratedVersion.VersionMinor -VersionRevision $GeneratedVersion.VersionRevision
Test-VariableValue -Variable { $GeneratedVersion } -ExitIfNullOrEmpty
Test-VariableValue -Variable { $GeneratedVersionAsDateTime } -ExitIfNullOrEmpty

# Generate a local PowerShell Gallery repository to publish to.
$LocalPowerShellGalleryName = "LocalPowerShellGallery"
$LocalPowerShellGalleryName = Register-LocalPSGalleryRepository -RepositoryName "$LocalPowerShellGalleryName"

# Generate a local NuGet package source to publish to.
$LocalNuGetSourceName = "LocalNuGet"
$LocalNuGetSourceName = Register-LocalNuGetDotNetPackageSource -SourceName "$LocalNuGetSourceName"

# All config files paths
$ConfigRootPath = Get-Path -Paths @("$GitRepositoryRoot",".github","workflows",".config")

$SPDXCachePath = Get-Path -Paths @("$ConfigRootPath","SPDX_cache")
$DotNetToolsManifestPath = Get-Path -Paths @("$ConfigRootPath","dotnet-tools","dotnet-tools.json")
$NuGetAllowedLicensesPath = Get-Path -Paths @("$ConfigRootPath","nuget-license","allowed-licenses.json")
$NuGetLicenseMappingsPath = Get-Path -Paths @("$ConfigRootPath","nuget-license","licenses-mapping.json")
$NuGetLicenseFileMappingsPath = Get-Path -Paths @("$ConfigRootPath","nuget-license","license-file-mappings.json")
$DocFxTemplatePath = Get-Path -Paths @("$ConfigRootPath","docfx","build","docfx_local.template.json")
$IndexTemplatePath = Get-Path -Paths @("$ConfigRootPath","docfx","build","index.template.md")

# Enable github specific nuget sources.
$GitHubPackagesUser = "eigenverft"
$GitHubSourceName = "github"
$GitHubSourceUri = "https://nuget.pkg.github.com/$GitHubPackagesUser/index.json"
$NuGetTestSourceUri = "https://apiint.nugettest.org/v3/index.json"
$NuGetOrgSourceUri = "https://api.nuget.org/v3/index.json"
Unregister-LocalNuGetDotNetPackageSource -SourceName "$GitHubSourceName"
Invoke-ProcessTyped -Executable "dotnet" -Arguments @("nuget","add", "source", "--username", "$GitHubPackagesUser","--password","$GitHubToken","--store-password-in-clear-text","--name","$GitHubSourceName","$GitHubSourceUri") -CaptureOutput $false -CaptureOutputDump $false -HideValues @($GitHubToken)

# Enable the .NET tools specified in the manifest file
Enable-TempDotnetTools -ManifestFile "$DotNetToolsManifestPath" -NoReturn

# Required output root folder
$OutputRootPath = Get-Path -Paths @("$GitRepositoryRoot","output")
New-Directory -Paths @($OutputRootPath)

# Delete clean the outputfolder
if (-not $($RunEnvironment.IsCI)) { Remove-FilesByPattern -Path "$OutputRootPath" -Pattern "*"  }

$BranchVersionRelativePath = Get-Path -Paths @($BranchDeploymentConfig.Branch.PathSegmentsSanitized,$GeneratedVersion.VersionFull)
$ChannelVersionRelativePath = Get-Path -Paths @($BranchDeploymentConfig.Channel.Value,$GeneratedVersion.VersionFull)
$ChannelLatestRelativePath = Get-Path -Paths @($BranchDeploymentConfig.Channel.Value,"latest")

# All required output folders
$BuildRootPath = Get-Path -Paths @("$OutputRootPath","build")
$BuildBinPath = Get-Path -Paths @("$BuildRootPath","bin")
$BuildObjPath = Get-Path -Paths @("$BuildRootPath","obj")

$PackRootPath = Get-Path -Paths @("$OutputRootPath","pack")
$PublishRootPath = Get-Path -Paths @("$OutputRootPath","publish")
$RepoPublishRootPath = Get-Path -Paths @("$OutputRootPath","repopublish")
$SlnPublishRootPath = Get-Path -Paths @("$OutputRootPath","slnpublish")
$ProjPublishRootPath = Get-Path -Paths @("$OutputRootPath","projpublish")
$ReportsRootPath =  Get-Path -Paths @("$OutputRootPath","reports")
$DocsRootPath = Get-Path -Paths @("$OutputRootPath","docs")

# Initialize the array to accumulate projects.
$SolutionFileInfos = Find-FilesByPattern -Path "$GitRepositoryRoot\src" -Pattern "*.sln;*.slnx"
$SolutionProjectPaths = @()
foreach ($solutionFile in $SolutionFileInfos) {
    # all ready sorted by the drydock.exe
    $CurrentProjectPaths = Invoke-ProcessTyped -Executable "drydock.exe" -Arguments @( "sln", "--location", "$($solutionFile.FullName)") -ReturnType 'Objects'
    $SolutionProjectPaths += [pscustomobject]@{ Sln =$solutionFile; Prj = ($CurrentProjectPaths | ForEach-Object { Get-Item $_ }) };
}

$Vswhere = Find-FilesByPattern -Path "${env:ProgramFiles(x86)}\Microsoft Visual Studio" -Pattern "vswhere.exe"
$MsBuildVs = Invoke-ProcessTyped -Executable "$($Vswhere.FullName)" -Arguments @("-latest", "-products","*", "-requires","Microsoft.Component.MSBuild", "-find", "**\Bin\MSBuild.exe") -ReturnType Objects

# Build, Test, Pack, Publish, and Generate Reports for each project in the solution.
foreach ($SolutionProjectPath in $SolutionProjectPaths) {
    foreach ($ProjectFileInfo in $SolutionProjectPath.Prj) {
        $SolutionFileInfo = $SolutionProjectPath.Sln

        # Create required output directories
        New-Directory -Paths @($BuildRootPath)
        $BuildBinDirectory = New-Directory -Paths @($BuildBinPath,$SolutionFileInfo.BaseName,$ProjectFileInfo.BaseName,$BranchVersionRelativePath)
        $BuildObjDirectory = New-Directory -Paths @($BuildObjPath,$SolutionFileInfo.BaseName,$ProjectFileInfo.BaseName,$BranchVersionRelativePath)

        $PackDirectory = New-Directory -Paths @($PackRootPath,$SolutionFileInfo.BaseName,$ProjectFileInfo.BaseName,$ChannelVersionRelativePath)
        $PublishDirectory = New-Directory -Paths @($PublishRootPath,$SolutionFileInfo.BaseName,$ProjectFileInfo.BaseName,$ChannelVersionRelativePath)
        $ReportsDirectory = New-Directory -Paths @($ReportsRootPath,$SolutionFileInfo.BaseName,$ProjectFileInfo.BaseName,$ChannelVersionRelativePath)
        $DocsDirectory = New-Directory -Paths @($DocsRootPath,$SolutionFileInfo.BaseName,$ProjectFileInfo.BaseName,$ChannelVersionRelativePath)

        $DotnetCommonParameters = @(
            "-p:Configuration=Release",
            "-p:Platform=AnyCPU",
            "-v:minimal",
            "-p:Deterministic=true",
            "-p:ContinuousIntegrationBuild=true",
            "-p:VersionBuild=$($GeneratedVersion.VersionBuild)",
            "-p:VersionMajor=$($GeneratedVersion.VersionMajor)",
            "-p:VersionMinor=$($GeneratedVersion.VersionMinor)",
            "-p:VersionRevision=$($GeneratedVersion.VersionRevision)",
            "-p:VersionSuffix=$($BranchDeploymentConfig.Affix.Suffix)",
            "-p:BaseOutputPath=$($BuildBinDirectory)/",
            "-p:IntermediateOutputPath=$($BuildObjDirectory)/",
            "-p:UseSharedCompilation=false",
            "-m:1"
        )

        $NonSDKParameters = @(
            "-p:Configuration=Release",
            "-p:Platform=AnyCPU",
            "-v:minimal",
            "-p:VersionBuild=$($GeneratedVersion.VersionBuild)",
            "-p:VersionMajor=$($GeneratedVersion.VersionMajor)",
            "-p:VersionMinor=$($GeneratedVersion.VersionMinor)",
            "-p:VersionRevision=$($GeneratedVersion.VersionRevision)",
            "-p:VersionSuffix=$($BranchDeploymentConfig.Affix.Suffix)",
            "-p:OutputPath=$($BuildBinDirectory)/",
            "-p:BaseIntermediateOutputPath=$($BuildObjDirectory)/",
            "-p:UseSharedCompilation=false"
        )

        Invoke-ProcessTyped -Executable "drydock.exe" -Arguments @("csproj", "--location", "$($ProjectFileInfo.FullName)", "--property", "TargetFrameworkVersion") -ReturnType Objects -AllowedExitCodes @(0,-1) -CaptureOutput $false -CaptureOutputDump $true

        $IsSDKProj = $false
        $IsNoneSDKProj = $false
        $IsSDKWithFramework = $false

        if ($LASTEXITCODE -eq -1) {
            $IsSDKProj = $true
        } else {
            $IsNoneSDKProj = $true
        }

        # TargetFrameworkVersion not found assume sdk project style and get TargetFramework
        if ($IsSDKProj) {
            $TargetFramework = Invoke-ProcessTyped -Executable "drydock.exe" -Arguments @("csproj", "--location", "$($ProjectFileInfo.FullName)", "--property", "TargetFramework") -ReturnType Objects -AllowedExitCodes @(0,-1)
            if ($LASTEXITCODE -eq -1)
            {
                $TargetFrameworks = Invoke-ProcessTyped -Executable "drydock.exe" -Arguments @("csproj", "--location", "$($ProjectFileInfo.FullName)", "--property", "TargetFrameworks") -ReturnType Objects -AllowedExitCodes @(0)
                $TargetFrameworks = $TargetFrameworks.Split(';')
                foreach ($TargetFrame in $TargetFrameworks)
                {
                    if ($TargetFrame.Trim().ToLowerInvariant() -in @('net20', 'net35', 'net40', 'net403', 'net45', 'net451', 'net452', 'net46', 'net461', 'net462', 'net47', 'net471', 'net472', 'net48', 'net481'))
                    {
                        $IsSDKWithFramework = $true
                        break;
                    }
                }
            } elseif ($LASTEXITCODE -eq 0) {
                if ($TargetFramework -in @('net20', 'net35', 'net40', 'net403', 'net45', 'net451', 'net452', 'net46', 'net461', 'net462', 'net47', 'net471', 'net472', 'net48', 'net481'))
                {
                   $IsSDKWithFramework = $true
                }
            }
        }

        # Sequence for framework and dotnet core projects , restore,clean,restore needed for proper incremental build
        Invoke-ProcessTyped -Executable "dotnet" -Arguments @("restore", "$($ProjectFileInfo.FullName)", "-p:Stage=restore") -ReturnType Objects -CommonArguments $DotnetCommonParameters
        Invoke-ProcessTyped -Executable "dotnet" -Arguments @("clean", "$($ProjectFileInfo.FullName)", "-p:Stage=clean") -ReturnType Objects -CommonArguments $DotnetCommonParameters
        Invoke-ProcessTyped -Executable "dotnet" -Arguments @("restore", "$($ProjectFileInfo.FullName)", "-p:Stage=restore") -ReturnType Objects -CommonArguments $DotnetCommonParameters

        if ($IsNoneSDKProj)
        {
            Invoke-ProcessTyped -Executable "$MsBuildVs" -Arguments @("$($ProjectFileInfo.FullName)", "-p:Stage=build") -CommonArguments $NonSDKParameters -ReturnType Objects -CaptureOutput $true -CaptureOutputDump $false
        }

        if ($IsSDKProj)
        {
            if ($IsSDKWithFramework)
            {
                Invoke-ProcessTyped -Executable "$MsBuildVs" -Arguments @("/t:Build","$($ProjectFileInfo.FullName)", "-p:Stage=build")  -CommonArguments $DotnetCommonParameters -ReturnType Objects -CaptureOutput $true -CaptureOutputDump $false
            }
            else {
                Invoke-ProcessTyped -Executable "dotnet" -Arguments @("build","$($ProjectFileInfo.FullName)", "-p:Stage=build")  -CommonArguments $DotnetCommonParameters -ReturnType Objects -CaptureOutput $true -CaptureOutputDump $false
            }
        }

        $IsTestProject = $false
        $IsPackable = $false
        $IsPublishable = $false
        if ($IsSDKProj)
        {
            $IsTestProject = Invoke-ProcessTyped -Executable "drydock.exe" -Arguments @("csproj", "--location", "$($ProjectFileInfo.FullName)", "--property", "IsTestProject") -ReturnType Objects
            $IsPackable = Invoke-ProcessTyped -Executable "drydock.exe" -Arguments @("csproj", "--location", "$($ProjectFileInfo.FullName)", "--property", "IsPackable") -ReturnType Objects
            $IsPublishable = Invoke-ProcessTyped -Executable "drydock.exe" -Arguments @("csproj", "--location", "$($ProjectFileInfo.FullName)", "--property", "IsPublishable") -ReturnType Objects
        }

        if (($IsPackable -eq $true) -or ($IsPublishable -eq $true))
        {
            #Dependency-Health-and-Inventory.Report
            $VulnerabilitiesJson = Invoke-ProcessTyped -Executable "dotnet" -Arguments @("list", "$($ProjectFileInfo.FullName)", "package", "--vulnerable", "--format", "json")
            New-DotnetVulnerabilitiesReport -jsonInput $VulnerabilitiesJson -OutputFile "$ReportsDirectory\Vulnerabilities.md" -OutputFormat markdown -ExitOnVulnerability $false
            New-DotnetVulnerabilitiesReport -jsonInput $VulnerabilitiesJson -OutputFile "$ReportsDirectory\Vulnerabilities.txt" -OutputFormat text -ExitOnVulnerability $false

            $DeprecatedPackagesJson = Invoke-ProcessTyped -Executable "dotnet" -Arguments @("list", "$($ProjectFileInfo.FullName)", "package", "--deprecated", "--include-transitive", "--format", "json")
            New-DotnetDeprecatedReport -jsonInput $DeprecatedPackagesJson -OutputFile "$ReportsDirectory\Deprecated.md" -OutputFormat markdown -IgnoreTransitivePackages $true -ExitOnDeprecated $false
            New-DotnetDeprecatedReport -jsonInput $DeprecatedPackagesJson -OutputFile "$ReportsDirectory\Deprecated.txt" -OutputFormat text -IgnoreTransitivePackages $true -ExitOnDeprecated $false

            $OutdatedPackagesJson = Invoke-ProcessTyped -Executable "dotnet" -Arguments @("list", "$($ProjectFileInfo.FullName)", "package", "--outdated", "--include-transitive", "--format", "json")
            New-DotnetOutdatedReport -jsonInput $OutdatedPackagesJson -OutputFile "$ReportsDirectory\Outdated.md" -OutputFormat markdown -IgnoreTransitivePackages $false
            New-DotnetOutdatedReport -jsonInput $OutdatedPackagesJson -OutputFile "$ReportsDirectory\Outdated.txt" -OutputFormat text -IgnoreTransitivePackages $false

            $BillOfMaterialsJson = Invoke-ProcessTyped -Executable "dotnet" -Arguments @("list", "$($ProjectFileInfo.FullName)", "package", "--include-transitive", "--format", "json")
            New-DotnetBillOfMaterialsReport -jsonInput $BillOfMaterialsJson -OutputFile "$ReportsDirectory\BillOfMaterials.md" -OutputFormat markdown -IgnoreTransitivePackages $true
            New-DotnetBillOfMaterialsReport -jsonInput $BillOfMaterialsJson -OutputFile "$ReportsDirectory\BillOfMaterials.txt" -OutputFormat text -IgnoreTransitivePackages $true

            Join-FileText -InputFiles @("$ReportsDirectory\BillOfMaterials.txt", "$ReportsDirectory\Vulnerabilities.txt","$ReportsDirectory\Deprecated.txt") -OutputFile "$ReportsDirectory\SBOM-$(($ProjectFileInfo.BaseName).Replace('.','_'))" -BetweenFiles 'One'

            $NuGetLicenseReportPath = "$ReportsDirectory/$($ProjectFileInfo.BaseName).ThirdPartyLicencesNotices.json"
            Invoke-ProcessTyped -Executable "nuget-license" -Arguments @("--input", "$($ProjectFileInfo.FullName)", "--allowed-license-types", "$NuGetAllowedLicensesPath", "--output", "JsonPretty", "--licenseurl-to-license-mappings", "$NuGetLicenseMappingsPath", "--licensefile-to-license-mappings", "$NuGetLicenseFileMappingsPath", "--file-output", "$NuGetLicenseReportPath" ) -AllowedExitCodes @(0,1)
            $NuGetLicenseExitCode = $LASTEXITCODE
            if ($NuGetLicenseExitCode -ne 0)
            {
                if (Test-Path -LiteralPath $NuGetLicenseReportPath -PathType Leaf)
                {
                    Write-Host "nuget-license validation report:"
                    Get-Content -LiteralPath $NuGetLicenseReportPath -Raw | Write-Host
                }
                throw "nuget-license found disallowed or unresolved package licenses (exit code $NuGetLicenseExitCode)."
            }
            New-ThirdPartyNotice -LicenseJsonPath "$NuGetLicenseReportPath" -OutputPath "$ReportsDirectory\$($ProjectFileInfo.BaseName).ThirdPartyLicencesNotices.txt" -Name "$($ProjectFileInfo.BaseName)"

            Export-PackageLicenseTexts -JsonPath "$ReportsDirectory/$($ProjectFileInfo.BaseName).ThirdPartyLicencesNotices.json" -OutputDirectory "$ReportsDirectory" -CacheDirectory "$SPDXCachePath"
        }

        if ($IsTestProject -eq $true)
        {
            Invoke-ProcessTyped -Executable "dotnet" -Arguments @("test", "$($ProjectFileInfo.FullName)", "-c", "Release","-p:""Stage=test""")  -CommonArguments $DotnetCommonParameters -CaptureOutput $false
        }

        if ($IsPackable -eq $true)
        {
            Invoke-ProcessTyped -Executable "dotnet" -Arguments @("pack", "$($ProjectFileInfo.FullName)", "-c", "Release","-p:""Stage=pack""","-p:""PackageOutputPath=$($PackDirectory)""")  -CommonArguments $DotnetCommonParameters -CaptureOutput $false
        }

        if ($IsPublishable -eq $true)
        {
            Invoke-ProcessTyped -Executable "dotnet" -Arguments @("publish", "$($ProjectFileInfo.FullName)", "-c", "Release","-p:""Stage=publish""","-p:""PublishDir=$($PublishDirectory)""")  -CommonArguments $DotnetCommonParameters -CaptureOutput $false
        }

        if ($IsNoneSDKProj) {
            Copy-FilesRecursively -SourceDirectory "$($BuildBinDirectory)" -DestinationDirectory "$($PublishDirectory)" -Filter "*" -CopyEmptyDirs $false -ForceOverwrite $true -CleanDestination MirrorTree
        }

        if ($IsPackable -eq $true)
        {
            $DocFxReplacementsByToken = @{
                "sourceCodeDirectory" = "$($ProjectFileInfo.DirectoryName.Replace('\','/'))"
                "outputDirectory"     = (Get-Path -Paths @("$DocsDirectory","docfx")).Replace('\','/')
                "appName"     = "$($ProjectFileInfo.BaseName)"
            }
            $DocFxConfigFileInfos = Convert-TemplateFilePlaceholders -TemplateFile $DocFxTemplatePath -Replacements $DocFxReplacementsByToken
            $null = Convert-TemplateFilePlaceholders -TemplateFile $IndexTemplatePath -Replacements $DocFxReplacementsByToken
            Invoke-ProcessTyped -Executable "docfx" -Arguments @("$($DocFxConfigFileInfos.FullName)")  -CaptureOutput $false -CaptureOutputDump $true
        }

    }
}

#$ThirdPartyLicencesNoticesFiles = Find-FilesByPattern -Path "$ReportsRootPath" -Pattern "*.ThirdPartyLicencesNotices.txt" | ForEach-Object { $_.FullName }
#$THIRDPARTYDirectory = New-Directory -Paths @($PublishDirectory,"THIRDPARTY-LICENSES-NOTICE")
#Join-FileText -InputFiles @($ThirdPartyLicencesNoticesFiles) -OutputFile "$THIRDPARTYDirectory\THIRDPARTY-LICENSE-NOTICE" -BetweenFiles 'One'
#$InventoryHealthReportFiles = Find-FilesByPattern -Path "$ReportsRootPath" -Pattern "*.Inventory-Health-Report.txt" | ForEach-Object { $_.FullName }
#Join-FileText -InputFiles @($InventoryHealthReportFiles) -OutputFile "$PublishDirectory\BOM-HEALTH" -BetweenFiles 'One'

# Resolving deployment information for the current branch
$DeploymentChannel = $BranchDeploymentConfig.Channel.Value

$Drop = "C:\temp\$GitRepositoryName-drops"
$RepositoryDropRootPath = "$Drop\rep"
$SolutionsDropRootPath = "$Drop\sln"
$ProjectsDropRootPath = "$Drop\prj"

$PushToLocalSource = $false
$PushToGitHubSource = $false
$PushToNuGetTest = $false
$PushToNuGetOrg = $false

# Determine where to publish based on the deployment channel
if ($DeploymentChannel -in @("development"))
{
    $PushToLocalSource = $true
    $PushToGitHubSource = $true
    $PushToNuGetTest = $false
    $PushToNuGetOrg = $false
}

if ($DeploymentChannel -in @('quality'))
{
    $PushToLocalSource = $true
    $PushToGitHubSource = $true
    $PushToNuGetTest = $true
    $PushToNuGetOrg = $false
}

if ($DeploymentChannel -in @('staging'))
{
    $PushToLocalSource = $true
    $PushToGitHubSource = $true
    $PushToNuGetTest = $true
    $PushToNuGetOrg = $false
}

if ($DeploymentChannel -in @('production'))
{
    $PushToLocalSource = $true
    $PushToGitHubSource = $true
    $PushToNuGetTest = $false
    $PushToNuGetOrg = $true
}

# Deploy *.nupkg artifacts to the appropriate destinations
if ($PushToLocalSource -eq $true)
{
    $NuGetPackageFileInfos = Find-FilesByPattern -Path "$PackRootPath" -Pattern "*.nupkg"
    foreach ($NuGetPackageFileInfo in $NuGetPackageFileInfos)
    {
        Invoke-ProcessTyped -Executable "dotnet" -Arguments @("nuget", "push", "$($NuGetPackageFileInfo.FullName)", "--source","$LocalNuGetSourceName")
    }
}

if ($PushToGitHubSource -eq $true)
{
    $NuGetPackageFileInfos = Find-FilesByPattern -Path "$PackRootPath" -Pattern "*.nupkg"
    foreach ($NuGetPackageFileInfo in $NuGetPackageFileInfos)
    {
        Invoke-ProcessTyped -Executable "dotnet" -Arguments @("nuget","push", "$($NuGetPackageFileInfo.FullName)", "--api-key", "$GitHubToken","--source","$GitHubSourceName") -HideValues @($GitHubToken)
    }
    Unregister-LocalNuGetDotNetPackageSource -SourceName "$GitHubSourceName"
}

if ($PushToNuGetTest -eq $true)
{
    $NuGetPackageFileInfos = Find-FilesByPattern -Path "$PackRootPath" -Pattern "*.nupkg"
    foreach ($NuGetPackageFileInfo in $NuGetPackageFileInfos)
    {
        Invoke-ProcessTyped -Executable "dotnet" -Arguments @("nuget","push", "$($NuGetPackageFileInfo.FullName)", "--api-key", "$IntTestNuGetApiKey","--source","$NuGetTestSourceUri") -HideValues @($IntTestNuGetApiKey)
    }
}

if ($PushToNuGetOrg -eq $true)
{
    $NuGetPackageFileInfos = Find-FilesByPattern -Path "$PackRootPath" -Pattern "*.nupkg"
    foreach ($NuGetPackageFileInfo in $NuGetPackageFileInfos)
    {
        Invoke-ProcessTyped -Executable "dotnet" -Arguments @("nuget","push", "$($NuGetPackageFileInfo.FullName)", "--api-key", "$NuGetApiKey","--source","$NuGetOrgSourceUri") -HideValues @($NuGetApiKey)
    }
}

# additional publish copys
foreach ($SolutionProjectPath in $SolutionProjectPaths) {
    foreach ($ProjectFileInfo in $SolutionProjectPath.Prj) {
        $SolutionFileInfo = $SolutionProjectPath.Sln
            $PublishDirectory = New-Directory -Paths @($PublishRootPath,$SolutionFileInfo.BaseName,$ProjectFileInfo.BaseName,$ChannelVersionRelativePath)
            $ReportsDirectory = New-Directory -Paths @($ReportsRootPath,$SolutionFileInfo.BaseName,$ProjectFileInfo.BaseName,$ChannelVersionRelativePath)
            $DocsDirectory = New-Directory -Paths @($DocsRootPath,$SolutionFileInfo.BaseName,$ProjectFileInfo.BaseName,$ChannelVersionRelativePath)
            Copy-FilesRecursively -SourceDirectory "$ReportsDirectory" -DestinationDirectory "$PublishDirectory" -Filter "LICENSE-*" -CopyEmptyDirs $false -ForceOverwrite $true
            Copy-FilesRecursively -SourceDirectory "$ReportsDirectory" -DestinationDirectory "$PublishDirectory" -Filter "SBOM-*" -CopyEmptyDirs $false -ForceOverwrite $true
            if (Test-Path -Path "$DocsDirectory\docfx" -PathType Container)
            {
                Copy-FilesRecursively -SourceDirectory "$DocsDirectory\docfx" -DestinationDirectory "$PublishDirectory\DOCFX\$($ProjectFileInfo.BaseName)" -Filter "*" -CopyEmptyDirs $false -ForceOverwrite $true
            }
     }
}

# additional publish cleanups
foreach ($SolutionProjectPath in $SolutionProjectPaths) {
    foreach ($ProjectFileInfo in $SolutionProjectPath.Prj) {
        $SolutionFileInfo = $SolutionProjectPath.Sln
            $PublishDirectory = New-Directory -Paths @($PublishRootPath,$SolutionFileInfo.BaseName,$ProjectFileInfo.BaseName,$ChannelVersionRelativePath)
            Remove-FilesByPattern -Path "$PublishDirectory" -Pattern "*.pdb"
     }
}

# repository based drops of files.
$RepoPublishDirectory = New-Directory -Paths @($RepoPublishRootPath,$ChannelVersionRelativePath)
foreach ($SolutionProjectPath in $SolutionProjectPaths) {
    $SolutionFileInfo = $SolutionProjectPath.Sln
    foreach ($ProjectFileInfo in $SolutionProjectPath.Prj) {
            $PublishDirectory = New-Directory -Paths @($PublishRootPath,$SolutionFileInfo.BaseName,$ProjectFileInfo.BaseName,$ChannelVersionRelativePath)
            Copy-FilesRecursively -SourceDirectory "$PublishDirectory" -DestinationDirectory "$RepoPublishDirectory" -Filter "*" -CopyEmptyDirs $false -ForceOverwrite $true
    }
}
Copy-FilesRecursively -SourceDirectory "$RepoPublishDirectory" -DestinationDirectory (Get-Path -Paths @($RepositoryDropRootPath,$GitRepositoryName,$ChannelVersionRelativePath)) -Filter "*" -CopyEmptyDirs $false -ForceOverwrite $true -CleanDestination MirrorTree
Copy-FilesRecursively -SourceDirectory "$RepoPublishDirectory" -DestinationDirectory (Get-Path -Paths @($RepositoryDropRootPath,$GitRepositoryName,$ChannelLatestRelativePath)) -Filter "*" -CopyEmptyDirs $false -ForceOverwrite $true -CleanDestination MirrorTree
Copy-FilesRecursively -SourceDirectory "$RepoPublishDirectory" -DestinationDirectory (Get-Path -Paths @($RepositoryDropRootPath,$GitRepositoryName,"distributed")) -Filter "*" -CopyEmptyDirs $false -ForceOverwrite $true -CleanDestination MirrorTree
$nugetFilePart1 = Join-Text -InputObject @("$($GitRepositoryName)","$($GeneratedVersion.VersionFull)") -Separator '.' -Normalization Trim
$nugetFileEmulation = Join-Text -InputObject @("$nugetFilePart1","$($BranchDeploymentConfig.Affix.Label)") -Separator '-' -Normalization Trim
Compress-Directory -SourceDirectory "$RepoPublishDirectory" -DestinationFile "$(Get-Path -Paths @($RepositoryDropRootPath,$GitRepositoryName,"zipped","$nugetFileEmulation.zip"))"


# solution based drops of files.
foreach ($SolutionProjectPath in $SolutionProjectPaths) {
    $SolutionFileInfo = $SolutionProjectPath.Sln
    $SolutionPublishDirectory = New-Directory -Paths @($SlnPublishRootPath,$SolutionFileInfo.BaseName,$ChannelVersionRelativePath)
    Remove-FilesByPattern -Path "$SolutionPublishDirectory" -Pattern "*"
    foreach ($ProjectFileInfo in $SolutionProjectPath.Prj) {
            $PublishDirectory = New-Directory -Paths @($PublishRootPath,$SolutionFileInfo.BaseName,$ProjectFileInfo.BaseName,$ChannelVersionRelativePath)
            Copy-FilesRecursively -SourceDirectory "$PublishDirectory" -DestinationDirectory "$SolutionPublishDirectory" -Filter "*" -CopyEmptyDirs $false -ForceOverwrite $true
    }
    Copy-FilesRecursively -SourceDirectory "$SolutionPublishDirectory" -DestinationDirectory (Get-Path -Paths @($SolutionsDropRootPath,$SolutionFileInfo.BaseName,$ChannelVersionRelativePath)) -Filter "*" -CopyEmptyDirs $false -ForceOverwrite $true -CleanDestination MirrorTree
    Copy-FilesRecursively -SourceDirectory "$SolutionPublishDirectory" -DestinationDirectory (Get-Path -Paths @($SolutionsDropRootPath,$SolutionFileInfo.BaseName,$ChannelLatestRelativePath)) -Filter "*" -CopyEmptyDirs $false -ForceOverwrite $true -CleanDestination MirrorTree
    Copy-FilesRecursively -SourceDirectory "$SolutionPublishDirectory" -DestinationDirectory (Get-Path -Paths @($SolutionsDropRootPath,$SolutionFileInfo.BaseName,"distributed")) -Filter "*" -CopyEmptyDirs $false -ForceOverwrite $true -CleanDestination MirrorTree
    $nugetFilePart1 = Join-Text -InputObject @("$($SolutionFileInfo.BaseName)","$($GeneratedVersion.VersionFull)") -Separator '.' -Normalization Trim
    $nugetFileEmulation = Join-Text -InputObject @("$nugetFilePart1","$($BranchDeploymentConfig.Affix.Label)") -Separator '-' -Normalization Trim
    Compress-Directory -SourceDirectory "$SolutionPublishDirectory" -DestinationFile "$(Get-Path -Paths @($SolutionsDropRootPath,$SolutionFileInfo.BaseName,"zipped","$nugetFileEmulation.zip"))"
}

# project based drops of files.
foreach ($SolutionProjectPath in $SolutionProjectPaths) {
    $SolutionFileInfo = $SolutionProjectPath.Sln
    foreach ($ProjectFileInfo in $SolutionProjectPath.Prj) {
            $PublishDirectory = New-Directory -Paths @($PublishRootPath,$SolutionFileInfo.BaseName,$ProjectFileInfo.BaseName,$ChannelVersionRelativePath)
            $ProjPublishDirectory = New-Directory -Paths @($ProjPublishRootPath,$ProjectFileInfo.BaseName,$ChannelVersionRelativePath)
            Copy-FilesRecursively -SourceDirectory "$PublishDirectory" -DestinationDirectory "$ProjPublishDirectory" -Filter "*" -CopyEmptyDirs $false -ForceOverwrite $true
            Copy-FilesRecursively -SourceDirectory "$ProjPublishDirectory" -DestinationDirectory (Get-Path -Paths @($ProjectsDropRootPath,$ProjectFileInfo.BaseName,$ChannelVersionRelativePath)) -Filter "*" -CopyEmptyDirs $false -ForceOverwrite $true -CleanDestination MirrorTree
            Copy-FilesRecursively -SourceDirectory "$ProjPublishDirectory" -DestinationDirectory (Get-Path -Paths @($ProjectsDropRootPath,$ProjectFileInfo.BaseName,$ChannelLatestRelativePath)) -Filter "*" -CopyEmptyDirs $false -ForceOverwrite $true -CleanDestination MirrorTree
            Copy-FilesRecursively -SourceDirectory "$ProjPublishDirectory" -DestinationDirectory (Get-Path -Paths @($ProjectsDropRootPath,$ProjectFileInfo.BaseName,"distributed")) -Filter "*" -CopyEmptyDirs $false -ForceOverwrite $true -CleanDestination MirrorTree
            $nugetFilePart1 = Join-Text -InputObject @("$($ProjectFileInfo.BaseName)","$($GeneratedVersion.VersionFull)") -Separator '.' -Normalization Trim
            $nugetFileEmulation = Join-Text -InputObject @("$nugetFilePart1","$($BranchDeploymentConfig.Affix.Label)") -Separator '-' -Normalization Trim
            Compress-Directory -SourceDirectory "$ProjPublishDirectory" -DestinationFile "$(Get-Path -Paths @($ProjectsDropRootPath,$ProjectFileInfo.BaseName,"zipped","$nugetFileEmulation.zip"))"
    }
}
