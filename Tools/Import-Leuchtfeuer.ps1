param(
    [string]$JsonPath = (Join-Path $PSScriptRoot '..\Source\Data\Leuchtfeuer.json'),
    [string]$Country = '',
    [string]$NameMatch = '',
    [string]$Code = '',
    [int]$Max = 25,
    [string]$PrefabPath = 'lighthouse',
    [string]$RotatorObjectName = 'LighthouseRotator',
    [float]$RotationDegreesPerSecond = 0,
    [switch]$Enabled,
    [string]$OutFile = ''
)

# Converts entries of the Leuchtfeuer registry into LighthouseProfile C#
# blocks for the mod's registry (Source/Data/LighthouseProfiles.cs).
#
# Examples:
#   .\Import-Leuchtfeuer.ps1 -Country Deutschland -Max 10
#   .\Import-Leuchtfeuer.ps1 -NameMatch 'Leuchtturm' -OutFile profiles.txt
#   .\Import-Leuchtfeuer.ps1 -Code B1054
#
# The emitted block still needs two manual touches before it is pasted into
# LighthouseProfiles.cs: RotationDegreesPerSecond (0 = fixed light, 360 /
# rotation period in seconds for a rotating beam) and Enabled.

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $JsonPath)) {
    throw "Leuchtfeuer registry not found: $JsonPath"
}

$data = Get-Content -LiteralPath $JsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
$entries = @($data.entries)

if ($Country)  { $entries = @($entries | Where-Object { $_.country -eq $Country }) }
if ($NameMatch) { $entries = @($entries | Where-Object { $_.name -match $NameMatch }) }
if ($Code)     { $entries = @($entries | Where-Object { $_.code -eq $Code }) }
$entries = @($entries | Select-Object -First $Max)

if ($entries.Count -eq 0) {
    Write-Warning 'No entries matched the filter.'
    return
}

function Quote([string]$value) {
    return $value.Replace('\', '\\').Replace('"', '\"')
}

function ParseHeight([string]$fireHeight) {
    if (-not $fireHeight) { return 0 }
    $match = [regex]::Match($fireHeight, '\d+(?:[.,]\d+)?')
    if (-not $match.Success) { return 0 }
    return [double]::Parse($match.Value.Replace(',', '.'), [Globalization.CultureInfo]::InvariantCulture)
}

$builder = New-Object System.Text.StringBuilder
foreach ($entry in $entries) {
    $name = Quote([string]$entry.name)
    $country = Quote([string]$entry.country)
    $location = Quote([string]$entry.location)
    $chars = Quote([string]$entry.characteristic)
    $type = Quote([string]$entry.type)
    $height = ParseHeight ([string]$entry.fireHeight)
    $enabledLiteral = if ($Enabled) { 'true' } else { 'false' }

    [void]$builder.AppendLine('            new LighthouseProfile(')
    [void]$builder.AppendLine("                code: ""$($entry.code)"",")
    [void]$builder.AppendLine("                name: ""$name"",")
    [void]$builder.AppendLine("                country: ""$country"",")
    [void]$builder.AppendLine("                location: ""$location"",")
    [void]$builder.AppendLine("                latitude: $($entry.lat)f,")
    [void]$builder.AppendLine("                longitude: $($entry.lon)f,")
    [void]$builder.AppendLine("                fireHeightM: ${height}f,")
    [void]$builder.AppendLine("                characteristic: ""$chars"",")
    [void]$builder.AppendLine("                type: ""$type"",")
    [void]$builder.AppendLine("                prefabPath: ""$PrefabPath"",")
    [void]$builder.AppendLine("                rotatorObjectName: ""$RotatorObjectName"",")
    [void]$builder.AppendLine("                rotationDegreesPerSecond: ${RotationDegreesPerSecond}f,")
    [void]$builder.AppendLine('                enabled: ' + $enabledLiteral + '),')
}

$text = $builder.ToString()
if ($OutFile) {
    Set-Content -LiteralPath $OutFile -Value $text -Encoding UTF8
    Write-Host "Wrote $($entries.Count) profile block(s) to $OutFile"
} else {
    Write-Host "Matched $($entries.Count) entry(ies):"
    Write-Output $text
}
