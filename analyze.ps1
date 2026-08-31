param(
    [ValidateSet("info", "warning", "error")]
    [string]$Severity = "warning",
    [ValidateSet("quick", "full")]
    [string]$Profile = "full",
    [switch]$Fix,
    [switch]$Json
)

# Roslynator CLI analysis wrapper for RadioNavigation.
#
# Uses the analysis-only RadioNavigation.Analysis.csproj (net48 + the REAL
# UnityEngine.* and game DLLs from UBOAT_Data\Managed). This is the correct
# semantic model - Roslyn resolves the actual Unity API surface UBOAT is built
# against. No stubs, no OpenMod.Redist.
#
# Why net48: netstandard2.0 conflicts with Unity's mscorlib-2.0-based assemblies
# (CS0012 flood -> analyzers never run). .NET Framework TFMs unify mscorlib so
# the real Unity DLLs resolve cleanly: net40=16 CS errs, net45=4, net48=0.
#
# IMPORTANT: multiple csproj files sharing one obj/ folder corrupt each other's
# NuGet restore state (project.assets.json). A stale assets file makes Roslynator
# fail to resolve Unity types -> CS0012 flood -> analyzers never run (0 RCS).
# We therefore always `dotnet restore` the analysis project first.
#
# Profiles:
#   full  - every Roslynator rule enabled in .editorconfig (default)
#   quick - only high-signal / bug-catching rules (dead code, NRE/null safety,
#           IDisposable, allocations, comparisons, boxing, large members,
#           Unity lifecycle mistakes, C# 7.0 compatibility)
#
# Usage:
#   powershell -File analyze.ps1                     # full profile, warnings
#   powershell -File analyze.ps1 -Profile quick      # dangerous rules only
#   powershell -File analyze.ps1 -Severity info      # include info-level rules
#   powershell -File analyze.ps1 -Fix                # apply auto-fixes
#   powershell -File analyze.ps1 -Json               # SARIF output to file

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$csproj = Join-Path $root "RadioNavigation.Analysis.csproj"

if (-not (Test-Path $csproj)) {
    Write-Error "[RadioNavigation] Analysis project not found: $csproj"
    exit 1
}

$roslynator = Get-Command roslynator -ErrorAction SilentlyContinue
if (-not $roslynator) {
    Write-Error "[RadioNavigation] roslynator not on PATH. Install: dotnet tool install -g roslynator.dotnet.cli"
    exit 1
}

# Always restore so the shared obj/project.assets.json matches THIS project.
& dotnet restore $csproj | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error "[RadioNavigation] dotnet restore failed for $csproj"
    exit $LASTEXITCODE
}

# High-signal rules for the 'quick' profile - only rules that catch real bugs,
# dead code, null/NRE hazards, perf/allocations, or serialization instability.
# Curated against the actual 3084-diagnostic breakdown of THIS codebase
# (2026-08-31). Cosmetic/style rules (var, braces, parens, doc comments, naming)
# are deliberately excluded - they belong to 'full' periodic audits only.
#
#   dead code:          RCS1213 (unused member), RCS1163 (unused param, e.g. __instance)
#   null/NRE safety:    RCS1146 (conditional access), RCS1128 (coalesce),
#                       RCS1173 (coalesce vs if), RCS1221 (pattern vs as+nullcheck)
#   correctness:        RCS1169 (make readonly), RCS1129 (redundant init),
#                       RCS1238 (nested ?:), RCS1097 (redundant ToString),
#                       RCS1258 (redundant enum flag)
#   perf/allocation:    RCS1198 (boxing), RCS1188 (redundant async/await)
#   serialization:      RCS1161 (enum explicit values - save-data stability)
$quickRules = @(
    "RCS1213","RCS1163","RCS1146","RCS1128","RCS1173","RCS1221",
    "RCS1169","RCS1129","RCS1238","RCS1097","RCS1258",
    "RCS1198","RCS1188","RCS1161"
)

# Roslynator's --supported-diagnostics takes a SPACE-separated list (one arg per
# rule). Comma-joining in a single arg silently matches nothing ("No analyzers found").
function Add-SupportedDiagnostics {
    param([string[]]$RuleList, [System.Collections.ArrayList]$Target)
    if ($Profile -eq "quick") {
        [void]$Target.Add("--supported-diagnostics")
        foreach ($r in $RuleList) { [void]$Target.Add($r) }
    }
}

if ($Fix) {
    $fixArgs = New-Object System.Collections.ArrayList
    Add-SupportedDiagnostics $quickRules $fixArgs
    & roslynator fix $csproj --severity-level $Severity @fixArgs
    exit $LASTEXITCODE
}

$args = New-Object System.Collections.ArrayList
Add-SupportedDiagnostics $quickRules $args

if ($Json) {
    $out = Join-Path $root "roslynator-report.sarif"
    $args += "--output", $out, "--output-format", "sarif"
    & roslynator analyze $csproj --severity-level $Severity @args
    Write-Host "[RadioNavigation] SARIF report -> $out"
    exit $LASTEXITCODE
}

& roslynator analyze $csproj --severity-level $Severity @args
exit $LASTEXITCODE
