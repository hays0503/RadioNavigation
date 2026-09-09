param(
    [switch]$DryRun,
    [double]$MinHeight = 15,
    [string]$RegistryPath = (Join-Path $PSScriptRoot '..\Source\Data\Leuchtfeuer.json'),
    [string]$MinorPath = (Join-Path $PSScriptRoot '..\Source\Data\LeuchtfeuerMinor.json')
)

# Moves low fire-height lighthouse entries out of the runtime registry
# (user decision 2026-09-09: only lights with fireHeight >= 15 m stay on
# the map and in 3D; smaller lights made the map look like marker soup).
#
# Input : Source/Data/Leuchtfeuer.json       - current runtime registry
#         Source/Data/LeuchtfeuerMinor.json  - archive (optional; created
#                                              on first real run)
# Output: Source/Data/Leuchtfeuer.json       - entries kept in the fleet
#         Source/Data/LeuchtfeuerMinor.json  - moved entries (archive;
#                                              the mod never reads this)
#
# Re-runnable in BOTH directions (ADR-031): raising -MinHeight moves more
# entries to the archive, lowering it moves them back into the registry.
# Only theater entries (LeuchtfeuerRegistry bounds: lat 49..62, lon
# -5..20) are classified; non-theater rows stay in the main file
# regardless of height. Curated profiles live in C# (LighthouseProfiles.cs)
# and are NOT touched by this script.
#
# Examples:
#   .\Exclude-MinorLights.ps1 -DryRun          # counts only, no writes
#   .\Exclude-MinorLights.ps1                  # 15 m threshold
#   .\Exclude-MinorLights.ps1 -MinHeight 10    # move <10 m instead

$ErrorActionPreference = 'Stop'

# Theater window mirrors LeuchtfeuerRegistry.Min/MaxLatitude/Longitude.
$minLat = 49.0
$maxLat = 62.0
$minLon = -5.0
$maxLon = 20.0

function In-Theater([double]$lat, [double]$lon) {
    return ($lat -ge $minLat -and $lat -le $maxLat -and
            $lon -ge $minLon -and $lon -le $maxLon)
}

# Mirrors LeuchtfeuerRegistry.ParseFireHeightM: digits collected until the
# first non-digit after the number has started ("13 m" -> 13, garbage -> 0).
function Get-FireHeightM([string]$fireHeight) {
    if ([string]::IsNullOrEmpty($fireHeight)) { return 0.0 }
    $digits = ''
    $seenDigit = $false
    foreach ($c in $fireHeight.ToCharArray()) {
        if ([char]::IsDigit($c) -or $c -eq '.' -or $c -eq ',') {
            if ($c -eq ',') { $digits += '.' } else { $digits += $c }
            $seenDigit = $true
        } elseif ($seenDigit) {
            break
        }
    }
    if (-not $seenDigit) { return 0.0 }
    $value = 0.0
    if (-not [double]::TryParse(
            $digits,
            [System.Globalization.NumberStyles]::Float,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [ref]$value)) {
        return 0.0
    }
    return $value
}

# Composite key: codes repeat in the DB (distinct lights share a code), so
# identity is code AND coordinates rounded to 3 decimals (~110 m).
function EntryKey($code, $lat, $lon) {
    return "{0}|{1}|{2}" -f $code, [math]::Round([double]$lat, 3), [math]::Round([double]$lon, 3)
}

