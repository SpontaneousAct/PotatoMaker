param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Framework = "net10.0",
    [string]$Version = "",
    [bool]$SingleFile = $false,
    [bool]$ReadyToRun = $false,
    [string]$FfmpegDir = "",
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

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-DefaultSemanticVersion -Path $versionPropsPath
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

if (-not $SkipFfmpeg) {
    if ([string]::IsNullOrWhiteSpace($FfmpegDir)) {
        throw "FFmpeg directory not provided. Add ffmpeg.exe + ffprobe.exe to third_party\\ffmpeg\\$Runtime, or pass -FfmpegDir <path>, or install both tools on PATH, or use -SkipFfmpeg."
    }

    $ffmpegDirPath = (Resolve-Path $FfmpegDir).Path
    $ffmpegExe = Join-Path $ffmpegDirPath "ffmpeg.exe"
    $ffprobeExe = Join-Path $ffmpegDirPath "ffprobe.exe"

    if (-not (Test-Path $ffmpegExe)) { throw "Missing ffmpeg.exe in $ffmpegDirPath" }
    if (-not (Test-Path $ffprobeExe)) { throw "Missing ffprobe.exe in $ffmpegDirPath" }
    Write-Host "Bundling FFmpeg from: $ffmpegDirPath"

    $targetFfmpegDir = Join-Path $packageDir "ffmpeg"
    New-Item -ItemType Directory -Path $targetFfmpegDir -Force | Out-Null

    Copy-Item $ffmpegExe -Destination $targetFfmpegDir -Force
    Copy-Item $ffprobeExe -Destination $targetFfmpegDir -Force

    Get-ChildItem -Path $ffmpegDirPath -File -Include *.txt,LICENSE* -ErrorAction SilentlyContinue |
        Copy-Item -Destination $targetFfmpegDir -Force
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
