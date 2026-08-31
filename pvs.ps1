param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$Json,
    [switch]$Sarif,
    [switch]$Text
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

Write-Host "[RadioNavigation] Running PVS-Studio on $csproj ..."
& $pvsCmd -t $csproj -o $plog -c $Configuration
$code = $LASTEXITCODE

# Decode the PVS-Studio_Cmd bit-mask exit code.
$filesFail   = ($code -band 1)   / 1
$generalErr  = ($code -band 2)   / 2
$badArgs     = ($code -band 4)   / 4
$fileNotFound= ($code -band 8)   / 8
$badCfg      = ($code -band 16)  / 16
$badSolution = ($code -band 32)  / 32
$badExt      = ($code -band 64)  / 64
$noLicense   = ($code -band 128) / 128
$issuesFound = ($code -band 256) / 256
$suppressErr = ($code -band 512) / 512
$licenseSoon = ($code -band 1024)/ 1024

if ($noLicense -eq 1) {
    Write-Error "[RadioNavigation] PVS-Studio license missing/expired (exit $code)."
    exit 1
}
if ($generalErr -eq 1 -or $badArgs -eq 1 -or $fileNotFound -eq 1 -or $badCfg -eq 1 -or $badSolution -eq 1 -or $badExt -eq 1) {
    Write-Error "[RadioNavigation] PVS-Studio run failed (exit $code). See $plog"
    exit 1
}
if ($filesFail -eq 1) {
    Write-Warning "[RadioNavigation] PVS-Studio crashed analyzing some files (exit $code)."
}

# Convert to human-readable txt (always) plus optional JSON/SARIF.
# PlogConverter's -o is an OUTPUT DIRECTORY; the file name comes from -n.
& $plogConv $plog -t Txt -o $root -n "pvs-report" | Out-Null
if ($Json) {
    & $plogConv $plog -t JSON -o $root -n "pvs-report" | Out-Null
}
if ($Sarif) {
    & $plogConv $plog -t Sarif -o $root -n "pvs-report" | Out-Null
}

if ($issuesFound -eq 1) {
    Write-Host "[RadioNavigation] PVS-Studio found issues -> $txt"
    Write-Host "[RadioNavigation] PVS-Studio exit code: $code (bit 256 = issues)"
    exit 1
}

if ($licenseSoon -eq 1) {
    Write-Warning "[RadioNavigation] PVS-Studio license expires within a month."
}
Write-Host "[RadioNavigation] PVS-Studio clean -> $txt"
exit 0
