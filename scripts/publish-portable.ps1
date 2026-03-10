param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Framework = "net10.0",
    [bool]$SingleFile = $false,
    [string]$FfmpegDir = "",
    [switch]$SkipFfmpeg
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

$guiProject = Join-Path $repoRoot "PotatoMaker.GUI\PotatoMaker.GUI.csproj"
$publishDir = Join-Path $repoRoot "PotatoMaker.GUI\bin\$Configuration\$Framework\$Runtime\publish"
$artifactsDir = Join-Path $repoRoot "artifacts"
$packageName = "PotatoMaker"
$packageDir = Join-Path $artifactsDir $packageName
$zipPath = Join-Path $artifactsDir "$packageName.zip"

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

Write-Host "Publishing GUI ($Configuration, $Runtime, $Framework)..."
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

& dotnet restore $guiProject -r $Runtime
$publishArgs = @(
    "publish",
    $guiProject,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "/p:PublishSingleFile=$SingleFile",
    "/p:PublishReadyToRun=true"
)

if ($SingleFile) {
    $publishArgs += "/p:IncludeNativeLibrariesForSelfExtract=true"
    $publishArgs += "/p:EnableCompressionInSingleFile=true"
}

& dotnet @publishArgs

if (-not (Test-Path $publishDir)) {
    throw "Publish output not found: $publishDir"
}

New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
if (Test-Path $packageDir) {
    Remove-Item $packageDir -Recurse -Force
}

New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination $packageDir -Recurse -Force

Get-ChildItem -Path $packageDir -Recurse -File -Include *.pdb,*.lib -ErrorAction SilentlyContinue |
    Remove-Item -Force

$unusedLibVlcDir = if ($Runtime -eq "win-x64") {
    Join-Path $packageDir "libvlc\win-x86"
}
elseif ($Runtime -eq "win-x86") {
    Join-Path $packageDir "libvlc\win-x64"
}
else {
    $null
}

if ($null -ne $unusedLibVlcDir -and (Test-Path $unusedLibVlcDir)) {
    Remove-Item $unusedLibVlcDir -Recurse -Force
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

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Push-Location $artifactsDir
try {
    Compress-Archive -Path $packageName -DestinationPath $zipPath -Force
}
finally {
    Pop-Location
}

Write-Host "Portable package ready:"
Write-Host "  Folder: $packageDir"
Write-Host "  Zip   : $zipPath"
