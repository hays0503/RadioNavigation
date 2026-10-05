# Coverage for the custom xunit runner (vstest is unusable: restricted
# tokens crash testhost, so coverlet.collector is out).
# Uses Microsoft.CodeCoverage in process mode — no vstest involved:
#   1. dotnet tool install -g dotnet-coverage   (once)
#   2. powershell -ExecutionPolicy Bypass -File Tools/coverage.ps1
# Output: ./artifacts/reports/coverage/coverage.cobertura.xml (only Source files linked into the
# Tests project are reachable; MonoBehaviour/scene code is live-gate
# territory by design — see docs/SAD/11_Testing_Strategy.md 8.3).
# Verified 2026-09-26: exit 0, 1135/1135 tests, Source-only 9717/10048 (96.7%).
param(
    [string]$Configuration = "Release",
    [string]$Output = "artifacts\reports\coverage\coverage.cobertura.xml",
    [double]$MinLineRate = 0.965
)

$ErrorActionPreference = "Stop"

$coverage = Get-Command dotnet-coverage -ErrorAction SilentlyContinue
if ($null -eq $coverage) {
    Write-Error "dotnet-coverage not found. Install once: dotnet tool install -g dotnet-coverage"
    exit 1
}

$root = Split-Path -Parent $PSScriptRoot
$runnerCsproj = Join-Path $root "Dev/Tests/Runner/Runner.csproj"
dotnet build $runnerCsproj -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$runnerDll = Join-Path $root "Dev/Tests/Runner/bin/$Configuration/net10.0/RadioNavigation.TestRunner.dll"
if (-not (Test-Path -LiteralPath $runnerDll)) {
    Write-Error "Runner dll not found at $runnerDll"
    exit 1
}

$outputPath = if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $root $Output }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outputPath) | Out-Null
# Via the dotnet host: collecting against the raw exe leaves <packages>
# empty (profiler does not attach, verified 2026-09-04).
& dotnet-coverage collect -f cobertura -o $outputPath -- dotnet $runnerDll
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Output "Coverage written to $outputPath"

# Coverage floor gate: Source-only line-rate must stay at or above -MinLineRate
# (default 0.965). History: 97.0% on 2026-09-04 (465 tests) eroded to 96.71%
# measured 2026-09-26 (1135 tests, 9717/10048) while nobody watched - the top
# uncovered files are game-coupled by design (StoryDecisionGateway,
# StationManager, SceneContentBuilder: Unity/game assemblies unloadable in
# xunit, live-gate territory per SAD/11 8.3). The floor sits ~20 lines below
# the measurement as a buffer against profiler hit-noise; climbing back to
# 97.0 needs ~30 covered lines from testable code and is backlog, not this
# gate. The report also covers Tests/ files, so scope the metric to classes
# under a Source/ directory; a file listed in two packages counts twice, but
# the ratio is stable run over run for the same collect command. New uncovered
# lines must add tests - or the floor moves deliberately via -MinLineRate in
# the same commit. Silent erosion is how 97% becomes 80%.
[xml]$report = Get-Content -LiteralPath $outputPath -Raw
$covered = 0
$total = 0
foreach ($class in $report.coverage.packages.package.classes.class) {
    if ([string]$class.filename -notmatch '(^|[\\/])Source[\\/]') {
        continue
    }
    foreach ($line in $class.lines.line) {
        $total++
        if ([int]$line.hits -gt 0) {
            $covered++
        }
    }
}
if ($total -eq 0) {
    Write-Error "[RadioNavigation] Coverage gate: no Source lines in $outputPath - refusing to report a vacuous pass."
    exit 1
}
$rate = $covered / $total
Write-Host ("[RadioNavigation] Source-only line coverage: {0}/{1} ({2:0.0}%), floor {3:0.0}%" -f $covered, $total, ($rate * 100), ($MinLineRate * 100))
if ($rate -lt $MinLineRate) {
    Write-Error ("[RadioNavigation] Coverage gate FAILED: {0:0.00}% below floor {1:0.00}%. Add tests for the new lines or lower -MinLineRate deliberately in the same commit." -f ($rate * 100), ($MinLineRate * 100))
    exit 1
}
Write-Host "[RadioNavigation] Coverage gate passed."
exit 0
