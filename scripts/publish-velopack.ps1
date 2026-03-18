param(
    [string]$Version = "",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Framework = "net10.0",
    [string]$PackId = "PotatoMaker",
    [string]$PackTitle = "PotatoMaker",
    [string]$Authors = "PotatoMaker",
    [string]$Channel = "win-x64",
    [string]$GitHubRepoUrl = "",
    [string]$GitHubToken = "",
    [string]$FfmpegDir = "",
    [switch]$SkipFfmpeg,
    [bool]$SingleFile = $false,
    [bool]$ReadyToRun = $false,
    [bool]$DownloadPreviousReleases = $true,
    [bool]$UploadToGitHub = $false,
    [bool]$PublishRelease = $false,
    [bool]$Prerelease = $false,
    [bool]$MergeAssets = $true,
    [string]$ReleaseName = "",
    [string]$ReleaseTag = ""
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

function Read-ChoiceValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Prompt,
        [Parameter(Mandatory = $true)]
        [string[]]$Choices,
        [Parameter(Mandatory = $true)]
        [string]$Default
    )

    while ($true) {
        Write-Host $Prompt
        for ($i = 0; $i -lt $Choices.Count; $i++) {
            $marker = if ($Choices[$i] -eq $Default) { " (default)" } else { "" }
            Write-Host "  [$($i + 1)] $($Choices[$i])$marker"
        }

        $input = (Read-Host "Choose 1-$($Choices.Count) or enter a value").Trim()
        if ([string]::IsNullOrWhiteSpace($input)) {
            return $Default
        }

        $selectedIndex = 0
        if ([int]::TryParse($input, [ref]$selectedIndex)) {
            $index = $selectedIndex - 1
            if ($index -ge 0 -and $index -lt $Choices.Count) {
                return $Choices[$index]
            }
        }

        foreach ($choice in $Choices) {
            if ($choice.Equals($input, [System.StringComparison]::OrdinalIgnoreCase)) {
                return $choice
            }
        }

        Write-Warning "Please choose one of the listed options."
    }
}

function Read-YesNo {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Prompt,
        [Parameter(Mandatory = $true)]
        [bool]$Default
    )

    $defaultLabel = if ($Default) { "Y/n" } else { "y/N" }
    while ($true) {
        $input = (Read-Host "$Prompt [$defaultLabel]").Trim()
        if ([string]::IsNullOrWhiteSpace($input)) {
            return $Default
        }

        switch -Regex ($input) {
            '^(y|yes)$' { return $true }
            '^(n|no)$' { return $false }
        }

        Write-Warning "Please answer y or n."
    }
}

function Read-ValueOrDefault {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Prompt,
        [Parameter(Mandatory = $true)]
        [string]$Default
    )

    $input = Read-Host "$Prompt [$Default]"
    if ([string]::IsNullOrWhiteSpace($input)) {
        return $Default
    }

    return $input.Trim()
}

function Read-OptionalValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Prompt,
        [string]$Default = ""
    )

    $suffix = if ([string]::IsNullOrWhiteSpace($Default)) { " (leave blank to skip)" } else { " [$Default]" }
    $input = Read-Host "$Prompt$suffix"
    if ([string]::IsNullOrWhiteSpace($input)) {
        return $Default
    }

    return $input.Trim()
}

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

