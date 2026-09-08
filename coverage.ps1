# Coverage for the custom xunit runner (vstest is unusable: restricted
# tokens crash testhost, so coverlet.collector is out).
# Uses Microsoft.CodeCoverage in process mode — no vstest involved:
#   1. dotnet tool install -g dotnet-coverage   (once)
#   2. powershell -ExecutionPolicy Bypass -File coverage.ps1
# Output: ./coverage.cobertura.xml (only Source files linked into the
# Tests project are reachable; MonoBehaviour/scene code is live-gate
# territory by design — see SAD/11_Testing_Strategy.md 8.3).
# Verified 2026-09-04: exit 0, 465/465 tests, Source-only 7213/7435 (97.0%).
param(
    [string]$Configuration = "Release",
    [string]$Output = "coverage.cobertura.xml"
)

$ErrorActionPreference = "Stop"

$coverage = Get-Command dotnet-coverage -ErrorAction SilentlyContinue
if ($null -eq $coverage) {
    Write-Error "dotnet-coverage not found. Install once: dotnet tool install -g dotnet-coverage"
    exit 1
}

$runnerCsproj = Join-Path $PSScriptRoot "Tests/Runner/Runner.csproj"
dotnet build $runnerCsproj -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$runnerDll = Join-Path $PSScriptRoot "Tests/Runner/bin/$Configuration/net10.0/RadioNavigation.TestRunner.dll"
if (-not (Test-Path -LiteralPath $runnerDll)) {
    Write-Error "Runner dll not found at $runnerDll"
    exit 1
}

$outputPath = Join-Path $PSScriptRoot $Output
# Via the dotnet host: collecting against the raw exe leaves <packages>
# empty (profiler does not attach, verified 2026-09-04).
& dotnet-coverage collect -f cobertura -o $outputPath -- dotnet $runnerDll
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Output "Coverage written to $outputPath"
