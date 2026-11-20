# Fails unless the app, the installer, the changelog (and the tag, if given) all name the same version.
# Prints the version on success.   Usage: tools\check-version.ps1 [-Tag v1.2.3]
param([string]$Tag)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$app = [regex]::Match((Get-Content (Join-Path $root 'Snapline\Snapline.csproj') -Raw), '<Version>([^<]+)</Version>').Groups[1].Value
$setup = [regex]::Match((Get-Content (Join-Path $root 'setup\Installer.cs') -Raw), 'Version = "([^"]+)"').Groups[1].Value
$log = [regex]::Match((Get-Content (Join-Path $root 'CHANGELOG.md') -Raw -Encoding UTF8), '(?m)^## (\d+\.\d+\.\d+)').Groups[1].Value
$problems = @()
if (-not $app) { $problems += 'No <Version> in Snapline\Snapline.csproj' }
if ($setup -ne $app) { $problems += "setup\Installer.cs says '$setup', Snapline.csproj says '$app'" }
if ($log -ne $app) { $problems += "The newest CHANGELOG.md section is '$log', Snapline.csproj says '$app'" }
if ($Tag -and $Tag.TrimStart('v') -ne $app) { $problems += "Tag '$Tag' doesn't match version '$app'" }
if ($problems.Count) { $problems | ForEach-Object { Write-Host "::error::$_" }; throw ($problems -join '; ') }
$app
