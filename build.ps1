param(
    [string]$GamePath = "C:\Program Files (x86)\Steam\steamapps\common\UBOAT",
    [string]$Configuration = "Release",
    [string]$OutputDir = "."
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

$srcDir = Join-Path $PSScriptRoot "Source"
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
    "$GamePath\UBOAT_Data\Managed\UnityEngine.ImageConversionModule.dll"
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

$outPath = Join-Path $OutputDir "radionavigation.dll"

# F78: C# preprocessor symbols are case-sensitive; /define:Debug would not
# match #if DEBUG. Map the Configuration to an UPPERCASE symbol.
$defineSymbol = if ($Configuration -eq "Debug") { "DEBUG" } else { "RELEASE" }

# F196: /debug:full + /optimize+ combined break step-through debugging
# (optimizer removes locals); split the flags per configuration.
$debugFlag = if ($Configuration -eq "Debug") { "/debug:full" } else { "/debug:pdbonly" }
$optimizeFlag = if ($Configuration -eq "Debug") { "/optimize-" } else { "/optimize+" }

if ($cscPath.EndsWith(".dll")) {
    $allArgs = @("/target:library", "/out:$outPath", $debugFlag, $optimizeFlag, "/define:$defineSymbol", "/noconfig")
    $allArgs += $refArgs
    $allArgs += $srcFiles
    & dotnet $cscPath $allArgs
} else {
    & $cscPath /target:library `
        /out:$outPath `
        $debugFlag `
        $optimizeFlag `
        /define:$defineSymbol `
        $refArgs `
        $srcFiles
}

if ($LASTEXITCODE -eq 0) {
    Write-Host "[RadioNavigation] Build OK -> $outPath"
} else {
    Write-Host "[RadioNavigation] Build FAILED (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}
