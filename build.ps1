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
    "$GamePath\UBOAT_Data\Managed\UnityEngine.UI.dll",
    "$GamePath\UBOAT_Data\Managed\UnityEngine.IMGUIModule.dll",
    "$GamePath\UBOAT_Data\Managed\UnityEngine.InputLegacyModule.dll",
    "$GamePath\UBOAT_Data\Managed\UnityEngine.PhysicsModule.dll",
    "$GamePath\UBOAT_Data\Managed\UnityEngine.TextRenderingModule.dll",
    "$GamePath\UBOAT_Data\Managed\0Harmony.dll",
    "$GamePath\UBOAT_Data\Managed\DWS.Common.dll"
)

$refArgs = $refs | ForEach-Object { "/reference:`"$_`"" }
$srcFiles = Get-ChildItem $srcDir -Filter "*.cs" | ForEach-Object { "`"" + $_.FullName + "`"" }

$outPath = Join-Path $OutputDir "testui.dll"

csc /target:library `
    /out:$outPath `
    /debug:full `
    /optimize+ `
    /define:$Configuration `
    $refArgs `
    $srcFiles

if ($LASTEXITCODE -eq 0) {
    Write-Host "[TestUI] Build OK -> $outPath"
} else {
    Write-Host "[TestUI] Build FAILED (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}
