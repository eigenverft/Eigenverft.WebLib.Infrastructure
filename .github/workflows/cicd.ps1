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
$GitHubToken = Get-ConfigValue -Check $GitHubToken -FilePath (Join-Path $PSScriptRoot 'cicd.secrets.json') -Property 'GitHubToken'
$NuGetApiKey = Get-ConfigValue -Check $NuGetApiKey -FilePath (Join-Path $PSScriptRoot 'cicd.secrets.json') -Property 'NuGetApiKey'
$IntTestNuGetApiKey = Get-ConfigValue -Check $IntTestNuGetApiKey -FilePath (Join-Path $PSScriptRoot 'cicd.secrets.json') -Property 'IntTestNuGetApiKey'
$PowerShellGalleryApiKey = Get-ConfigValue -Check $PowerShellGalleryApiKey -FilePath (Join-Path $PSScriptRoot 'cicd.secrets.json') -Property 'PowerShellGalleryApiKey'
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
$GeneratedVersion = Convert-DateTimeTo64SecVersionComponents -VersionBuild 1 -VersionMajor 0
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

# GitHub Pages publication model
# ------------------------------
# The versioned build tree under output/docs is intentionally NOT committed. It is an immutable
# build result and may contain many versions of the same deployment channel.
#
# The repository-level docs tree is different: it is the current/live snapshot served by GitHub
# Pages when Pages is configured as main:/docs. Documentation type comes first in the public URL,
# then the deployment channel.
#
# Example:
#   output/docs/.../production/<version>/docfx   -> versioned build result
#   docs/docfx/production                       -> current production documentation
#   docs/docfx/quality                          -> current quality/integration documentation
#   docs/reports/production                     -> current production build/release reports
#
# docs/index.html and docs/.nojekyll are durable root files and are never part of a channel mirror.
$GitHubPagesDocsRootPath = Get-Path -Paths @("$GitRepositoryRoot","docs")
$GitHubPagesReportsChannelPath = Get-Path -Paths @("$GitHubPagesDocsRootPath","reports",$BranchDeploymentConfig.Channel.Value)
$GitHubPagesDocFxChannelPath = Get-Path -Paths @("$GitHubPagesDocsRootPath","docfx",$BranchDeploymentConfig.Channel.Value)
$GitHubPagesStagingRootPath = Get-Path -Paths @("$OutputRootPath","pages")
$GitHubPagesReportsChannelStagingPath = Get-Path -Paths @("$GitHubPagesStagingRootPath","reports",$BranchDeploymentConfig.Channel.Value)
$GitHubPagesDocFxChannelStagingPath = Get-Path -Paths @("$GitHubPagesStagingRootPath","docfx",$BranchDeploymentConfig.Channel.Value)

