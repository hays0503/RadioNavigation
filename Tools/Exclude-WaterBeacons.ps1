param(
    [switch]$DryRun,
    [string]$CsvPath = (Join-Path $PSScriptRoot '..\Source\Data\LeuchtfeuerLandProbe.csv'),
    [string]$RegistryPath = (Join-Path $PSScriptRoot '..\Source\Data\Leuchtfeuer.json'),
    [string]$ExcludedPath = (Join-Path $PSScriptRoot '..\Source\Data\LeuchtfeuerExcluded.json')
)

# Moves water-side lighthouse entries out of the runtime registry.
#
# Input : Source/Data/LeuchtfeuerLandProbe.csv (rn_lighthouse_landprobe)
#         rows with verdict=water are excluded (user decision 2026-09-09:
#         only land-standing beacons stay in the fleet; shore band 0.3 km).
# Output: Source/Data/Leuchtfeuer.json          - land + borderline entries
#         Source/Data/LeuchtfeuerExcluded.json  - moved water entries (archive;
#                                                 the mod never reads this file)
#
# Curated profiles live in C# (LighthouseProfiles.cs) and are NOT touched:
# the CSV lists them for review, but a curated entry is handled manually
# (warn-only policy).
#
# Examples:
#   .\Exclude-WaterBeacons.ps1 -DryRun   # counts only, no writes
#   .\Exclude-WaterBeacons.ps1           # performs the move

$ErrorActionPreference = 'Stop'

function Contains-Code($list, $code) {
    foreach ($e in $list) { if ($e.code -eq $code) { return $true } }
    return $false
}

if (-not (Test-Path -LiteralPath $CsvPath)) {
    throw "sweep CSV not found: $CsvPath (run rn_lighthouse_landprobe in game first)"
}
if (-not (Test-Path -LiteralPath $RegistryPath)) {
    throw "registry not found: $RegistryPath"
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$sweep = Import-Csv -LiteralPath $CsvPath -Delimiter ';'
$registry = Get-Content -LiteralPath $RegistryPath -Raw -Encoding UTF8 | ConvertFrom-Json
$entries = @($registry.entries)

$excludedEntries = @()
$excludedKeys = New-Object System.Collections.Generic.HashSet[string]
if (Test-Path -LiteralPath $ExcludedPath) {
    $existing = Get-Content -LiteralPath $ExcludedPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $excludedEntries = @($existing.entries)
    foreach ($e in $excludedEntries) {
        [void]$excludedKeys.Add((EntryKey $e.code $e.lat $e.lon))
    }
}

# Composite key: codes repeat in the DB (e.g. B1258 Ost/West are two
# different lights), so the sweep row is matched to a registry entry by
# code AND coordinates rounded to 3 decimals (~110 m; the CSV carries
# float-formatted values, the registry full doubles — 3 decimals absorbs
# the float noise without colliding distinct lights).
function EntryKey($code, $lat, $lon) {
    return "{0}|{1}|{2}" -f $code, [math]::Round([double]$lat, 3), [math]::Round([double]$lon, 3)
}

$waterKeys = @{}
foreach ($row in $sweep) {
    if ($row.verdict -eq 'water' -and $row.source -eq 'registry') {
        $waterKeys[(EntryKey $row.code $row.lat $row.lon)] = $true
    }
}

$curatedWater = @($sweep | Where-Object { $_.verdict -eq 'water' -and $_.source -eq 'curated' })
if ($curatedWater.Count -gt 0) {
    Write-Warning "curated profiles classified as water (manual C# decision, NOT moved):"
    foreach ($r in $curatedWater) {
        Write-Warning ("  {0} {1} ({2} km)" -f $r.code, $r.name, $r.distanceKm)
    }
}

$kept = New-Object System.Collections.Generic.List[object]
$moved = New-Object System.Collections.Generic.List[object]
foreach ($entry in $entries) {
    if ($waterKeys.ContainsKey((EntryKey $entry.code $entry.lat $entry.lon))) {
        $moved.Add($entry)
    } else {
        $kept.Add($entry)
    }
}

Write-Host ("registry entries      : {0}" -f $entries.Count)
Write-Host ("water rows in sweep   : {0} (registry source)" -f $waterKeys.Count)
Write-Host ("to move               : {0}" -f $moved.Count)
Write-Host ("to keep               : {0}" -f $kept.Count)
Write-Host ("already excluded file : {0}" -f $excludedEntries.Count)

if ($moved.Count + $kept.Count -ne $entries.Count) {
    throw "conservation violated: moved $($moved.Count) + kept $($kept.Count) != original $($entries.Count)"
}

$missing = @()
foreach ($k in $waterKeys.Keys) {
    $code = ($k -split '\|')[0]
    if (-not (Contains-Code $moved $code)) {
        $missing += $k
    }
}
if ($missing.Count -gt 0) {
    throw ("sweep water keys unmatched in registry: {0}" -f (($missing | Select-Object -First 5) -join ', '))
}

if ($DryRun) {
    Write-Host "DRY RUN - no files written"
    exit 0
}

# Merge into the exclusion archive (dedupe by composite key — codes
# repeat across distinct lights, e.g. B1258 Ost/West).
foreach ($m in $moved) {
    $key = EntryKey $m.code $m.lat $m.lon
    if (-not $excludedKeys.Contains($key)) {
        $excludedEntries += $m
        [void]$excludedKeys.Add($key)
    }
}

function ConvertTo-CompactJson($object) {
    # PS 5.1 ConvertTo-Json escapes every non-ASCII char as \uXXXX, which
    # the mod's MiniJson parser does not support ("Unknown escape \u").
    # Decode them back to literal UTF-8 characters before writing.
    $json = ConvertTo-Json -InputObject $object -Depth 6
    return [regex]::Replace($json, '\\u([0-9a-fA-F]{4})', {
        param($m)
        return [string][char][Convert]::ToInt32($m.Groups[1].Value, 16)
    })
}

$registryOut = [ordered]@{
    count     = $kept.Count
    countries = $registry.countries
    entries   = $kept
}
[System.IO.File]::WriteAllText($RegistryPath, (ConvertTo-CompactJson ([pscustomobject]$registryOut)), $utf8NoBom)

$excludedOut = [ordered]@{
    count     = $excludedEntries.Count
    reason    = 'land curation 2026-09-09: water-side beacons beyond 0.3 km shore band removed from the runtime fleet'
    entries   = $excludedEntries
}
[System.IO.File]::WriteAllText($ExcludedPath, (ConvertTo-CompactJson ([pscustomobject]$excludedOut)), $utf8NoBom)

Write-Host ("written {0} ({1} entries)" -f $RegistryPath, $kept.Count)
Write-Host ("written {0} ({1} entries)" -f $ExcludedPath, $excludedEntries.Count)
