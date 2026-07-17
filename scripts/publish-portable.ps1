param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Framework = "net10.0",
    [string]$Version = "",
    [bool]$SingleFile = $false,
    [bool]$ReadyToRun = $false,
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

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-DefaultSemanticVersion -Path $versionPropsPath
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
