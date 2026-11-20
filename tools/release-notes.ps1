# Prints the CHANGELOG.md section for one version (without its heading), for GitHub release notes.
# -Bullets returns just the bullet texts (plain, one per line), for update.json.
param([Parameter(Mandatory = $true)][string]$Version, [switch]$Bullets)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$lines = Get-Content (Join-Path $root 'CHANGELOG.md') -Encoding UTF8
$section = @(); $inside = $false
foreach ($line in $lines) {
    if ($line -match '^## ') { if ($inside) { break }; $inside = $line -match ('^## ' + [regex]::Escape($Version) + '\b'); continue }
    if ($inside) { $section += $line }
}
if (-not $section) { throw "CHANGELOG.md has no section for $Version" }
if (-not $Bullets) { return ($section -join "`n").Trim() }
$items = @()
foreach ($line in $section) {
    if ($line -match '^- (.*)') { $items += $Matches[1] }
    elseif ($line -match '^\s+(\S.*)' -and $items.Count) { $items[-1] = $items[-1] + ' ' + $Matches[1] }
}
$items | ForEach-Object { $_ -replace '\*\*', '' -replace '`', '' }
