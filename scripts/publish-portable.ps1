param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Framework = "net10.0",
    [string]$Version = "",
    [bool]$SingleFile = $false,
    [bool]$ReadyToRun = $false,
    [string]$FfmpegDir = "",
    [string]$FfmpegManifestPath = "",
    [switch]$SkipFfmpeg,
    [switch]$SkipZip
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

$artifactsDir = Join-Path $repoRoot "artifacts"
$packageName = "PotatoMaker"
$guiProject = Join-Path $repoRoot "PotatoMaker.GUI\PotatoMaker.GUI.csproj"
$versionPropsPath = Join-Path $repoRoot "Directory.Build.props"
$publishRoot = Join-Path $artifactsDir "publish"
$portableRoot = Join-Path $artifactsDir "portable"
$noticesSourceDir = Join-Path $repoRoot "third_party\notices"
$defaultFfmpegManifestPath = Join-Path $repoRoot "third_party\ffmpeg\$Runtime\runtime-manifest.json"

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

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-DefaultSemanticVersion -Path $versionPropsPath
}

if ([string]::IsNullOrWhiteSpace($FfmpegManifestPath)) {
    $FfmpegManifestPath = $defaultFfmpegManifestPath
}

function Get-ExecutableVersionOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath
    )

    $output = & $FilePath -hide_banner -version 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect executable: $FilePath"
    }

    return $output.Trim()
}

