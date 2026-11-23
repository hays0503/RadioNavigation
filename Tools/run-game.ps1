# RadioNavigation game-launch helper (recipe verified live 2026-09-22).
#
# Why this exists:
#   The game only initializes a mod when IT compiles the mod, i.e. when some
#   Source/*.cs is newer than Temp/<assembly>.dll. With a fresh Temp DLL
#   (IsAssemblyOutdated == false) the game calls
#   ScriptDomain.LoadAssembly(bytes), which returns null -> Mod.assembly == null
#   -> InitializeAssembly() silently returns: no [RadioNav] lines, no error
#   (log muted), the mod looks unloaded while mod_state.modLoaded still says
#   true (it counts LoadedMods).
#
#   The launcher additionally gates script mods behind an Accept/Cancel security
#   dialog; a pending or cancelled dialog starts the game with NO mods at all.
#
# What this script does (no DLL copying - that is what broke the load):
#   1. build.ps1 (csc + PVS gate) unless -SkipBuild
#   2. touches Source/*.cs of every mod that must load, so IsAssemblyOutdated
#      returns true and the game recompiles them itself
#   3. starts UBOAT.exe directly with -quicklatestgamestate (bypasses the
#      launcher; that path calls IntroScene_completed ->
#      ModManager.InitializeAssemblies() explicitly)
#   4. polls the McpBridge HTTP bridge until /state reports loading.ready=true
#      (the full load gate, not merely a cached player position)
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File Tools/run-game.ps1
#   powershell -ExecutionPolicy Bypass -File Tools/run-game.ps1 -SkipBuild
#   powershell -ExecutionPolicy Bypass -File Tools/run-game.ps1 -NoRestart
# Experimental drift (launch variables; defaults unchanged):
#   powershell -ExecutionPolicy Bypass -File Tools/run-game.ps1 -DriftDemo
#   powershell -ExecutionPolicy Bypass -File Tools/run-game.ps1 -CurrentField '1,0' -DirectWind -NearbyDrift -RelativeRudder
# Route steering follows a constant field; a directory of RNW1 tiles replaces it:
#   powershell -ExecutionPolicy Bypass -File Tools/run-game.ps1 -CurrentField '1,0' -RouteCurrent
#   powershell -ExecutionPolicy Bypass -File Tools/run-game.ps1 -AtlasTile 'X:\tiles' -CurrentProfile '0:1,0;20:0,0'
#   powershell -ExecutionPolicy Bypass -File Tools/run-game.ps1 -CurrentField '1,0' -FarDrift
param(
    [switch]$SkipBuild,
    [switch]$NoRestart,
    [int]$TimeoutMinutes = 12,
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\UBOAT',
    [string]$CurrentField,
    [switch]$DirectWind,
    [switch]$NearbyDrift,
    [switch]$RelativeRudder,
    [switch]$RouteCurrent,
    [switch]$FarDrift,
    [string]$CurrentProfile,
    [string]$AtlasTile,
    [switch]$DriftDemo
)

$ErrorActionPreference = 'Stop'
$modDir = Split-Path -Parent $PSScriptRoot
$modsRoot = Split-Path -Parent $modDir

function Ok($msg) { Write-Host "[OK] $msg" -ForegroundColor Green }
function Warn($msg) { Write-Host "[WARN] $msg" -ForegroundColor Yellow }
function Fail($msg) { Write-Host "[FAIL] $msg" -ForegroundColor Red; exit 1 }

if ($PSBoundParameters.ContainsKey('CurrentField')) {
    $parts = $CurrentField -split ','
    if ($parts.Count -ne 2) { Fail "CurrentField must be 'east,north' in m/s" }
    foreach ($part in $parts) {
        [float]$number = 0
        if (-not [float]::TryParse($part, [Globalization.NumberStyles]::Float,
                [Globalization.CultureInfo]::InvariantCulture, [ref]$number) -or
                [float]::IsNaN($number) -or [float]::IsInfinity($number)) {
            Fail "CurrentField must contain two finite speeds in m/s"
        }
    }
}

# --- 1. build (static checks only; the game compiles the mod itself) ---------
if (-not $SkipBuild) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) { Fail 'build.ps1 (compile or PVS gate)' }
    Ok 'build + PVS gate'
}

# --- 2. make every needed mod's source newer than its Temp DLL --------------
$touched = 0
$targets = @(
    (Join-Path $modDir 'Source\Logic\Main.cs'),
    (Join-Path $modsRoot 'McpBridge\Source\Logic\McpAudio.cs')
)
foreach ($t in $targets) {
    if (Test-Path -LiteralPath $t) {
        (Get-Item -LiteralPath $t).LastWriteTime = Get-Date
        $touched++
        Ok "touched $t"
    } else {
        Warn "missing (skipped): $t"
    }
}
if ($touched -eq 0) { Fail 'no mod source touched - nothing would recompile' }

