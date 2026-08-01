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
    "$GamePath\UBOAT_Data\Managed\Unity.TextMeshPro.dll"
)

$refArgs = $refs | ForEach-Object { "/reference:`"$_`"" }
$srcFiles = Get-ChildItem $srcDir -Filter "*.cs" -Recurse | ForEach-Object { "`"" + $_.FullName + "`"" }

$outPath = Join-Path $OutputDir "radionavigation.dll"

# F78: C# preprocessor symbols are case-sensitive; /define:Debug would not
# match #if DEBUG. Map the Configuration to an UPPERCASE symbol.
$defineSymbol = if ($Configuration -eq "Debug") { "DEBUG" } else { "RELEASE" }

# F196: /debug:full + /optimize+ combined break step-through debugging
# (optimizer removes locals); split the flags per configuration.
$debugFlag = if ($Configuration -eq "Debug") { "/debug:full" } else { "/debug:pdbonly" }
$optimizeFlag = if ($Configuration -eq "Debug") { "/optimize-" } else { "/optimize+" }

csc /target:library `
    /out:$outPath `
    $debugFlag `
    $optimizeFlag `
    /define:$defineSymbol `
    $refArgs `
    $srcFiles

if ($LASTEXITCODE -eq 0) {
    Write-Host "[RadioNavigation] Build OK -> $outPath"
} else {
    Write-Host "[RadioNavigation] Build FAILED (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}
