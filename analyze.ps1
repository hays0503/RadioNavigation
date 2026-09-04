param(
    [ValidateSet("info", "warning", "error")]
    [string]$Severity = "warning",
    [ValidateSet("quick", "full")]
    [string]$Profile = "full",
    [switch]$Fix,
    [switch]$Json,
    [switch]$SkipSourceCheck,
    [switch]$IdeAudit
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
#   powershell -File analyze.ps1 -SkipSourceCheck    # skip neural-slop gate
#   powershell -File analyze.ps1 -IdeAudit             # IDE0051/IDE0052 dead-member audit (own phase, see below)

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

# Neural-slop gate: compile the mod against the clean (non-obfuscated) game source
# reference BEFORE static analysis. A hallucinated/wrong-signature game API call
# fails here instead of passing silently against the obfuscated DLL. The runner
# exits 0 only when all 3 SourceCheck tests pass. Skip with -SkipSourceCheck.
if (-not $SkipSourceCheck) {
    $scproj = Join-Path $root "RadioNavigation.SourceCheck\RadioNavigation.SourceCheck.csproj"
    if (Test-Path $scproj) {
        Write-Host "[RadioNavigation] SourceCheck (neural-slop guard) ..."
        & dotnet run --project $scproj -c Release
        if ($LASTEXITCODE -ne 0) {
            Write-Error "[RadioNavigation] SourceCheck FAILED - mod references a game API that does not exist / has wrong signature (neural slop). Fix before analyzing."
            exit $LASTEXITCODE
        }
        Write-Host "[RadioNavigation] SourceCheck PASS"
    }
    else {
        Write-Warning "[RadioNavigation] SourceCheck project not found ($scproj) - gate skipped."
    }
}

# Always restore so the shared obj/project.assets.json matches THIS project.
& dotnet restore $csproj | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error "[RadioNavigation] dotnet restore failed for $csproj"
    exit $LASTEXITCODE
}

# IDE0051 (unused private member) + IDE0052 (unread private member) audit
# (risk register iter6). Roslynator CLI never surfaces IDE rules, so this
# phase builds the analysis project with -p:EnforceCodeStyleInBuild=true
# instead. Enforcement is CLI-only: the repo carries just the inert severity
# lines in .editorconfig, so quick/full runs, tests.ps1 and build.ps1 are
# unaffected. NoWarn drops CS0414 for one run to compare the compiler's own
# write-only-field detector against IDE0052 (decision logged in AGENTS.md).
# Triage: Unity engine-called methods (Awake/Update/...) are noise — the
# engine invokes them, Roslyn cannot see it. Everything else blocks (exit 1).
if ($IdeAudit) {
    $unityNoise = @('Awake','Start','Update','LateUpdate','FixedUpdate',
        'OnDestroy','OnEnable','OnDisable','OnValidate','OnDrawGizmos',
        'OnPreRender','OnPostRender','OnAudioFilterRead','OnTriggerEnter',
        'OnTriggerExit','OnCollisionEnter','OnPointerClick','OnPointerEnter',
        'OnPointerExit','OnDrag','OnBeginDrag','OnEndDrag','OnScroll')
    # NOTE: semicolons in -p: values MUST be %3B-escaped — MSBuild splits a
    # bare -p:NoWarn="a;b" into junk properties (MSB1006) after PowerShell
    # strips the quotes, and the audit would report a vacuous clean.
    $buildLines = & dotnet build $csproj -c Release -p:EnforceCodeStyleInBuild=true -p:NoWarn="CS8032%3BCS0162%3BCS0219%3BCS0649" 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "[RadioNavigation] IdeAudit build failed (exit $LASTEXITCODE) - refusing to report a clean audit on a broken compile"
        $buildLines | ForEach-Object { Write-Host $_ }
        exit $LASTEXITCODE
    }
    $buildOut = $buildLines | Out-String
    # Build warnings repeat (project + target passes) — dedupe so counts
    # are stable run over run.
    $hits = @($buildOut -split "`r?`n" | Select-String -Pattern 'warning (IDE0051|IDE0052|CS0414)' | ForEach-Object { $_.Line.Trim() } | Sort-Object -Unique)
    $genuine = @()
    $noise = @()
    foreach ($h in $hits) {
        # Roslyn quotes the symbol in DOUBLE quotes ("Type.Member").
        $m = [regex]::Match($h, '"([^"]+)"')
        $memberName = ""
        if ($m.Success) {
            $dotted = $m.Groups[1].Value
            $dot = $dotted.LastIndexOf('.')
            if ($dot -ge 0) { $memberName = $dotted.Substring($dot + 1) }
        }
        if ($memberName -ne "" -and $unityNoise -contains $memberName) { $noise += $h }
        else { $genuine += $h }
    }
    Write-Host "[RadioNavigation] IdeAudit: $($hits.Count) hit(s) ($($genuine.Count) genuine, $($noise.Count) Unity-noise)"
    if ($noise.Count -gt 0) {
        Write-Host "--- Unity-noise (engine-called, ignored) ---"
        $noise | ForEach-Object { Write-Host $_ }
    }
    if ($genuine.Count -gt 0) {
        Write-Host "--- GENUINE (fix or justify) ---"
        $genuine | ForEach-Object { Write-Host $_ }
        Write-Host "[RadioNavigation] IdeAudit FAILED - genuine dead members found"
        exit 1
    }
    Write-Host "[RadioNavigation] IdeAudit clean"
    exit 0
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
