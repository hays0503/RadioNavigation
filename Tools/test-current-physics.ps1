param(
    [string]$UnityPath = "C:\Program Files\Unity 2021.3.33f1\Editor\Unity.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw "Unity Editor not found: $UnityPath"
}

$work = Join-Path $root ("artifacts\current-physics-probe\" + [Guid]::NewGuid().ToString("N"))
# Unity imports package documentation; obj is excluded from the docs/graph gates.
$project = Join-Path $work "obj"
$sources = Join-Path $project "Assets\Editor"
$packages = Join-Path $project "Packages"
$settings = Join-Path $project "ProjectSettings"
$log = Join-Path $work "unity.log"
New-Item -ItemType Directory -Path $sources, $packages, $settings -Force | Out-Null
'm_EditorVersion: 2021.3.33f1' |
    Set-Content -LiteralPath (Join-Path $settings "ProjectVersion.txt") -Encoding Ascii
'{"dependencies":{"com.unity.modules.physics":"1.0.0"}}' |
    Set-Content -LiteralPath (Join-Path $packages "manifest.json") -Encoding Ascii
Copy-Item -LiteralPath (Join-Path $root "Source\Logic\ConstantCurrentField.cs"),
    (Join-Path $root "Source\Logic\CurrentWaterDrag.cs"),
    (Join-Path $root "Dev\CurrentAtlasPrototype\UnityPhysicsProbe.cs") -Destination $sources

Write-Host "Unity/PhysX component check; no UBOAT launch. Log: $log"
$process = Start-Process -FilePath $UnityPath -ArgumentList @(
    "-batchmode", "-nographics", "-quit", "-projectPath", ('"{0}"' -f $project),
    "-executeMethod", "UnityPhysicsProbe.Run", "-logFile", ('"{0}"' -f $log)
) -PassThru
if (-not $process.WaitForExit(180000)) {
    $process.Kill()
    throw "Unity probe exceeded 180 s; see $log"
}
$receipt = Select-String -LiteralPath $log -Pattern '^CURRENT_PHYSX_PASS ' | Select-Object -Last 1
if ($process.ExitCode -ne 0 -or $null -eq $receipt) {
    throw "Unity probe failed (exit $($process.ExitCode)); see $log"
}
Write-Host $receipt.Line
