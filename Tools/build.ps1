param(
    [string]$GamePath = "C:\Program Files (x86)\Steam\steamapps\common\UBOAT",
    [string]$Configuration = "Release",
    [string]$OutputDir = (Join-Path (Split-Path -Parent $PSScriptRoot) "artifacts\build\$Configuration"),
    [switch]$SkipPvs
)

# F182: fail with a clear message before csc spews 15+ missing-reference
# errors when the game path is wrong.
if (-not (Test-Path $GamePath)) {
    Write-Error "[RadioNavigation] GamePath does not exist: $GamePath"
    exit 1
}

$coreRef = Join-Path $GamePath "UBOAT_Data\Managed\Assembly-CSharp.dll"
if (-not (Test-Path $coreRef)) {
    Write-Error "[RadioNavigation] Missing game managed DLLs at: $coreRef"
    exit 1
}

$modRoot = Split-Path -Parent $PSScriptRoot
$srcDir = Join-Path $modRoot "Source"
$refs = @(
    "$GamePath\UBOAT_Data\Managed\Assembly-CSharp.dll",
    "$GamePath\UBOAT_Data\Managed\UnityEngine.dll",
    "$GamePath\UBOAT_Data\Managed\UnityEngine.CoreModule.dll",
    "$GamePath\UBOAT_Data\Managed\UnityEngine.UIModule.dll",
    "$GamePath\UBOAT_Data\Managed\UnityEngine.UI.dll",
    "$GamePath\UBOAT_Data\Managed\UnityEngine.IMGUIModule.dll",
    "$GamePath\UBOAT_Data\Managed\UnityEngine.InputLegacyModule.dll",
    "$GamePath\UBOAT_Data\Managed\UnityEngine.PhysicsModule.dll",
    "$GamePath\UBOAT_Data\Managed\UnityEngine.TextRenderingModule.dll",
    "$GamePath\UBOAT_Data\Managed\UnityEngine.AudioModule.dll",
    "$GamePath\UBOAT_Data\Managed\0Harmony.dll",
    "$GamePath\UBOAT_Data\Managed\com.uboat.dependencies.dll",
    "$GamePath\UBOAT_Data\Managed\com.uboat.game.dll",
    "$GamePath\UBOAT_Data\Managed\com.uboat.modapi.dll",
    "$GamePath\UBOAT_Data\Managed\Unity.TextMeshPro.dll",
    "$GamePath\UBOAT_Data\Managed\com.uboat.rendering.dll",
    "$GamePath\UBOAT_Data\Managed\com.unity.postprocessing.Runtime.dll",
    "$GamePath\UBOAT_Data\Managed\UnityEngine.ImageConversionModule.dll",
    "$GamePath\UBOAT_Data\Managed\com.playway.water.Runtime.dll"
)

# csc is not on PATH in every shell; locate a compiler before invoking.
# Prefer Roslyn (modern C#) over the C# 5 framework compiler.
$cscPath = $null
$roslyn = Get-ChildItem "$env:ProgramFiles\dotnet\sdk" -Filter "csc.dll" -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match "Roslyn" } | Sort-Object FullName -Descending | Select-Object -First 1
if ($roslyn) {
    $cscPath = $roslyn.FullName
} else {
    $csc = Get-Command csc -ErrorAction SilentlyContinue
    if ($csc) {
        $cscPath = $csc.Source
    } elseif (Test-Path "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe") {
        $cscPath = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    }
}
if (-not $cscPath) {
    Write-Error "[RadioNavigation] csc not found. Install .NET SDK or add csc to PATH."
    exit 1
}

if ($cscPath.EndsWith(".dll")) {
    # Roslyn csc.dll needs explicit core-framework references; the game's
    # Managed facades are incomplete (e.g. missing SortedList forwards).
    $fx = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
    $refs += @(
        "$fx\mscorlib.dll",
        "$fx\System.dll",
        "$fx\System.Core.dll",
        "$fx\System.Xml.dll"
    )
}

