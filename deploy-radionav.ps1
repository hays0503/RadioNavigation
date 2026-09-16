# RadioNavigation deploy helper.
#
# The mod is NOT in modlist.txt: the ProbeTest shim loads
# persistentDataPath/Temp/radionavigation.dll directly, so the game never
# recompiles the mod from Source. This script closes that gap:
#   build.ps1 (csc + PVS gate) -> copy radionavigation.dll into Temp ->
#   optionally restart the game to sea (sea-run.ps1).
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File deploy-radionav.ps1
#   powershell -ExecutionPolicy Bypass -File deploy-radionav.ps1 -SkipBuild -NoRestart
param(
    [switch]$SkipBuild,
    [switch]$NoRestart,
    [int]$TimeoutMinutes = 12
)

$ErrorActionPreference = 'Stop'
$modDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$tempDir = Join-Path $env:USERPROFILE 'AppData\LocalLow\Deep Water Studio\UBOAT\Temp'
$src = Join-Path $modDir 'radionavigation.dll'
$dst = Join-Path $tempDir 'radionavigation.dll'

function Ok($msg) { Write-Host "[OK] $msg" -ForegroundColor Green }
function Fail($msg) { Write-Host "[FAIL] $msg" -ForegroundColor Red; exit 1 }

if (-not $SkipBuild) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $modDir 'build.ps1')
    if ($LASTEXITCODE -ne 0) { Fail 'build.ps1 (compile or PVS gate)' }
    Ok 'build + PVS gate'
}
if (-not (Test-Path -LiteralPath $src)) { Fail "missing $src (run without -SkipBuild)" }
if (-not (Test-Path -LiteralPath $tempDir)) {
    New-Item -ItemType Directory -Path $tempDir | Out-Null
}
Copy-Item -LiteralPath $src -Destination $dst -Force
$hash = (Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash
Ok "deployed -> $dst (sha256 $($hash.Substring(0, 12)))"

if (-not $NoRestart) {
    $seaRun = Join-Path $modDir '..\McpBridge\sea-run.ps1'
    if (-not (Test-Path -LiteralPath $seaRun)) { Fail "sea-run.ps1 not found at $seaRun" }
    Write-Host 'Restarting game to sea (sea-run.ps1)...'
    & powershell -NoProfile -ExecutionPolicy Bypass -File $seaRun -TimeoutMinutes $TimeoutMinutes
    if ($LASTEXITCODE -ne 0) { Fail 'sea-run.ps1 (game did not reach sea)' }
    Ok 'game at sea'
}