function Test-ApprovedFfmpegBuild {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FfmpegPath,
        [Parameter(Mandatory = $true)]
        [string]$FfprobePath,
        [Parameter(Mandatory = $true)]
        [string]$ManifestPath,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedRuntime
    )

    if (-not (Test-Path -LiteralPath $ManifestPath)) {
        throw "Approved FFmpeg manifest not found: $ManifestPath"
    }

    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if ($manifest.runtime -ne $ExpectedRuntime) {
        throw "FFmpeg manifest runtime '$($manifest.runtime)' does not match '$ExpectedRuntime'."
    }

    foreach ($property in @(
        "versionContains",
        "license",
        "sourceUrl",
        "sourceArchiveUrl",
        "ffmpegSha256",
        "ffprobeSha256")) {
        if ([string]::IsNullOrWhiteSpace($manifest.$property)) {
            throw "FFmpeg manifest is missing '$property': $ManifestPath"
        }
    }

    $ffmpegHash = (Get-FileHash -LiteralPath $FfmpegPath -Algorithm SHA256).Hash
    $ffprobeHash = (Get-FileHash -LiteralPath $FfprobePath -Algorithm SHA256).Hash
    if ($ffmpegHash -ne $manifest.ffmpegSha256) {
        throw "ffmpeg.exe is not the approved build for $ExpectedRuntime. Expected $($manifest.ffmpegSha256), got $ffmpegHash."
    }
    if ($ffprobeHash -ne $manifest.ffprobeSha256) {
        throw "ffprobe.exe is not the approved build for $ExpectedRuntime. Expected $($manifest.ffprobeSha256), got $ffprobeHash."
    }

    $ffmpegOutput = Get-ExecutableVersionOutput -FilePath $FfmpegPath
    $ffprobeOutput = Get-ExecutableVersionOutput -FilePath $FfprobePath
    if (-not $ffmpegOutput.Contains($manifest.versionContains, [System.StringComparison]::OrdinalIgnoreCase) -or
        -not $ffprobeOutput.Contains($manifest.versionContains, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "FFmpeg executables do not report approved version '$($manifest.versionContains)'."
    }

    foreach ($flag in @($manifest.requiredConfigurationFlags)) {
        if (-not $ffmpegOutput.Contains($flag, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Approved FFmpeg build is missing required configuration flag '$flag'."
        }
    }
    foreach ($flag in @($manifest.forbiddenConfigurationFlags)) {
        if ($ffmpegOutput.Contains($flag, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "FFmpeg build is not redistributable because it contains forbidden configuration flag '$flag'."
        }
    }

    return [pscustomobject]@{
        Manifest = $manifest
        FfmpegHash = $ffmpegHash
        FfprobeHash = $ffprobeHash
        FfmpegOutput = $ffmpegOutput
        FfprobeOutput = $ffprobeOutput
    }
}

function Copy-ThirdPartyNotices {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    $noticesPath = Join-Path $noticesSourceDir "THIRD-PARTY-NOTICES.txt"
    $licenseSourceDir = Join-Path $noticesSourceDir "licenses"
    $potatoMakerLicense = Join-Path $repoRoot "LICENSE.txt"
    foreach ($requiredPath in @($noticesPath, $licenseSourceDir, $potatoMakerLicense)) {
        if (-not (Test-Path -LiteralPath $requiredPath)) {
            throw "Required license material not found: $requiredPath"
        }
    }

    $licenseDestination = Join-Path $Destination "licenses"
    New-Item -ItemType Directory -Path $licenseDestination -Force | Out-Null
    Copy-Item -LiteralPath $noticesPath -Destination (Join-Path $Destination "THIRD-PARTY-NOTICES.txt") -Force
    Copy-Item -Path (Join-Path $licenseSourceDir "*") -Destination $licenseDestination -Recurse -Force
    Copy-Item -LiteralPath $potatoMakerLicense -Destination (Join-Path $licenseDestination "PotatoMaker-MIT.txt") -Force
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

if (-not $PSBoundParameters.ContainsKey("SkipZip")) {
    $SkipZip = -not (Read-YesNo -Prompt "Create a portable zip archive?" -Default (-not $SkipZip))
}

$packageDir = Join-Path $publishRoot "$packageName-$Runtime"
$zipPath = Join-Path $portableRoot "$packageName-$Version-$Runtime-portable.zip"

Write-Host "Publishing GUI ($Configuration, $Runtime, $Framework)..."
if (Test-Path $packageDir) {
    Remove-Item $packageDir -Recurse -Force
}

$publishArgs = @(
    "publish",
    $guiProject,
    "-c", $Configuration,
    "-f", $Framework,
    "-r", $Runtime,
    "--self-contained", "true",
    "-o", $packageDir,
    "/p:Version=$Version",
    "/p:PublishSingleFile=$SingleFile",
    "/p:PublishReadyToRun=$ReadyToRun",
    "/p:DebugSymbols=false",
    "/p:DebugType=None"
)

if ($SingleFile) {
    $publishArgs += "/p:IncludeNativeLibrariesForSelfExtract=true"
    $publishArgs += "/p:EnableCompressionInSingleFile=true"
}

Invoke-External -FilePath "dotnet" -Arguments $publishArgs

if (-not (Test-Path $packageDir)) {
    throw "Publish output not found: $packageDir"
}

Copy-ThirdPartyNotices -Destination $packageDir

$forbiddenLibVlcPlugins = @(
    "libdolby_surround_decoder_plugin.dll",
    "libheadphone_channel_mixer_plugin.dll"
)
foreach ($pluginName in $forbiddenLibVlcPlugins) {
    $matches = @(Get-ChildItem -LiteralPath $packageDir -Recurse -File -Filter $pluginName -ErrorAction SilentlyContinue)
    if ($matches.Count -ne 0) {
        throw "GPL-only LibVLC plugin must not be packaged: $($matches[0].FullName)"
    }
}

if (-not $SkipFfmpeg) {
    if ([string]::IsNullOrWhiteSpace($FfmpegDir)) {
        throw "FFmpeg directory not provided. Add ffmpeg.exe + ffprobe.exe to third_party\\ffmpeg\\$Runtime, or pass -FfmpegDir <path>, or install both tools on PATH, or use -SkipFfmpeg."
    }

    $ffmpegDirPath = (Resolve-Path $FfmpegDir).Path
    $ffmpegExe = Join-Path $ffmpegDirPath "ffmpeg.exe"
    $ffprobeExe = Join-Path $ffmpegDirPath "ffprobe.exe"

    if (-not (Test-Path $ffmpegExe)) { throw "Missing ffmpeg.exe in $ffmpegDirPath" }
    if (-not (Test-Path $ffprobeExe)) { throw "Missing ffprobe.exe in $ffmpegDirPath" }
    $approvedFfmpeg = Test-ApprovedFfmpegBuild `
        -FfmpegPath $ffmpegExe `
        -FfprobePath $ffprobeExe `
        -ManifestPath $FfmpegManifestPath `
        -ExpectedRuntime $Runtime
    Write-Host "Bundling FFmpeg from: $ffmpegDirPath"

    $targetFfmpegDir = Join-Path $packageDir "ffmpeg"
    New-Item -ItemType Directory -Path $targetFfmpegDir -Force | Out-Null

    Copy-Item $ffmpegExe -Destination $targetFfmpegDir -Force
    Copy-Item $ffprobeExe -Destination $targetFfmpegDir -Force

    $manifest = $approvedFfmpeg.Manifest
    $sourceNotice = @"
FFmpeg binary, source, and build information
============================================

Distributor: $($manifest.distributor)
Reported version contains: $($manifest.versionContains)
License: $($manifest.license)
FFmpeg source commit: $($manifest.sourceCommit)
Source: $($manifest.sourceUrl)
Source archive: $($manifest.sourceArchiveUrl)
Corresponding source bundle: $($manifest.sourceBundleName)
Corresponding source bundle SHA-256: $($manifest.sourceBundleSha256)
Build recipe: $($manifest.buildRecipe)
ffmpeg.exe SHA-256: $($approvedFfmpeg.FfmpegHash)
ffprobe.exe SHA-256: $($approvedFfmpeg.FfprobeHash)

Configuration and versions
--------------------------
$($approvedFfmpeg.FfmpegOutput)

$($approvedFfmpeg.FfprobeOutput)
"@
    [System.IO.File]::WriteAllText(
        (Join-Path $targetFfmpegDir "FFMPEG-SOURCE.txt"),
        $sourceNotice,
        [System.Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $noticesSourceDir "licenses\GPL-2.0.txt") -Destination $targetFfmpegDir -Force
    Copy-Item -LiteralPath (Join-Path $noticesSourceDir "licenses\SVT-AV1-BSD-3-Clause-Clear.txt") -Destination $targetFfmpegDir -Force
    Copy-Item -LiteralPath (Join-Path $noticesSourceDir "licenses\NVIDIA-Codec-Headers-MIT.txt") -Destination $targetFfmpegDir -Force
    Copy-Item -LiteralPath (Join-Path $noticesSourceDir "licenses\zlib.txt") -Destination $targetFfmpegDir -Force
}

if ($SkipZip) {
    Write-Host "Portable publish staging ready:"
    Write-Host "  Folder: $packageDir"
    return
}

New-Item -ItemType Directory -Path $portableRoot -Force | Out-Null
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Push-Location $publishRoot
try {
    Compress-Archive -Path (Split-Path $packageDir -Leaf) -DestinationPath $zipPath -Force
}
finally {
    Pop-Location
}

Write-Host "Portable package ready:"
Write-Host "  Folder: $packageDir"
Write-Host "  Zip   : $zipPath"