$refArgs = $refs | ForEach-Object { "/reference:$_" }
$srcFiles = @(Get-ChildItem $srcDir -Filter "*.cs" -Recurse | ForEach-Object { $_.FullName })

New-Item -ItemType Directory -Force -Path $OutputDir -ErrorAction Stop | Out-Null
$outPath = Join-Path $OutputDir "radionavigation.dll"

# F78: C# preprocessor symbols are case-sensitive; /define:Debug would not
# match #if DEBUG. Map the Configuration to an UPPERCASE symbol.
# F228: mirror the game's RoslynCSharp compiler define set (UBOAT,
# UBOAT_2024_1, UNITY_STANDALONE_WIN; the game never defines RELEASE) so a
# local build compiles the same #if branches as the in-game compile. Debug
# adds DEBUG for local step-through only.
$gameSymbols = "UBOAT;UBOAT_2024_1;UNITY_STANDALONE_WIN"
$defineSymbol = if ($Configuration -eq "Debug") { "DEBUG;$gameSymbols" } else { $gameSymbols }

# F196: /debug:full + /optimize+ combined break step-through debugging
# (optimizer removes locals); split the flags per configuration.
$debugFlag = if ($Configuration -eq "Debug") { "/debug:full" } else { "/debug:pdbonly" }
$optimizeFlag = if ($Configuration -eq "Debug") { "/optimize-" } else { "/optimize+" }

$allArgs = @("/target:library", "/out:$outPath", $debugFlag, $optimizeFlag, "/define:$defineSymbol")
if ($cscPath.EndsWith(".dll")) {
    # /langversion:7 mirrors the game's RoslynCSharp mod compiler (C# 7.0):
    # the game rejects newer language features with CS8107 and then loads a
    # broken assembly (BadImageFormatException, in-game 2026-09-03).
    $allArgs += "/langversion:7"
}
$allArgs += $refArgs
$allArgs += $srcFiles

# The source list now exceeds Windows' 32K process command-line limit at this
# checkout path. A response file preserves raw csc/game-language parity and
# also quotes every path containing spaces. Delete only our own temporary file.
$responseDir = Join-Path $env:LOCALAPPDATA "Temp\opencode"
New-Item -ItemType Directory -Force -Path $responseDir | Out-Null
$responsePath = Join-Path $responseDir ("radionav-csc-" + [Guid]::NewGuid().ToString("N") + ".rsp")
try {
    $responseArgs = $allArgs | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    [IO.File]::WriteAllLines($responsePath, [string[]]$responseArgs, [Text.UTF8Encoding]::new($false))
    if ($cscPath.EndsWith(".dll")) {
        # /noconfig must be a command-line option, not a response-file option.
        & dotnet $cscPath /noconfig "@$responsePath"
    } else {
        & $cscPath "@$responsePath"
    }
    $compileCode = $LASTEXITCODE
} finally {
    Remove-Item -LiteralPath $responsePath -ErrorAction SilentlyContinue
}

if ($compileCode -eq 0) {
    Write-Host "[RadioNavigation] Build OK -> $outPath"
} else {
    Write-Host "[RadioNavigation] Build FAILED (exit $compileCode)"
    exit $compileCode
}

# PVS-Studio static-analysis gate (blocking). Skipped with -SkipPvs for a fast
# compile-check loop; gates.ps1 runs PVS as its own timed stage instead.
# Analyzes RadioNavigation.Analysis.csproj (net48 + real game DLLs) via
# pvs.ps1. A non-zero result (issues found or analyzer failure) fails the
# build so regressions can't slip in silently.
if (-not $SkipPvs) {
    Write-Host "[RadioNavigation] Running PVS-Studio gate ..."
    & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "pvs.ps1") -Configuration $Configuration
    $pvsCode = $LASTEXITCODE
    if ($pvsCode -ne 0) {
        Write-Host "[RadioNavigation] PVS-Studio gate FAILED (exit $pvsCode). See artifacts/reports/pvs/pvs-report.txt"
        exit $pvsCode
    }
    Write-Host "[RadioNavigation] PVS-Studio gate passed."
}
