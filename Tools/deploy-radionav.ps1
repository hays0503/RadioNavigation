# RadioNavigation deploy helper (rewritten 2026-09-22).
#
# The old body copied radionavigation.dll into persistentDataPath\Temp\ and
# relied on the ProbeTest shim loading it directly. That is now harmful: with a
# Temp DLL newer than Source/*.cs the game takes the prebuilt path
# (IsAssemblyOutdated == false -> ScriptDomain.LoadAssembly(bytes)), which
# returns null, so the mod silently never initializes. Deployment is now just
# "touch the sources and let the game compile the mod itself".
#
# Thin wrapper around run-game.ps1 (see its header for the full rationale).
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File Tools/deploy-radionav.ps1
#   powershell -ExecutionPolicy Bypass -File Tools/deploy-radionav.ps1 -SkipBuild -NoRestart
param(
    [switch]$SkipBuild,
    [switch]$NoRestart,
    [int]$TimeoutMinutes = 12
)

$ErrorActionPreference = 'Stop'
$runGame = Join-Path $PSScriptRoot 'run-game.ps1'
if (-not (Test-Path -LiteralPath $runGame)) { Write-Host "[FAIL] missing $runGame" -ForegroundColor Red; exit 1 }

$argv = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $runGame, '-TimeoutMinutes', $TimeoutMinutes)
if ($SkipBuild) { $argv += '-SkipBuild' }
if ($NoRestart) { $argv += '-NoRestart' }
& powershell @argv
exit $LASTEXITCODE
