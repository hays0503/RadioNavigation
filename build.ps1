param(
    [string]$GamePath = "C:\Program Files (x86)\Steam\steamapps\common\UBOAT",
    [string]$Configuration = "Release",
    [string]$OutputDir = "."
)

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

csc /target:library `
    /out:$outPath `
    /debug:full `
    /optimize+ `
    /define:$defineSymbol `
    $refArgs `
    $srcFiles

if ($LASTEXITCODE -eq 0) {
    Write-Host "[RadioNavigation] Build OK -> $outPath"
} else {
    Write-Host "[RadioNavigation] Build FAILED (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}