function Resolve-PathFfmpegDirFromPath {
    $ffmpegCmd = Get-Command ffmpeg -ErrorAction SilentlyContinue
    $ffprobeCmd = Get-Command ffprobe -ErrorAction SilentlyContinue

    if ($null -eq $ffmpegCmd -or $null -eq $ffprobeCmd) {
        return $null
    }

    $ffmpegPathDir = Split-Path -Parent $ffmpegCmd.Source
    $ffprobePathDir = Split-Path -Parent $ffprobeCmd.Source

    if ($ffmpegPathDir -ne $ffprobePathDir) {
        return $null
    }

    return $ffmpegPathDir
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

if (-not $PSBoundParameters.ContainsKey("Configuration")) {
    $Configuration = Read-ChoiceValue -Prompt "Configuration?" -Choices @("Release", "Debug") -Default $Configuration
}

if (-not $PSBoundParameters.ContainsKey("Runtime")) {
    $Runtime = Read-ChoiceValue -Prompt "Runtime?" -Choices @("win-x64", "win-arm64", "win-x86") -Default $Runtime
}

if (-not $PSBoundParameters.ContainsKey("Framework")) {
    $Framework = Read-ValueOrDefault -Prompt "Target framework?" -Default $Framework
}

if (-not $PSBoundParameters.ContainsKey("Version")) {
    $Version = Read-ValueOrDefault -Prompt "Version?" -Default $Version
}

if (-not $PSBoundParameters.ContainsKey("Channel")) {
    $Channel = Read-ValueOrDefault -Prompt "Release channel?" -Default $Channel
}

if (-not $PSBoundParameters.ContainsKey("PackTitle")) {
    $PackTitle = Read-ValueOrDefault -Prompt "Package title?" -Default $PackTitle
}

if (-not $PSBoundParameters.ContainsKey("Authors")) {
    $Authors = Read-ValueOrDefault -Prompt "Authors?" -Default $Authors
}

if (-not $PSBoundParameters.ContainsKey("SingleFile")) {
    $SingleFile = Read-YesNo -Prompt "Publish as a single-file app?" -Default $SingleFile
}

if (-not $PSBoundParameters.ContainsKey("ReadyToRun")) {
    $ReadyToRun = Read-YesNo -Prompt "Enable ReadyToRun precompilation?" -Default $ReadyToRun
}

if (-not $SkipFfmpeg -and [string]::IsNullOrWhiteSpace($FfmpegDir)) {
    $defaultFfmpegDir = Join-Path $repoRoot "third_party\ffmpeg\$Runtime"
    if (Test-Path $defaultFfmpegDir) {
        $FfmpegDir = $defaultFfmpegDir
    }
    else {
        $pathFfmpegDir = Resolve-PathFfmpegDirFromPath
        if (-not [string]::IsNullOrWhiteSpace($pathFfmpegDir)) {
            $FfmpegDir = $pathFfmpegDir
        }
    }
}

if (-not $PSBoundParameters.ContainsKey("SkipFfmpeg") -and -not $PSBoundParameters.ContainsKey("FfmpegDir")) {
    $bundleFfmpeg = Read-YesNo -Prompt "Bundle FFmpeg into the package?" -Default (-not [string]::IsNullOrWhiteSpace($FfmpegDir))
    if (-not $bundleFfmpeg) {
        $SkipFfmpeg = $true
    }
    elseif (-not [string]::IsNullOrWhiteSpace($FfmpegDir)) {
        $useDetectedFfmpeg = Read-YesNo -Prompt "Use detected FFmpeg directory '$FfmpegDir'?" -Default $true
        if (-not $useDetectedFfmpeg) {
            $FfmpegDir = Read-Host "Enter FFmpeg directory"
        }
    }
    else {
        $FfmpegDir = Read-Host "Enter FFmpeg directory"
    }
}

if ([string]::IsNullOrWhiteSpace($GitHubToken)) {
    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
        $GitHubToken = $env:GITHUB_TOKEN
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:POTATOMAKER_GITHUB_TOKEN)) {
        $GitHubToken = $env:POTATOMAKER_GITHUB_TOKEN
    }
}

