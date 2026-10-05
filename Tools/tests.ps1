param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "== RadioNavigation tests (F22 baseline) ==" -ForegroundColor Cyan

# Custom runner (xunit core attributes, no vstest testhost) - works in restricted tokens
# where testhost.x86 crashes on OpenProcess (Win32Exception 5).
# Deterministic JIT: background tiered recompilation can flip 1-ulp FP
# comparisons on envelope thresholds (Render_Cycle60 flake, 2 in ~12 runs).
# Pinned off for test runs only; production (game process) unaffected.
$env:DOTNET_TieredCompilation = "0"
& dotnet run --project "$root\Dev\Tests\Runner\Runner.csproj" --configuration $Configuration --nologo

if ($LASTEXITCODE -ne 0) {
    Write-Host "TESTS FAILED (exit $LASTEXITCODE)" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "TESTS PASSED" -ForegroundColor Green
exit 0
