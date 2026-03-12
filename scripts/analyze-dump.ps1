param(
    [Parameter(Mandatory = $true)]
    [string]$DumpPath,
    [string]$OutputPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot

try {
    dotnet tool restore | Out-Host

    $resolvedDumpPath = Resolve-Path $DumpPath
    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        $dumpDirectory = Split-Path $resolvedDumpPath -Parent
        $OutputPath = Join-Path $dumpDirectory "dump-analysis.txt"
    }

    $analysisCommands = @(
        "clrthreads",
        "threads",
        "clrstack -all",
        "dumpasync -waiting",
        "syncblk",
        "exit"
    )

    $commandArguments = @("dotnet-dump", "analyze", $resolvedDumpPath)
    foreach ($command in $analysisCommands) {
        $commandArguments += "-c"
        $commandArguments += $command
    }

    & dotnet @commandArguments *>&1 | Tee-Object -FilePath $OutputPath | Out-Host

    Write-Host ""
    Write-Host "Analysis written to:"
    Write-Host "  $OutputPath"
}
finally {
    Pop-Location
}
