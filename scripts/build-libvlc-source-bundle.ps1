param(
    [Parameter(Mandatory = $true)]
    [string]$BashPath,
    [Parameter(Mandatory = $true)]
    [string]$WorkDir,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,
    [string]$NugetPackagePath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$manifestPath = Join-Path $repoRoot "third_party\libvlc\manifests\win-x64.json"
$fetchScript = Join-Path $scriptRoot "fetch-libvlc-contrib.sh"

function Get-VerifiedDownload {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$Sha256,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    Invoke-WebRequest -Uri $Url -OutFile $Destination
    $actualHash = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash
    if ($actualHash -ne $Sha256) {
        throw "Download hash mismatch for $Url. Expected $Sha256, got $actualHash."
    }
}

function ConvertTo-MsysPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $converted = (& $BashPath -lc "cygpath -u `"$Path`"").Trim()
    if ([string]::IsNullOrWhiteSpace($converted)) {
        throw "Could not convert path for MSYS2: $Path"
    }
    return $converted
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$work = [System.IO.Path]::GetFullPath($WorkDir)
if ($work -eq [System.IO.Path]::GetPathRoot($work)) { throw "Refusing to reset filesystem root: $work" }
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
New-Item -ItemType Directory -Path $work -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($NugetPackagePath)) {
    $NugetPackagePath = Join-Path $env:USERPROFILE ".nuget\packages\videolan.libvlc.windows\$($manifest.version)\videolan.libvlc.windows.$($manifest.version).nupkg"
}
if (-not (Test-Path -LiteralPath $NugetPackagePath)) {
    throw "Restored LibVLC NuGet package not found: $NugetPackagePath"
}
$nugetHash = (Get-FileHash -LiteralPath $NugetPackagePath -Algorithm SHA256).Hash
if ($nugetHash -ne $manifest.nugetSha256) {
    throw "LibVLC NuGet package hash mismatch. Expected $($manifest.nugetSha256), got $nugetHash."
}

$downloads = Join-Path $work "downloads"
$extract = Join-Path $work "extract"
$stage = Join-Path $work "bundle"
New-Item -ItemType Directory -Path $downloads, $extract, $stage -Force | Out-Null

$vlcArchive = Join-Path $downloads $manifest.sourceArchiveName
$packagingArchive = Join-Path $downloads $manifest.packagingSourceArchiveName
Get-VerifiedDownload $manifest.sourceUrl $manifest.sourceSha256 $vlcArchive
Get-VerifiedDownload $manifest.packagingSourceUrl $manifest.packagingSourceSha256 $packagingArchive

& tar -xf $vlcArchive -C $extract
if ($LASTEXITCODE -ne 0) { throw "Could not extract VLC source archive." }
$vlcSource = @(Get-ChildItem -LiteralPath $extract -Directory | Where-Object Name -like "vlc-*")
if ($vlcSource.Count -ne 1) { throw "Expected one extracted VLC source directory, found $($vlcSource.Count)." }

$fetchCommand = "bash '$(ConvertTo-MsysPath $fetchScript)' " +
    "'$(ConvertTo-MsysPath $vlcSource[0].FullName)' " +
    "'$($manifest.contribHost)'"
& $BashPath -lc $fetchCommand
if ($LASTEXITCODE -ne 0) { throw "Fetching LibVLC contrib sources failed with exit code $LASTEXITCODE." }

$tarballs = Join-Path $vlcSource[0].FullName "contrib\tarballs"
if (-not (Test-Path -LiteralPath $tarballs)) { throw "VLC contrib source tarballs were not created." }

Copy-Item -LiteralPath $vlcArchive -Destination $stage
Copy-Item -LiteralPath $packagingArchive -Destination $stage
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $stage "source-manifest.json")
Copy-Item -LiteralPath $fetchScript -Destination $stage
Copy-Item -LiteralPath $MyInvocation.MyCommand.Path -Destination $stage
Copy-Item -LiteralPath (Join-Path $vlcSource[0].FullName "contrib\src") -Destination (Join-Path $stage "contrib-build-recipes") -Recurse
Copy-Item -LiteralPath $tarballs -Destination (Join-Path $stage "contrib-source-archives") -Recurse
Copy-Item -LiteralPath (Join-Path $repoRoot "third_party\notices\licenses\LGPL-2.1.txt") -Destination $stage

$provenance = @"
LibVLC corresponding source bundle
==================================

NuGet package: $($manifest.nugetPackage) $($manifest.version)
NuGet package SHA-256: $($manifest.nugetSha256)
Official Windows binary: $($manifest.officialBinaryUrl)
Official Windows binary SHA-256: $($manifest.officialBinarySha256)
VLC source: $($manifest.sourceUrl)
VLC source SHA-256: $($manifest.sourceSha256)
LibVLC NuGet packaging commit: $($manifest.packagingCommit)

The contrib-source-archives directory was produced by VLC's own contrib build
recipes for host $($manifest.contribHost). Those recipes verify the upstream
archives against the checksums shipped in the VLC $($manifest.version) source tree.
"@
[System.IO.File]::WriteAllText((Join-Path $stage "SOURCE-PROVENANCE.txt"), $provenance, [System.Text.UTF8Encoding]::new($false))

$outputParent = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $outputParent -Force | Out-Null
if (Test-Path -LiteralPath $OutputPath) { Remove-Item -LiteralPath $OutputPath -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $OutputPath -CompressionLevel Optimal
Write-Host "LibVLC corresponding source bundle: $OutputPath"
Write-Host "SHA-256: $((Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash)"
