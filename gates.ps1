param(
    [string]$Configuration = "Release",
    [switch]$SkipBuild,
    [switch]$SkipAnalyze,
    [switch]$SkipTests,
    [switch]$SkipPvs,
    [switch]$Coverage,
    [switch]$Parallel
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
# -Parallel runs two lanes as background jobs (Start-Job, PS 5.1-safe) with
# live-streamed output and cross-lane fail-fast: a red fast lane stops the
# slow lane instead of letting it burn 5 minutes. The split is by shared
# MSBuild state, not by speed: Analyze and PVS both restore and build
# RadioNavigation.Analysis.csproj into the same obj/ (see the shared-obj
# warnings in analyze.ps1 and pvs.ps1), so they stay sequential inside the
# slow lane - concurrent builds of one project collide loudly on outputs.
# Build (raw csc to the repo root) and Tests (own Runner project) touch
# neither, hence the fast lane.
#
# Fast iteration loop (no slow gates, ~11 s):
#   powershell -ExecutionPolicy Bypass -File gates.ps1 -SkipAnalyze -SkipPvs
#
# PS 5.1-compatible (no ternary, no ??, no ForEach-Object -Parallel).

$stages = @(
    @{ Name = "Build (csc)"; Script = "build.ps1"; Args = @("-Configuration", $Configuration, "-SkipPvs"); Skip = [bool]$SkipBuild; Lane = "fast" },
    @{ Name = "Analyze (SourceCheck + quick)"; Script = "analyze.ps1"; Args = @("-Profile", "quick"); Skip = [bool]$SkipAnalyze; Lane = "slow" },
    @{ Name = "Tests (xunit)"; Script = "tests.ps1"; Args = @("-Configuration", $Configuration); Skip = [bool]$SkipTests; Lane = "fast" },
    @{ Name = "PVS-Studio"; Script = "pvs.ps1"; Args = @("-Configuration", $Configuration); Skip = [bool]$SkipPvs; Lane = "slow" }
)
if ($Coverage) {
    $stages += @{ Name = "Coverage"; Script = "coverage.ps1"; Args = @("-Configuration", $Configuration); Skip = $false; Lane = "fast" }
}

# A lane runner executes its stages sequentially inside a background job and
# records one "Name|Result|Seconds" line per stage. Self-contained: a job runs
# in a fresh session, so everything it needs arrives via -ArgumentList.
$laneRunner = {
    param($Root, $LaneStages, $ResultFile)
    # Background jobs start in the user's home directory, NOT the invoker's
    # working directory - without this, CWD-relative paths inside the stages
    # (e.g. Tests resolving "Source/Data") point at the wrong tree.
    Set-Location -LiteralPath $Root
    $lines = @()
    foreach ($st in $LaneStages) {
        if ($st.Skip) {
            $lines += "$($st.Name)|SKIPPED|0"
            continue
        }
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        & powershell -ExecutionPolicy Bypass -File (Join-Path $Root $st.Script) @($st.Args) 2>&1
        $code = $LASTEXITCODE
        $sw.Stop()
        $res = "PASS"
        if ($code -ne 0) {
            $res = "FAIL($code)"
        }
        $lines += "$($st.Name)|$res|$($sw.Elapsed.TotalSeconds.ToString("0.0", [cultureinfo]::InvariantCulture))"
        if ($code -ne 0) {
            break
        }
    }
    $lines | Set-Content -LiteralPath $ResultFile
}

if ($Parallel) {
    $resultDir = Join-Path $env:TEMP "opencode"
    if (-not (Test-Path -LiteralPath $resultDir)) {
        New-Item -ItemType Directory -Path $resultDir | Out-Null
    }
    $lanes = @(
        @{ Name = "fast"; Stages = @($stages | Where-Object { $_.Lane -eq "fast" }); File = (Join-Path $resultDir "gates-lane-fast.txt") },
        @{ Name = "slow"; Stages = @($stages | Where-Object { $_.Lane -eq "slow" }); File = (Join-Path $resultDir "gates-lane-slow.txt") }
    )
    foreach ($lane in $lanes) {
        Remove-Item -LiteralPath $lane.File -ErrorAction SilentlyContinue
        $lane.Job = Start-Job -ScriptBlock $laneRunner -ArgumentList $PSScriptRoot, $lane.Stages, $lane.File
    }
    $wall = [System.Diagnostics.Stopwatch]::StartNew()
    $killed = $false
    while (@($lanes | Where-Object { $_.Job.State -eq "Running" }).Count -gt 0) {
        foreach ($lane in $lanes) {
            Receive-Job $lane.Job
        }
        foreach ($lane in $lanes) {
            if ((Test-Path -LiteralPath $lane.File) -and ((Get-Content -LiteralPath $lane.File) -match "\|FAIL")) {
                foreach ($other in $lanes) {
                    if ($other.Job.State -eq "Running") {
                        Stop-Job $other.Job
                    }
                }
                $killed = $true
                break
            }
        }
        if ($killed) {
            break
        }
        Start-Sleep -Seconds 5
    }
    foreach ($lane in $lanes) {
        Receive-Job $lane.Job
    }
    $wall.Stop()

    $byName = @{}
    foreach ($lane in $lanes) {
        if (Test-Path -LiteralPath $lane.File) {
            foreach ($line in (Get-Content -LiteralPath $lane.File)) {
                $parts = $line -split "\|"
                $byName[$parts[0]] = @{ Result = $parts[1]; Seconds = $parts[2] }
            }
        }
    }
    Write-Host ""
    Write-Host "===== [gates] summary (parallel) =====" -ForegroundColor Cyan
    "{0,-36} {1,-10} {2,8}" -f "Stage", "Result", "Seconds"
    $failed = $false
    foreach ($st in $stages) {
        $entry = $byName[$st.Name]
        $res = "UNKNOWN"
        $sec = "-"
        if ($null -ne $entry) {
            $res = $entry.Result
            $sec = $entry.Seconds
        } else {
            $lane = @($lanes | Where-Object { $_.Stages.Name -contains $st.Name })[0]
            if ($null -ne $lane -and $lane.Job.State -eq "Stopped") {
                $res = "CANCELLED"
            }
        }
        if ($res -ne "PASS" -and $res -ne "SKIPPED") {
            $failed = $true
        }
        $color = "Green"
        if ($res -like "FAIL*" -or $res -eq "UNKNOWN" -or $res -eq "CANCELLED") {
            $color = "Red"
        } elseif ($res -eq "SKIPPED") {
            $color = "Yellow"
        }
        Write-Host ("{0,-36} {1,-10} {2,8}" -f $st.Name, $res, $sec) -ForegroundColor $color
    }
    Write-Host ("{0,-36} {1,-10} {2,8}" -f "Wall", "", $wall.Elapsed.TotalSeconds.ToString("0.0", [cultureinfo]::InvariantCulture))
    foreach ($lane in $lanes) {
        Remove-Job $lane.Job -Force -ErrorAction SilentlyContinue
    }
    if ($failed) {
        Write-Host "[gates] FAILED - see the first failed stage above" -ForegroundColor Red
        exit 1
    }
    Write-Host "[gates] ALL GREEN" -ForegroundColor Green
    exit 0
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
