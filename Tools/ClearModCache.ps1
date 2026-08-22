$TempDir = "$env:USERPROFILE\AppData\LocalLow\Deep Water Studio\UBOAT\Temp"
$ErrorActionPreference = "SilentlyContinue"
$files = @(
    (Join-Path $TempDir "radionavigation.dll")
    (Join-Path $TempDir "radionavigation.dll.mdb")
)
foreach ($f in $files) {
    if (Test-Path -LiteralPath $f) {
        for ($i=0; $i -lt 5; $i++) {
            try { Remove-Item -LiteralPath $f -Force; Write-Host "Cleared $f"; break } catch {
                Write-Host "Retry $($i+1)/5 lock on $f"
                Start-Sleep -Milliseconds 250
            }
        }
        if (Test-Path -LiteralPath $f) { Write-Warning "Failed $f - close game, exclude Temp from AV" }
    } else {
        Write-Host "No cache $f"
    }
}
Write-Host "Done - next launch will recompile (expect ~13s)"