if (-not $PSBoundParameters.ContainsKey("GitHubRepoUrl") -and
    -not $PSBoundParameters.ContainsKey("UploadToGitHub") -and
    -not $PSBoundParameters.ContainsKey("DownloadPreviousReleases")) {
    $useGitHub = Read-YesNo -Prompt "Use GitHub release metadata or upload?" -Default (-not [string]::IsNullOrWhiteSpace($GitHubRepoUrl))
    if ($useGitHub) {
        $GitHubRepoUrl = Read-OptionalValue -Prompt "GitHub repo URL" -Default $GitHubRepoUrl
        $DownloadPreviousReleases = Read-YesNo -Prompt "Download previous Velopack releases first?" -Default $DownloadPreviousReleases
        $UploadToGitHub = Read-YesNo -Prompt "Upload the packaged release to GitHub?" -Default $UploadToGitHub
    }
    else {
        $DownloadPreviousReleases = $false
        $UploadToGitHub = $false
    }
}

if ($UploadToGitHub -and [string]::IsNullOrWhiteSpace($GitHubRepoUrl)) {
    $GitHubRepoUrl = Read-OptionalValue -Prompt "GitHub repo URL" -Default $GitHubRepoUrl
}

if (($UploadToGitHub -or $DownloadPreviousReleases) -and -not $PSBoundParameters.ContainsKey("GitHubToken")) {
    $GitHubToken = Read-OptionalValue -Prompt "GitHub token" -Default $GitHubToken
}

if ($UploadToGitHub -and -not $PSBoundParameters.ContainsKey("Prerelease")) {
    $Prerelease = Read-YesNo -Prompt "Mark the GitHub release as prerelease?" -Default $Prerelease
}

if ($UploadToGitHub -and -not $PSBoundParameters.ContainsKey("PublishRelease")) {
    $PublishRelease = Read-YesNo -Prompt "Publish the GitHub release immediately?" -Default $PublishRelease
}

if ($UploadToGitHub -and -not $PSBoundParameters.ContainsKey("MergeAssets")) {
    $MergeAssets = Read-YesNo -Prompt "Merge assets into an existing release when possible?" -Default $MergeAssets
}

if ([string]::IsNullOrWhiteSpace($ReleaseName)) {
    $ReleaseName = "$PackTitle $Version"
}

if ([string]::IsNullOrWhiteSpace($ReleaseTag)) {
    $ReleaseTag = "v$Version"
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

if (-not [string]::IsNullOrWhiteSpace($GitHubRepoUrl) -and $DownloadPreviousReleases) {
    $downloadArgs = @(
        "--yes",
        "vpk@$velopackCliVersion",
        "download",
        "github",
        "--repoUrl", $GitHubRepoUrl,
        "--outputDir", $releaseDir,
        "--channel", $Channel
    )

    if (-not [string]::IsNullOrWhiteSpace($GitHubToken)) {
        $downloadArgs += "--token"
        $downloadArgs += $GitHubToken
    }

    if ($Prerelease) {
        $downloadArgs += "--pre"
    }

    Write-Host "Downloading prior Velopack release metadata from GitHub..."
    Invoke-External -FilePath "dnx" -Arguments $downloadArgs
}

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

if ($UploadToGitHub) {
    if ([string]::IsNullOrWhiteSpace($GitHubRepoUrl)) {
        throw "GitHub upload requested, but -GitHubRepoUrl was not provided."
    }

    $uploadArgs = @(
        "--yes",
        "vpk@$velopackCliVersion",
        "upload",
        "github",
        "--repoUrl", $GitHubRepoUrl,
        "--outputDir", $releaseDir,
        "--channel", $Channel,
        "--releaseName", $ReleaseName,
        "--tag", $ReleaseTag
    )

    if (-not [string]::IsNullOrWhiteSpace($GitHubToken)) {
        $uploadArgs += "--token"
        $uploadArgs += $GitHubToken
    }

    if ($Prerelease) {
        $uploadArgs += "--pre"
    }

    if ($PublishRelease) {
        $uploadArgs += "--publish"
    }

    if ($MergeAssets) {
        $uploadArgs += "--merge"
    }

    Write-Host "Uploading Velopack release feed assets to GitHub..."
    Invoke-External -FilePath "dnx" -Arguments $uploadArgs
}

Write-Host "Velopack artifacts ready:"
Write-Host "  Releases: $releaseDir"
