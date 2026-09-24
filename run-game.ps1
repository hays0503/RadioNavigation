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
#   4. polls the McpBridge HTTP bridge until /state reports a full snapshot
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File run-game.ps1
#   powershell -ExecutionPolicy Bypass -File run-game.ps1 -SkipBuild
#   powershell -ExecutionPolicy Bypass -File run-game.ps1 -NoRestart
param(
    [switch]$SkipBuild,
    [switch]$NoRestart,
    [int]$TimeoutMinutes = 12,
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\UBOAT'
)

$ErrorActionPreference = 'Stop'
$modDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$modsRoot = Split-Path -Parent $modDir

function Ok($msg) { Write-Host "[OK] $msg" -ForegroundColor Green }
function Warn($msg) { Write-Host "[WARN] $msg" -ForegroundColor Yellow }
function Fail($msg) { Write-Host "[FAIL] $msg" -ForegroundColor Red; exit 1 }

# --- 1. build (static checks only; the game compiles the mod itself) ---------
if (-not $SkipBuild) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $modDir 'build.ps1')
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

# --- 3. restart the game directly (bypasses the launcher dialog) ------------
$gameExe = Join-Path $GamePath 'UBOAT.exe'
$launcherExe = Join-Path $GamePath 'UBOAT Launcher.exe'
if (-not (Test-Path -LiteralPath $gameExe)) { Fail "UBOAT.exe not found at $gameExe" }

Get-CimInstance Win32_Process -Filter "Name LIKE '%UBOAT%'" -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 5

if (-not $NoRestart) {
    $env:SteamAppId = '494840'
    $env:SteamGameId = '494840'
    $env:SteamClientLaunch = '1'
    $env:SteamPath = 'C:\Program Files (x86)\Steam'
    $gameArgs = @($launcherExe, '-quicklatestgamestate') | ForEach-Object { "`"$_`"" }
    Start-Process -FilePath $gameExe -ArgumentList $gameArgs -WorkingDirectory $GamePath
    Ok 'game started (direct UBOAT.exe -quicklatestgamestate)'
} else {
    Ok 'game not restarted (-NoRestart); boot it manually to pick up the touched sources'
    exit 0
}

# --- 4. wait for the bridge (proves McpBridge initialized) ------------------
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
while ((Get-Date) -lt $deadline) {
    try {
        $s = (Invoke-WebRequest -Uri 'http://127.0.0.1:28777/state' -TimeoutSec 8 -UseBasicParsing).Content
        if ($s -like '*"position":{"ok":true*') {
            Ok 'bridge up, game at sea'
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
Fail "bridge did not report a full snapshot within $TimeoutMinutes min"