# Main pipeline preparation: discover every solution below src and resolve its projects.
# The resulting solution-to-project execution plan drives all subsequent build, test,
# pack, publish, documentation, reporting, and distribution stages.
$SolutionFileInfos = Find-FilesByPattern -Path "$GitRepositoryRoot\src" -Pattern "*.sln;*.slnx"
$SolutionProjectPaths = @()
foreach ($solutionFile in $SolutionFileInfos) {
    # Drydock returns the project paths in their deterministic execution order.
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
            # Render the checked-in DocFX templates for this concrete project/build.
            # Convert-TemplateFilePlaceholders removes the .template token from the file name, so:
            #   docfx_local.template.json -> docfx_local.json
            #   index.template.md         -> index.md
            #
            # Those rendered files are transient build inputs beside their templates; they are NOT the
            # GitHub Pages publication tree. The rendered JSON points DocFX at the versioned
            # output/docs/.../<channel>/<version>/docfx directory below. A later publication step mirrors
            # that already-built result into repository docs/docfx/<channel>/ for GitHub Pages.
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

# Publish the current deployment-channel documentation snapshot.
#
# This deliberately reuses the outputs already produced by this script. DocFX is NOT built a
# second time by a Pages-specific workflow. The same build therefore behaves consistently on a
# developer machine and in GitHub Actions:
#
#   1. Reports are generated into the versioned output/reports tree.
#   2. DocFX is generated into the versioned output/docs tree.
#   3. Reports and DocFX are staged separately under output/pages.
#   4. Reports are mirrored to docs/reports/<channel>; DocFX is mirrored to docs/docfx/<channel>.
#   5. In CI only, those current-channel snapshots are committed and pushed back to the branch.
#
# A local run intentionally stops after step 4. This makes the complete documentation result easy
# to inspect locally without a GitHub-specific deployment step or a second build implementation.
$null = New-Directory -Paths @($GitHubPagesReportsChannelStagingPath)
$null = New-Directory -Paths @($GitHubPagesDocFxChannelStagingPath)
Remove-FilesByPattern -Path "$GitHubPagesReportsChannelStagingPath" -Pattern "*"
Remove-FilesByPattern -Path "$GitHubPagesDocFxChannelStagingPath" -Pattern "*"

foreach ($SolutionProjectPath in $SolutionProjectPaths) {
    $SolutionFileInfo = $SolutionProjectPath.Sln

    foreach ($ProjectFileInfo in $SolutionProjectPath.Prj) {
        $ReportsDirectory = Get-Path -Paths @($ReportsRootPath,$SolutionFileInfo.BaseName,$ProjectFileInfo.BaseName,$ChannelVersionRelativePath)
        $DocsDirectory = Get-Path -Paths @($DocsRootPath,$SolutionFileInfo.BaseName,$ProjectFileInfo.BaseName,$ChannelVersionRelativePath)

        # Reports are published at the channel root so the landing page can link directly to build,
        # dependency, vulnerability, license and BOM information without knowing internal output paths.
        if (Test-Path -Path "$ReportsDirectory" -PathType Container)
        {
            Copy-FilesRecursively -SourceDirectory "$ReportsDirectory" -DestinationDirectory "$GitHubPagesReportsChannelStagingPath" -Filter "*" -CopyEmptyDirs $false -ForceOverwrite $true
        }

        # Public DocFX URLs are grouped by documentation type first: /docfx/<channel>/.
        if (Test-Path -Path "$DocsDirectory\docfx" -PathType Container)
        {
            Copy-FilesRecursively -SourceDirectory "$DocsDirectory\docfx" -DestinationDirectory "$GitHubPagesDocFxChannelStagingPath" -Filter "*" -CopyEmptyDirs $false -ForceOverwrite $true -CleanDestination MirrorTree
        }
    }
}

# Build a small browsable index for the report channel from the files that were actually produced.
# This page is generated from the same local/CI staging tree as the reports themselves, so
# docs/reports/<channel>/ remains directly browsable after every MirrorTree publication.
$GitHubPagesReportFileInfos = @(Get-ChildItem -Path "$GitHubPagesReportsChannelStagingPath" -File | Sort-Object Name)
$GitHubPagesReportLinks = @()
foreach ($GitHubPagesReportFileInfo in $GitHubPagesReportFileInfos)
{
    $GitHubPagesReportFileNameHtml = [System.Net.WebUtility]::HtmlEncode($GitHubPagesReportFileInfo.Name)
    $GitHubPagesReportFileUrl = [System.Uri]::EscapeDataString($GitHubPagesReportFileInfo.Name)
    $GitHubPagesReportLinks += ('<li><a href="{0}">{1}</a></li>' -f $GitHubPagesReportFileUrl,$GitHubPagesReportFileNameHtml)
}

$GitHubPagesReportListHtml = if ($GitHubPagesReportLinks.Count -gt 0) { $GitHubPagesReportLinks -join [Environment]::NewLine } else { "<li>No reports were produced by this build.</li>" }
$GitHubPagesReportIndexHtml = @"
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>$DeploymentChannel build reports - $GitRepositoryName</title>
</head>
<body>
  <main>
    <h1>$DeploymentChannel build reports</h1>
    <p>Reports produced by the current $DeploymentChannel CI/CD snapshot.</p>
    <ul>
$GitHubPagesReportListHtml
    </ul>
    <p><a href="../../docfx/$DeploymentChannel/">Open $DeploymentChannel DocFX documentation</a></p>
    <p><a href="../../">Back to documentation overview</a></p>
  </main>
</body>
</html>
"@
Set-Content -Path (Get-Path -Paths @($GitHubPagesReportsChannelStagingPath,"index.html")) -Value $GitHubPagesReportIndexHtml -Encoding UTF8

# Mirror only the current channel leaves. Durable docs root files and other channels stay untouched.
$null = New-Directory -Paths @($GitHubPagesReportsChannelPath)
$null = New-Directory -Paths @($GitHubPagesDocFxChannelPath)
Copy-FilesRecursively -SourceDirectory "$GitHubPagesReportsChannelStagingPath" -DestinationDirectory "$GitHubPagesReportsChannelPath" -Filter "*" -CopyEmptyDirs $false -ForceOverwrite $true -CleanDestination MirrorTree
Copy-FilesRecursively -SourceDirectory "$GitHubPagesDocFxChannelStagingPath" -DestinationDirectory "$GitHubPagesDocFxChannelPath" -Filter "*" -CopyEmptyDirs $false -ForceOverwrite $true -CleanDestination MirrorTree

if ($RunEnvironment.IsCI)
{
    # CI owns publication of the live channel snapshot. The workflow only triggers automatically for
    # src/** changes, so this docs-only commit does not start another release and cannot form a loop.
    # The workflow requires `permissions: contents: write`; repository Actions permissions must also
    # allow a read/write GITHUB_TOKEN.
    #
    # Do not call Invoke-GitAddCommitPush when the snapshot is byte-for-byte unchanged: git commit
    # returns exit code 1 for "nothing to commit", which should not turn a successful rebuild into a
    # failed release.
    $GitHubPagesReportsGitPath = "docs/reports/$DeploymentChannel"
    $GitHubPagesDocFxGitPath = "docs/docfx/$DeploymentChannel"
    $GitHubPagesChannelGitStatus = @(& git -C "$GitRepositoryRoot" status --porcelain -- "$GitHubPagesReportsGitPath" "$GitHubPagesDocFxGitPath")
    if ($LASTEXITCODE -ne 0)
    {
        throw "Unable to inspect Git status for '$DeploymentChannel' documentation publication paths."
    }

    if ($GitHubPagesChannelGitStatus.Count -gt 0)
    {
        Invoke-GitAddCommitPush -TopLevelDirectory "$GitRepositoryRoot" -Folders @("$GitHubPagesReportsGitPath","$GitHubPagesDocFxGitPath") -CurrentBranch "$GitCurrentBranch" -CommitMessage "Update $DeploymentChannel documentation [skip ci]" -SafeDirectory -ExitOnError
    }
    else
    {
        Write-Host "Documentation snapshot for '$DeploymentChannel' is unchanged. Git commit/push skipped."
    }
}
else
{
    Write-Host "Documentation snapshots updated locally at '$GitHubPagesReportsChannelPath' and '$GitHubPagesDocFxChannelPath'. Git commit/push skipped outside CI."
}

# Enrich every project publish tree before creating distributable drops.
# A repository can contain multiple solutions and every solution can contain multiple projects.
# Their publish trees remain isolated as publish/<solution>/<project>/<channel>/<version>.
# Compliance files are copied next to the binaries; DocFX output is kept below a
# project-specific DOCFX directory so aggregated documentation remains distinguishable.
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

# Remove build-only symbol files from every enriched project publish tree.
# All repository-, solution-, and project-level drops below are created from these cleaned trees.
foreach ($SolutionProjectPath in $SolutionProjectPaths) {
    foreach ($ProjectFileInfo in $SolutionProjectPath.Prj) {
        $SolutionFileInfo = $SolutionProjectPath.Sln
            $PublishDirectory = New-Directory -Paths @($PublishRootPath,$SolutionFileInfo.BaseName,$ProjectFileInfo.BaseName,$ChannelVersionRelativePath)
            Remove-FilesByPattern -Path "$PublishDirectory" -Pattern "*.pdb"
     }
}

# Every aggregation level below is exposed as:
# - <channel>/<version>: version-specific snapshot
# - <channel>/latest: refreshed copy of the latest version in that channel
# - distributed: refreshed channel-independent distribution
# - zipped/<name>.<version>-<affix>.zip: NuGet-style file name for a regular ZIP archive

# Build the repository-level all-in-one drop by flattening the publish trees of every
# project from every solution. Project output file names are therefore expected to be unique.
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


# Build one solution-level drop by flattening all project publish trees belonging to that
# solution. The solution staging directory is cleared first to prevent stale artifacts.
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

# Build one project-level drop for every solution/project association.
# Project drops are keyed by project base name, which must be unique across the repository.
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