# The game has no enabled flag in modlist.txt. If the launcher left an extra
# or missing mod, a later "bridge is up" result is ambiguous and can recreate
# the double-load class of failure. Require the exact harness set.
$modListPath = Join-Path (Split-Path -Parent $modsRoot) 'modlist.txt'
if (-not (Test-Path -LiteralPath $modListPath)) { Fail "modlist.txt not found: $modListPath" }
$loadedMods = @(Get-Content -LiteralPath $modListPath |
    ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' } | Sort-Object)
$expectedMods = @('McpBridge', 'RadioNavigation') | Sort-Object
$modDiff = @(Compare-Object -ReferenceObject $expectedMods -DifferenceObject $loadedMods)
if ($modDiff.Count -ne 0) {
    Fail "modlist.txt must contain exactly McpBridge + RadioNavigation (found: $($loadedMods -join ', '))"
}
Ok 'modlist verified (McpBridge + RadioNavigation)'

# --- 3. restart the game directly (bypasses the launcher dialog) ------------
$gameExe = Join-Path $GamePath 'UBOAT.exe'
$launcherExe = Join-Path $GamePath 'UBOAT Launcher.exe'
if (-not (Test-Path -LiteralPath $gameExe)) { Fail "UBOAT.exe not found at $gameExe" }

Get-CimInstance Win32_Process -Filter "Name LIKE '%UBOAT%'" -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
if ($PSBoundParameters.ContainsKey('AtlasTile') -and
    $AtlasTile -ne 'bundled' -and
    -not (Test-Path -LiteralPath $AtlasTile)) {
    Fail "AtlasTile path does not exist: $AtlasTile (use 'bundled' for the shipped slice)"
}

Start-Sleep -Seconds 5

if (-not $NoRestart) {
    $env:SteamAppId = '494840'
    $env:SteamGameId = '494840'
    $env:SteamClientLaunch = '1'
    $env:SteamPath = 'C:\Program Files (x86)\Steam'
    if ($DriftDemo) {
        $env:RADIONAV_CURRENT_FIELD = '1,0'
        $env:RADIONAV_DIRECT_WIND = '1'
        $env:RADIONAV_NEARBY_DRIFT = '1'
        $env:RADIONAV_RELATIVE_RUDDER = '1'
        $env:RADIONAV_ROUTE_CURRENT = '1'
        $env:RADIONAV_FAR_DRIFT = '1'
    }
    if ($PSBoundParameters.ContainsKey('CurrentField')) { $env:RADIONAV_CURRENT_FIELD = $CurrentField }
    if ($PSBoundParameters.ContainsKey('CurrentProfile')) { $env:RADIONAV_CURRENT_PROFILE = $CurrentProfile }
    if ($PSBoundParameters.ContainsKey('AtlasTile')) { $env:RADIONAV_ATLAS_TILE = $AtlasTile }
    if ($DirectWind) { $env:RADIONAV_DIRECT_WIND = '1' }
    if ($NearbyDrift) { $env:RADIONAV_NEARBY_DRIFT = '1' }
    if ($RelativeRudder) { $env:RADIONAV_RELATIVE_RUDDER = '1' }
    if ($RouteCurrent) { $env:RADIONAV_ROUTE_CURRENT = '1' }
    if ($FarDrift) { $env:RADIONAV_FAR_DRIFT = '1' }
    $gameArgs = @($launcherExe, '-quicklatestgamestate') | ForEach-Object { "`"$_`"" }
    Start-Process -FilePath $gameExe -ArgumentList $gameArgs -WorkingDirectory $GamePath
    Ok 'game started (direct UBOAT.exe -quicklatestgamestate)'
} else {
    Ok 'game not restarted (-NoRestart); boot it manually to pick up the touched sources'
    exit 0
}

# --- 4. wait for bridge + world load gate -----------------------------------
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
while ((Get-Date) -lt $deadline) {
    try {
        $s = (Invoke-WebRequest -Uri 'http://127.0.0.1:28777/state' -TimeoutSec 8 -UseBasicParsing).Content
        if ($s -like '*"loading":{"ready":true*') {
            Ok 'bridge up, loading gate ready'
            Write-Host $s.Substring(0, [Math]::Min(400, $s.Length))
            $log = Join-Path $env:USERPROFILE 'AppData\LocalLow\Deep Water Studio\UBOAT\output_log.txt'
            if (Test-Path -LiteralPath $log) {
                $n = (Select-String -Path $log -Pattern '\[RadioNav\]' -ErrorAction SilentlyContinue |
                    Measure-Object).Count
                if ($n -gt 0) { Ok "RadioNavigation initialized ($n log lines)" }
                else { Warn 'no [RadioNav] lines yet - check the log in a few seconds' }
            }
            exit 0
        }
    } catch { }
    Start-Sleep -Seconds 10
}
Fail "bridge did not report loading.ready=true within $TimeoutMinutes min"
