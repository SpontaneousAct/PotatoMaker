param(
    [ValidateSet("trace", "dump", "both")]
    [string]$Mode = "both",
    [string]$ProcessName = "PotatoMaker.GUI",
    [int]$ProcessId = 0,
    [int]$TraceSeconds = 15,
    [ValidateSet("Full", "Heap", "Mini", "Triage")]
    [string]$DumpType = "Heap",
    [string]$OutputRoot = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot

try {
    function Get-TargetProcess {
        param(
            [string]$RequestedName,
            [int]$RequestedId
        )

        if ($RequestedId -gt 0) {
            return Get-Process -Id $RequestedId -ErrorAction Stop
        }

        $namedProcess = Get-Process -Name $RequestedName -ErrorAction SilentlyContinue |
            Sort-Object StartTime -Descending |
            Select-Object -First 1
        if ($null -ne $namedProcess) {
            return $namedProcess
        }

        $matchingDotnetProcess = Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" |
            Where-Object { $_.CommandLine -like "*$RequestedName*" } |
            Sort-Object CreationDate -Descending |
            Select-Object -First 1

        if ($null -ne $matchingDotnetProcess) {
            return Get-Process -Id $matchingDotnetProcess.ProcessId -ErrorAction Stop
        }

        return $null
    }

    if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
        $OutputRoot = Join-Path $repoRoot "artifacts\diagnostics"
    }

    Write-Host "Restoring local diagnostic tools..."
    dotnet tool restore | Out-Host

    Write-Host "Resolving target process..."
    $process = Get-TargetProcess -RequestedName $ProcessName -RequestedId $ProcessId

    if ($null -eq $process) {
        $availableProcesses = Get-CimInstance Win32_Process |
            Where-Object {
                $_.Name -like "*PotatoMaker*" -or
                ($_.Name -eq "dotnet.exe" -and $_.CommandLine -like "*PotatoMaker*")
            } |
            Select-Object ProcessId, Name, CommandLine

        if ($availableProcesses) {
            Write-Host "No exact process match found. Nearby candidates:" -ForegroundColor Yellow
            $availableProcesses | Format-Table -Wrap -AutoSize | Out-Host
        }

        throw "No running process matched name '$ProcessName'. Try -ProcessId <pid> if needed."
    }

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $safeProcessLabel = if ($process.ProcessName -eq "dotnet") { "dotnet-$ProcessName" } else { $process.ProcessName }
    $outputDir = Join-Path $OutputRoot "$safeProcessLabel-$timestamp"
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

    Write-Host "Capturing diagnostics for $($process.ProcessName) (PID $($process.Id)) into $outputDir"

    $stackPath = Join-Path $outputDir "managed-stacks.txt"
    Write-Host "Capturing managed stacks..."
    & dotnet dotnet-stack report -p $process.Id *>&1 | Tee-Object -FilePath $stackPath | Out-Host

    if ($Mode -eq "trace" -or $Mode -eq "both") {
        $tracePath = Join-Path $outputDir "hang.nettrace"
        Write-Host "Collecting $TraceSeconds second trace..."
        & dotnet dotnet-trace collect `
            -p $process.Id `
            --duration ("00:00:{0:00}" -f $TraceSeconds) `
            --profile cpu-sampling `
            --output $tracePath `
            --format Speedscope | Out-Host
    }

    if ($Mode -eq "dump" -or $Mode -eq "both") {
        $dumpPath = Join-Path $outputDir "hang.dmp"
        Write-Host "Collecting $DumpType dump..."
        & dotnet dotnet-dump collect `
            -p $process.Id `
            --type $DumpType `
            --output $dumpPath | Out-Host
    }

    Write-Host ""
    Write-Host "Done. Diagnostics saved to:"
    Write-Host "  $outputDir"
}
finally {
    Pop-Location
}