if (-not (Test-Path -LiteralPath $RegistryPath)) {
    throw "registry not found: $RegistryPath"
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$registry = Get-Content -LiteralPath $RegistryPath -Raw -Encoding UTF8 | ConvertFrom-Json
$entries = @($registry.entries)

$minorEntries = @()
if (Test-Path -LiteralPath $MinorPath) {
    $existing = Get-Content -LiteralPath $MinorPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $minorEntries = @($existing.entries)
}

$kept = New-Object System.Collections.Generic.List[object]
$movedOut = New-Object System.Collections.Generic.List[object]
foreach ($entry in $entries) {
    $h = Get-FireHeightM $entry.fireHeight
    if ((In-Theater ([double]$entry.lat) ([double]$entry.lon)) -and $h -lt $MinHeight) {
        $movedOut.Add($entry)
    } else {
        $kept.Add($entry)
    }
}

# Reverse direction: archive rows that no longer qualify (threshold lowered
# or row reclassified) go back into the runtime registry.
$movedBack = New-Object System.Collections.Generic.List[object]
$minorKept = New-Object System.Collections.Generic.List[object]
foreach ($entry in $minorEntries) {
    $h = Get-FireHeightM $entry.fireHeight
    if ((In-Theater ([double]$entry.lat) ([double]$entry.lon)) -and $h -lt $MinHeight) {
        $minorKept.Add($entry)
    } else {
        $movedBack.Add($entry)
    }
}

Write-Host ("registry entries      : {0}" -f $entries.Count)
Write-Host ("minor archive entries : {0}" -f $minorEntries.Count)
Write-Host ("to move out           : {0} (theater, fireHeight < {1} m)" -f $movedOut.Count, $MinHeight)
Write-Host ("to keep in registry   : {0}" -f $kept.Count)
Write-Host ("to move back          : {0} (no longer below threshold)" -f $movedBack.Count)
Write-Host ("staying in archive    : {0}" -f $minorKept.Count)

if ($movedOut.Count + $kept.Count -ne $entries.Count) {
    throw "conservation violated (registry): $($movedOut.Count) + $($kept.Count) != $($entries.Count)"
}
if ($movedBack.Count + $minorKept.Count -ne $minorEntries.Count) {
    throw "conservation violated (archive): $($movedBack.Count) + $($minorKept.Count) != $($minorEntries.Count)"
}

if ($DryRun) {
    Write-Host "DRY RUN - no files written"
    exit 0
}

# Pure repartition (lossless): every entry from either file lands in the
# file its predicate selects. No dedupe — distinct lights can share a
# composite key at 3-decimal rounding (real case E1197.x Outer/Inner Mole
# Head, Celle Ligure), and dropping one would corrupt the dataset.
$finalMinor = New-Object System.Collections.Generic.List[object]
foreach ($e in $minorKept) { $finalMinor.Add($e) }
foreach ($e in $movedOut) { $finalMinor.Add($e) }

$finalRegistry = New-Object System.Collections.Generic.List[object]
foreach ($e in $kept) { $finalRegistry.Add($e) }
foreach ($e in $movedBack) { $finalRegistry.Add($e) }

if ($finalRegistry.Count + $finalMinor.Count -ne ($entries.Count + $minorEntries.Count)) {
    throw ("total conservation violated: {0} + {1} != {2}" -f
        $finalRegistry.Count, $finalMinor.Count, ($entries.Count + $minorEntries.Count))
}

# Anomaly report (warn-only): same composite key more than once within one
# output file. Distinct lights sharing rounded coordinates are legitimate;
# true duplicated rows would show up here for manual review.
function Warn-DuplicateKeys($list, $label) {
    $dups = @($list | Group-Object -Property {
            EntryKey $_.code $_.lat $_.lon
        } | Where-Object { $_.Count -gt 1 })
    foreach ($g in $dups) {
        Write-Warning ("duplicate key in {0}: {1} ({2} entries)" -f $label, $g.Name, $g.Count)
    }
}
Warn-DuplicateKeys $finalRegistry 'registry'
Warn-DuplicateKeys $finalMinor 'archive'

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
    count     = $finalRegistry.Count
    countries = $registry.countries
    entries   = $finalRegistry
}
[System.IO.File]::WriteAllText($RegistryPath, (ConvertTo-CompactJson ([pscustomobject]$registryOut)), $utf8NoBom)

$minorOut = [ordered]@{
    count     = $finalMinor.Count
    reason    = ('fire-height curation 2026-09-09 (ADR-031): theater lights with fireHeight < {0} m removed from the runtime fleet' -f $MinHeight)
    entries   = $finalMinor
}
[System.IO.File]::WriteAllText($MinorPath, (ConvertTo-CompactJson ([pscustomobject]$minorOut)), $utf8NoBom)

Write-Host ("written {0} ({1} entries)" -f $RegistryPath, $finalRegistry.Count)
Write-Host ("written {0} ({1} entries)" -f $MinorPath, $finalMinor.Count)
