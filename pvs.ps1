param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [ValidateRange(0, 128)]
    [int]$Threads = 0,
    [switch]$Json,
    [switch]$Sarif
)

# PVS-Studio analysis wrapper for RadioNavigation.
#
# Analyzes the same RadioNavigation.Analysis.csproj that Roslynator uses
# (net48 + the REAL UnityEngine.* and game DLLs from UBOAT_Data\Managed) so
# PVS sees the actual Unity API surface, not stubs. build.ps1 invokes this as
# a post-compile step; it can also be run standalone.
#
# Why Analysis.csproj and not build.ps1's raw csc output: PVS-Studio_Cmd only
# works through MSBuild projects (.csproj/.sln); build.ps1 compiles with raw
# csc.exe. Analysis.csproj is the same Source/** compiled against the real
# game assemblies, so it is the correct semantic model for PVS too.
#
# IMPORTANT: always `dotnet restore` the analysis project first - multiple
# csproj sharing one obj/ folder corrupt each other's NuGet state
# (project.assets.json), which breaks type resolution (CS0012 flood).
#
# Exit code: returns 0 on clean analysis, non-zero when issues are found or
# the analyzer fails. build.ps1 treats non-zero as a blocking gate.

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$csproj = Join-Path $root "RadioNavigation.Analysis.csproj"

$pvsDir = "C:\Program Files (x86)\PVS-Studio"
$pvsCmd = Join-Path $pvsDir "PVS-Studio_Cmd.exe"
$plogConv = Join-Path $pvsDir "PlogConverter.exe"

if (-not (Test-Path $csproj)) {
    Write-Error "[RadioNavigation] Analysis project not found: $csproj"
    exit 1
}
if (-not (Test-Path $pvsCmd)) {
    Write-Error "[RadioNavigation] PVS-Studio_Cmd.exe not found at $pvsCmd. Install PVS-Studio."
    exit 1
}
if (-not (Test-Path $plogConv)) {
    Write-Error "[RadioNavigation] PlogConverter.exe not found at $plogConv."
    exit 1
}

# Always restore so the shared obj/project.assets.json matches THIS project.
& dotnet restore $csproj | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error "[RadioNavigation] dotnet restore failed for $csproj"
    exit $LASTEXITCODE
}

$plog = Join-Path $root "pvs-report.plog"
$txt = Join-Path $root "pvs-report.txt"

$threadLabel = if ($Threads -eq 0) { "auto" } else { $Threads }
Write-Host "[RadioNavigation] Running PVS-Studio on $csproj ..."
Write-Host "[RadioNavigation] Analysis threads: $threadLabel"
& $pvsCmd -t $csproj -o $plog -c $Configuration -j $Threads
$code = $LASTEXITCODE

# Decode the PVS-Studio_Cmd bit-mask exit code.
$filesFail    = (($code -band 1)   -ne 0)
$generalErr   = (($code -band 2)   -ne 0)
$badArgs      = (($code -band 4)   -ne 0)
$fileNotFound = (($code -band 8)   -ne 0)
$badCfg       = (($code -band 16)  -ne 0)
$badSolution  = (($code -band 32)  -ne 0)
$badExt       = (($code -band 64)  -ne 0)
$noLicense    = (($code -band 128) -ne 0)
$issuesFound  = (($code -band 256) -ne 0)
$suppressErr  = (($code -band 512) -ne 0)
$licenseSoon  = (($code -band 1024) -ne 0)

if ($noLicense) {
    Write-Error "[RadioNavigation] PVS-Studio license missing/expired (exit $code)."
    exit 1
}
if ($generalErr -or $badArgs -or $fileNotFound -or $badCfg -or $badSolution -or $badExt -or $suppressErr) {
    Write-Error "[RadioNavigation] PVS-Studio run failed (exit $code). See $plog"
    exit 1
}
if ($filesFail) {
    # A crash on some files means the analysis is INCOMPLETE.
    # We cannot prove there are no issues in files the analyzer never processed.
    # For a blocking quality gate that is a failure, not a warning.
    Write-Error "[RadioNavigation] PVS-Studio crashed analyzing some files; analysis incomplete (exit $code). See $plog"
    exit 1
}

if (-not (Test-Path $plog)) {
    Write-Error "[RadioNavigation] PVS-Studio did not produce report: $plog"
    exit 1
}

# Convert to human-readable txt (always) plus optional JSON/SARIF.
# PlogConverter's -o is an OUTPUT DIRECTORY; the file name comes from -n.
& $plogConv $plog -t Txt -o $root -n "pvs-report" | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error "[RadioNavigation] PlogConverter failed to generate TXT report."
    exit 1
}
if (-not (Test-Path $txt)) {
    Write-Error "[RadioNavigation] PlogConverter reported success but TXT report was not created: $txt"
    exit 1
}
if ($Json) {
    & $plogConv $plog -t JSON -o $root -n "pvs-report" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "[RadioNavigation] PlogConverter failed to generate JSON report."
        exit 1
    }
}
if ($Sarif) {
    & $plogConv $plog -t Sarif -o $root -n "pvs-report" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "[RadioNavigation] PlogConverter failed to generate SARIF report."
        exit 1
    }
}

if ($licenseSoon) {
    Write-Warning "[RadioNavigation] PVS-Studio license expires within a month."
}
if ($issuesFound) {
    Write-Host "[RadioNavigation] PVS-Studio found issues -> $txt"
    Write-Host "[RadioNavigation] PVS-Studio exit code: $code (bit 256 = issues)"
    exit 1
}
Write-Host "[RadioNavigation] PVS-Studio clean -> $txt"
exit 0
