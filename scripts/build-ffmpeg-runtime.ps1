param(
    [Parameter(Mandatory = $true)]
    [string]$BashPath,
    [Parameter(Mandatory = $true)]
    [string]$WorkDir,
    [Parameter(Mandatory = $true)]
    [string]$OutputDir,
    [Parameter(Mandatory = $true)]
    [string]$RuntimeManifestPath,
    [Parameter(Mandatory = $true)]
    [string]$SourceBundlePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$sourceManifestPath = Join-Path $repoRoot "third_party\ffmpeg\manifests\source-win-x64.json"
$buildScriptPath = Join-Path $scriptRoot "build-ffmpeg-runtime.sh"

function Reset-Directory {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if ($fullPath -eq [System.IO.Path]::GetPathRoot($fullPath)) {
        throw "Refusing to reset filesystem root: $fullPath"
    }

    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
    return $fullPath
}

function Get-PinnedFile {
    param(
        [Parameter(Mandatory = $true)]$Source,
        [Parameter(Mandatory = $true)][string]$DestinationDirectory
    )

    $destination = Join-Path $DestinationDirectory $Source.archiveName
    Invoke-WebRequest -Uri $Source.url -OutFile $destination
    $actualHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
    if ($actualHash -ne $Source.sha256) {
        throw "Source hash mismatch for $($Source.name). Expected $($Source.sha256), got $actualHash."
    }
    return $destination
}

function Get-SingleSourceDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    $directories = @(Get-ChildItem -LiteralPath $Path -Directory)
    if ($directories.Count -ne 1) {
        throw "Expected one source directory under $Path, found $($directories.Count)."
    }
    return $directories[0].FullName
}

