param(
    [string]$Configuration = "Release",
    [switch]$SkipBuild,
    [switch]$SkipAnalyze,
    [switch]$SkipTests,
    [switch]$SkipPvs,
    [switch]$Coverage
)

# Single entry point for the blocking quality gates:
#   powershell -ExecutionPolicy Bypass -File gates.ps1
#
# Stages (fail-fast, each timed):
#   1. build.ps1 -SkipPvs  (raw csc compile check; PVS is stage 4, not doubled)
#   2. analyze.ps1 -Profile quick  (SourceCheck neural-slop guard + Roslynator baseline gate)
#   3. tests.ps1  (xunit, 1135 PASS)
#   4. pvs.ps1  (PVS-Studio, blocking)
#   5. coverage.ps1  (opt-in via -Coverage: slow, runs the suite under dotnet-coverage)
#
# Each stage runs in a CHILD powershell process: every stage script calls
# `exit`, which would terminate this orchestrator if invoked with & in-process.
# Exit code is 0 only when every executed stage passes.
#
# PS 5.1-compatible (no ternary, no ??, no ForEach-Object -Parallel).

$stages = @(
    @{ Name = "Build (csc)"; Script = "build.ps1"; Args = @("-Configuration", $Configuration, "-SkipPvs"); Skip = [bool]$SkipBuild },
    @{ Name = "Analyze (SourceCheck + quick)"; Script = "analyze.ps1"; Args = @("-Profile", "quick"); Skip = [bool]$SkipAnalyze },
    @{ Name = "Tests (xunit)"; Script = "tests.ps1"; Args = @("-Configuration", $Configuration); Skip = [bool]$SkipTests },
    @{ Name = "PVS-Studio"; Script = "pvs.ps1"; Args = @("-Configuration", $Configuration); Skip = [bool]$SkipPvs }
)
if ($Coverage) {
    $stages += @{ Name = "Coverage"; Script = "coverage.ps1"; Args = @("-Configuration", $Configuration); Skip = $false }
}

$results = @()
$failed = $false
foreach ($stage in $stages) {
    if ($stage.Skip) {
        $results += New-Object PSObject -Property @{ Stage = $stage.Name; Result = "SKIPPED"; Seconds = 0 }
        continue
    }
    Write-Host ""
    Write-Host "===== [gates] $($stage.Name) =====" -ForegroundColor Cyan
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot $stage.Script) @($stage.Args)
    $code = $LASTEXITCODE
    $sw.Stop()
    $result = "PASS"
    if ($code -ne 0) {
        $result = "FAIL($code)"
    }
    $results += New-Object PSObject -Property @{
        Stage = $stage.Name
        Result = $result
        Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1)
    }
    if ($code -ne 0) {
        $failed = $true
        break
    }
}

Write-Host ""
Write-Host "===== [gates] summary =====" -ForegroundColor Cyan
"{0,-36} {1,-10} {2,8}" -f "Stage", "Result", "Seconds"
foreach ($r in $results) {
    $color = "Green"
    if ($r.Result -like "FAIL*") {
        $color = "Red"
    } elseif ($r.Result -eq "SKIPPED") {
        $color = "Yellow"
    }
    Write-Host ("{0,-36} {1,-10} {2,8}" -f $r.Stage, $r.Result, $r.Seconds) -ForegroundColor $color
}
$total = ($results | Measure-Object -Property Seconds -Sum).Sum
Write-Host ("{0,-36} {1,-10} {2,8}" -f "Total", "", [math]::Round($total, 1))

if ($failed) {
    Write-Host "[gates] FAILED - see the first failed stage above" -ForegroundColor Red
    exit 1
}
Write-Host "[gates] ALL GREEN" -ForegroundColor Green
exit 0
