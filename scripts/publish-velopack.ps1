param(
    [string]$Version = "",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Framework = "net10.0",
    [string]$PackId = "PotatoMaker",
    [string]$PackTitle = "PotatoMaker",
    [string]$Authors = "PotatoMaker",
    [string]$Channel = "win-x64",
    [string]$FfmpegDir = "",
    [switch]$SkipFfmpeg,
    [bool]$SingleFile = $false,
    [bool]$ReadyToRun = $false
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

$packageName = "PotatoMaker"
$portableScript = Join-Path $scriptRoot "publish-portable.ps1"
$packageDir = Join-Path $repoRoot "artifacts\publish\$packageName-$Runtime"
$releaseDir = Join-Path $repoRoot "artifacts\velopack\$Channel"
$iconPath = Join-Path $repoRoot "PotatoMaker.GUI\Assets\potato.ico"
$versionPropsPath = Join-Path $repoRoot "Directory.Build.props"
$packagesPropsPath = Join-Path $repoRoot "Directory.Packages.props"
$mainExe = "PotatoMaker.GUI.exe"

function Invoke-External {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Get-DefaultSemanticVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path $Path)) {
        throw "Version properties file not found: $Path"
    }

    [xml]$versionProps = Get-Content $Path
    $propertyGroups = @($versionProps.Project.PropertyGroup)
    $versionPrefixNode = $propertyGroups |
        Where-Object { $_.PSObject.Properties.Name -contains 'VersionPrefix' } |
        Select-Object -First 1
    $versionSuffixNode = $propertyGroups |
        Where-Object { $_.PSObject.Properties.Name -contains 'VersionSuffix' } |
        Select-Object -First 1

    $versionPrefix = if ($null -ne $versionPrefixNode) { $versionPrefixNode.VersionPrefix.InnerText } else { "" }
    $versionSuffix = if ($null -ne $versionSuffixNode) { $versionSuffixNode.VersionSuffix.InnerText } else { "" }

    if ([string]::IsNullOrWhiteSpace($versionPrefix)) {
        throw "VersionPrefix was not found in $Path"
    }

    if ([string]::IsNullOrWhiteSpace($versionSuffix)) {
        return $versionPrefix.Trim()
    }

    return "$($versionPrefix.Trim())-$($versionSuffix.Trim())"
}

function Get-CentralPackageVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$PackageId
    )

    if (-not (Test-Path $Path)) {
        throw "Central package version file not found: $Path"
    }

    [xml]$packageVersions = Get-Content $Path
    $packageNode = $packageVersions.Project.ItemGroup.PackageVersion |
        Where-Object { $_.Include -eq $PackageId } |
        Select-Object -First 1

    if ($null -eq $packageNode -or [string]::IsNullOrWhiteSpace($packageNode.Version)) {
        throw "Package version '$PackageId' not found in $Path"
    }

    return $packageNode.Version
}

if (-not (Test-Path $portableScript)) {
    throw "Portable packaging script not found: $portableScript"
}

if (-not (Test-Path $iconPath)) {
    throw "Installer icon not found: $iconPath"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-DefaultSemanticVersion -Path $versionPropsPath
}

$velopackCliVersion = Get-CentralPackageVersion -Path $packagesPropsPath -PackageId "Velopack"

& $portableScript `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -Framework $Framework `
    -Version $Version `
    -SingleFile:$SingleFile `
    -ReadyToRun:$ReadyToRun `
    -FfmpegDir $FfmpegDir `
    -SkipFfmpeg:$SkipFfmpeg `
    -SkipZip

if (-not (Test-Path $packageDir)) {
    throw "Expected staged package directory was not created: $packageDir"
}

New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

$packArgs = @(
    "--yes",
    "vpk@$velopackCliVersion",
    "pack",
    "--packId", $PackId,
    "--packVersion", $Version,
    "--packDir", $packageDir,
    "--mainExe", $mainExe,
    "--packTitle", $PackTitle,
    "--packAuthors", $Authors,
    "--icon", $iconPath,
    "--outputDir", $releaseDir,
    "--channel", $Channel
)

Write-Host "Packing Velopack installer ($Version, $Runtime) with vpk $velopackCliVersion..."
Invoke-External -FilePath "dnx" -Arguments $packArgs

Write-Host "Velopack artifacts ready:"
Write-Host "  Releases: $releaseDir"
