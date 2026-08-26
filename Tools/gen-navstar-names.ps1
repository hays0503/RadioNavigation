# SEXTANT_PLAN §7.7 — generates Source/Data/NavStarNames.cs from Stellarium name.fab.
# One-shot dev-time tool (like Tools/StarCatalogConverter). Run from repo root:
#   powershell -ExecutionPolicy Bypass -File Tools\gen-navstar-names.ps1
$ErrorActionPreference = "Stop"

$nameFab = "C:\code\projects\stellarium\skycultures\common_star_names.fab"
$outFile = Join-Path $PSScriptRoot "..\Source\Data\NavStarNames.cs"

# Historical note: SEXTANT_PLAN §3.7 pointed at stars/hip_gaia3/name.fab, but that
# file holds Bayer designations only. Proper names live in common_star_names.fab,
# line format:  HIP|_("Name") weight

# Classic 1940s almanac navigator's list (STAR_SKY_PLAN §3.7), 57 entries.
$wanted = @(
    "Alpheratz","Ankaa","Schedar","Diphda","Achernar","Hamal","Acamar","Menkar",
    "Mirfak","Aldebaran","Rigel","Capella","Bellatrix","Elnath","Alnilam",
    "Betelgeuse","Canopus","Sirius","Adhara","Procyon","Pollux","Avior","Suhail",
    "Miaplacidus","Alphard","Regulus","Dubhe","Denebola","Gienah","Acrux","Gacrux",
    "Alioth","Spica","Alkaid","Hadar","Menkent","Arcturus","Rigil Kentaurus",
    "Kochab","Alphecca","Antares","Atria","Sabik","Shaula","Rasalhague","Eltanin",
    "Kaus Australis","Vega","Nunki","Altair","Peacock","Deneb","Enif","Al Na'ir",
    "Fomalhaut","Markab","Polaris"
)

# Spelling variants used by Stellarium for the same star.
$aliases = @{ "Al Na'ir" = "Alnair" }

$lines = [System.IO.File]::ReadAllLines($nameFab)
$byName = @{}
foreach ($line in $lines) {
    $t = $line.Trim()
    if ($t.Length -eq 0 -or $t.StartsWith("#")) { continue }
    if ($t -notmatch '^(\d+)\|_\("(.+)"\)') { continue }
    $hip = [int]$Matches[1]
    $n = $Matches[2].Trim()
    if (-not $byName.ContainsKey($n)) { $byName[$n] = $hip }
}

$rows = @()
$missing = @()
foreach ($name in $wanted) {
    $lookup = $name
    if (-not $byName.ContainsKey($lookup) -and $aliases.ContainsKey($name)) {
        $lookup = $aliases[$name]
    }
    if ($byName.ContainsKey($lookup)) {
        $rows += [pscustomobject]@{ Hip = $byName[$lookup]; Name = $name }
        continue
    }
    $squashed = $name.Replace(" ", "")
    if ($byName.ContainsKey($squashed)) {
        $rows += [pscustomobject]@{ Hip = $byName[$squashed]; Name = $name }
        continue
    }
    $missing += $name
}

if ($missing.Count -gt 0) {
    throw "Names not found in name.fab: " + ($missing -join ", ")
}

$dups = $rows | Group-Object Hip | Where-Object { $_.Count -gt 1 }
if ($dups) { throw "Duplicate HIPs: " + (($dups | ForEach-Object Name) -join ", ") }

$rows = $rows | Sort-Object Hip
$q = [char]34
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("// Auto-generated from Stellarium skycultures/common_star_names.fab by Tools/gen-navstar-names.ps1")
[void]$sb.AppendLine("// (SEXTANT_PLAN sec. 7.7, star list STAR_SKY_PLAN sec. 3.7). Do not edit by hand.")
[void]$sb.AppendLine("using System.Collections.Generic;")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("namespace UBOAT.Mods.RadioNavigation")
[void]$sb.AppendLine("{")
[void]$sb.AppendLine("    public static class NavStarNames")
[void]$sb.AppendLine("    {")
[void]$sb.AppendLine("        public static readonly Dictionary<int, string> Names = new Dictionary<int, string>")
[void]$sb.AppendLine("        {")
foreach ($r in $rows) {
    $csName = $r.Name.Replace("'", [string][char]0x2019)
    [void]$sb.AppendLine("            { " + $r.Hip + ", " + $q + $csName + $q + " },")
}
[void]$sb.AppendLine("        };")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("        public static bool TryGetName(int hip, out string name)")
[void]$sb.AppendLine("        {")
[void]$sb.AppendLine("            return Names.TryGetValue(hip, out name);")
[void]$sb.AppendLine("        }")
[void]$sb.AppendLine("    }")
[void]$sb.AppendLine("}")

[System.IO.File]::WriteAllText($outFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("Wrote " + $rows.Count + " star names -> " + (Resolve-Path $outFile))