function ConvertTo-MsysPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $converted = (& $BashPath -lc "cygpath -u `"$Path`"").Trim()
    if ([string]::IsNullOrWhiteSpace($converted)) {
        throw "Could not convert path for MSYS2: $Path"
    }
    return $converted
}

function Assert-ListedComponent {
    param(
        [Parameter(Mandatory = $true)][string]$Output,
        [Parameter(Mandatory = $true)][string]$Component,
        [Parameter(Mandatory = $true)][string]$Kind
    )

    if ($Output -notmatch "(?m)^\s*[A-Z\.]{1,8}\s+$([regex]::Escape($Component))(?:\s|,|$)") {
        throw "Built FFmpeg is missing required $Kind '$Component'."
    }
}

foreach ($requiredPath in @($BashPath, $sourceManifestPath, $buildScriptPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required build input not found: $requiredPath"
    }
}

$manifest = Get-Content -LiteralPath $sourceManifestPath -Raw | ConvertFrom-Json
if ($manifest.runtime -ne "win-x64") {
    throw "Unsupported FFmpeg source manifest runtime: $($manifest.runtime)"
}

$work = Reset-Directory -Path $WorkDir
$output = Reset-Directory -Path $OutputDir
$downloads = Join-Path $work "downloads"
$sources = Join-Path $work "sources"
New-Item -ItemType Directory -Path $downloads, $sources -Force | Out-Null

$sourceDirectories = @{}
foreach ($source in $manifest.sources) {
    $archive = Get-PinnedFile -Source $source -DestinationDirectory $downloads
    $extractDir = Join-Path $sources $source.name
    New-Item -ItemType Directory -Path $extractDir -Force | Out-Null
    if ($source.archiveType -eq "zip") {
        Expand-Archive -LiteralPath $archive -DestinationPath $extractDir
    }
    elseif ($source.archiveType -eq "tar.gz") {
        & tar -xzf $archive -C $extractDir
        if ($LASTEXITCODE -ne 0) { throw "Could not extract $archive" }
    }
    else {
        throw "Unsupported source archive type '$($source.archiveType)'."
    }
    $sourceDirectories[$source.name] = Get-SingleSourceDirectory -Path $extractDir
}

$buildCommand = "bash '$(ConvertTo-MsysPath $buildScriptPath)' " +
    "'$(ConvertTo-MsysPath $sourceDirectories['FFmpeg'])' " +
    "'$(ConvertTo-MsysPath $sourceDirectories['SVT-AV1'])' " +
    "'$(ConvertTo-MsysPath $sourceDirectories['nv-codec-headers'])' " +
    "'$(ConvertTo-MsysPath $sourceDirectories['zlib'])' " +
    "'$(ConvertTo-MsysPath (Join-Path $work 'build'))' " +
    "'$(ConvertTo-MsysPath $output)' " +
    "'$($manifest.versionLabel)'"
& $BashPath -lc $buildCommand
if ($LASTEXITCODE -ne 0) {
    throw "FFmpeg source build failed with exit code $LASTEXITCODE."
}

$ffmpegPath = Join-Path $output "ffmpeg.exe"
$ffprobePath = Join-Path $output "ffprobe.exe"
foreach ($path in @($ffmpegPath, $ffprobePath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Expected build output not found: $path" }
}

$versionOutput = (& $ffmpegPath -hide_banner -version 2>&1 | Out-String)
foreach ($flag in $manifest.requiredConfigurationFlags) {
    if (-not $versionOutput.Contains($flag, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Built FFmpeg is missing required configuration flag '$flag'."
    }
}
foreach ($flag in $manifest.forbiddenConfigurationFlags) {
    if ($versionOutput.Contains($flag, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Built FFmpeg contains forbidden configuration flag '$flag'."
    }
}

$encoders = (& $ffmpegPath -hide_banner -encoders 2>&1 | Out-String)
$decoders = (& $ffmpegPath -hide_banner -decoders 2>&1 | Out-String)
$demuxers = (& $ffmpegPath -hide_banner -demuxers 2>&1 | Out-String)
$muxers = (& $ffmpegPath -hide_banner -muxers 2>&1 | Out-String)
$filters = (& $ffmpegPath -hide_banner -filters 2>&1 | Out-String)
foreach ($component in $manifest.requiredEncoders) { Assert-ListedComponent $encoders $component "encoder" }
foreach ($component in $manifest.requiredDecoders) { Assert-ListedComponent $decoders $component "decoder" }
foreach ($component in $manifest.requiredDemuxers) { Assert-ListedComponent $demuxers $component "demuxer" }
foreach ($component in $manifest.requiredMuxers) { Assert-ListedComponent $muxers $component "muxer" }
foreach ($component in $manifest.requiredFilters) { Assert-ListedComponent $filters $component "filter" }

$bundleStage = Join-Path $work "source-bundle"
New-Item -ItemType Directory -Path $bundleStage -Force | Out-Null
Copy-Item -LiteralPath $sourceManifestPath -Destination (Join-Path $bundleStage "source-manifest.json")
Copy-Item -LiteralPath $buildScriptPath -Destination $bundleStage
Copy-Item -LiteralPath $MyInvocation.MyCommand.Path -Destination $bundleStage
Copy-Item -LiteralPath (Join-Path $output "BUILD-ENVIRONMENT.txt") -Destination $bundleStage
Copy-Item -Path (Join-Path $downloads "*") -Destination $bundleStage
Copy-Item -LiteralPath (Join-Path $repoRoot "third_party\notices\licenses\GPL-2.0.txt") -Destination $bundleStage
Copy-Item -LiteralPath (Join-Path $repoRoot "third_party\notices\licenses\SVT-AV1-BSD-3-Clause-Clear.txt") -Destination $bundleStage
Copy-Item -LiteralPath (Join-Path $repoRoot "third_party\notices\licenses\NVIDIA-Codec-Headers-MIT.txt") -Destination $bundleStage
Copy-Item -LiteralPath (Join-Path $repoRoot "third_party\notices\licenses\zlib.txt") -Destination $bundleStage

$bundleParent = Split-Path -Parent $SourceBundlePath
New-Item -ItemType Directory -Path $bundleParent -Force | Out-Null
if (Test-Path -LiteralPath $SourceBundlePath) { Remove-Item -LiteralPath $SourceBundlePath -Force }
Compress-Archive -Path (Join-Path $bundleStage "*") -DestinationPath $SourceBundlePath -CompressionLevel Optimal

$runtimeManifest = [ordered]@{
    runtime = "win-x64"
    distributor = "PotatoMaker project"
    versionContains = $manifest.versionLabel
    license = $manifest.license
    sourceCommit = $manifest.sources[0].commit
    sourceUrl = $manifest.sources[0].url
    sourceArchiveUrl = $manifest.sources[0].url
    sourceBundleName = Split-Path -Leaf $SourceBundlePath
    sourceBundleSha256 = (Get-FileHash -LiteralPath $SourceBundlePath -Algorithm SHA256).Hash
    buildRecipe = "scripts/build-ffmpeg-runtime.ps1 and scripts/build-ffmpeg-runtime.sh"
    ffmpegSha256 = (Get-FileHash -LiteralPath $ffmpegPath -Algorithm SHA256).Hash
    ffprobeSha256 = (Get-FileHash -LiteralPath $ffprobePath -Algorithm SHA256).Hash
    requiredConfigurationFlags = @($manifest.requiredConfigurationFlags)
    forbiddenConfigurationFlags = @($manifest.forbiddenConfigurationFlags)
}

$manifestParent = Split-Path -Parent $RuntimeManifestPath
New-Item -ItemType Directory -Path $manifestParent -Force | Out-Null
$runtimeManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $RuntimeManifestPath -Encoding utf8NoBOM
Write-Host "FFmpeg runtime: $output"
Write-Host "Runtime manifest: $RuntimeManifestPath"
Write-Host "Corresponding source bundle: $SourceBundlePath"
