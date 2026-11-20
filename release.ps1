# Builds Snapline and writes, in dist\:
#   SnaplineSetup-<version>.exe         the installer
#   SnaplineSetup-<version>.exe.sha256  checksum
#   update.json                       the OTA feed entry: version, url, sha256, RSA signature, notes
# Publish all three to the PUBLIC repo Coursion-Studio/Snapline (see README, Releasing).
#
# Signing needs the private key from tools\new-update-key.ps1 (%APPDATA%\Coursion\keys\snapline-update.xml).
param([string]$DownloadBase = 'https://github.com/Coursion-Studio/Snapline/releases/download')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = [regex]::Match((Get-Content (Join-Path $root 'setup\Installer.cs') -Raw), 'Version = "([^"]+)"').Groups[1].Value
if (-not $version) { throw 'Could not read the version from setup\Installer.cs' }
$keyFile = Join-Path $env:APPDATA 'Coursion\keys\snapline-update.xml'
if (-not (Test-Path $keyFile)) { throw "Signing key not found at $keyFile. Run tools\new-update-key.ps1 once (or restore your backup)." }

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$name = "SnaplineSetup-$version.exe"
$exe = Join-Path $dist $name
Copy-Item (Join-Path $root 'bin\SnaplineSetup.exe') $exe -Force
$bytes = [IO.File]::ReadAllBytes($exe)   # read AFTER code signing (build.ps1), which changes the file
$auth = Get-AuthenticodeSignature $exe
$codeSigned = if ($auth.SignerCertificate) { "code-signed by $($auth.SignerCertificate.GetNameInfo('SimpleName', $false))" } else { 'NOT code-signed (SmartScreen will warn)' }

$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$exe.sha256" -Value "$hash  $name" -Encoding ascii -NoNewline

$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider -ArgumentList (New-Object System.Security.Cryptography.CspParameters 24)
$rsa.PersistKeyInCsp = $false
$rsa.FromXmlString((Get-Content $keyFile -Raw))
$signature = [Convert]::ToBase64String($rsa.SignData($bytes, 'SHA256'))

# Release notes: the bullet points under this version's heading in CHANGELOG.md (continuation lines joined).
$notes = @()
$inSection = $false
foreach ($line in Get-Content (Join-Path $root 'CHANGELOG.md') -Encoding UTF8) {
    if ($line -match '^## ') { if ($inSection) { break }; $inSection = $line -match [regex]::Escape($version); continue }
    if (-not $inSection) { continue }
    if ($line -match '^- (.*)') { $notes += $Matches[1] }
    elseif ($line -match '^\s+(\S.*)' -and $notes.Count) { $notes[-1] = $notes[-1] + ' ' + $Matches[1] }
}
$notes = $notes | ForEach-Object { $_ -replace '\*\*', '' -replace '`', '' }

$feed = [ordered]@{
    version   = $version
    published = (Get-Date -Format 'yyyy-MM-dd')
    url       = "$DownloadBase/v$version/$name"
    sha256    = $hash
    signature = $signature
    notes     = @($notes)
}
$feed | ConvertTo-Json -Depth 3 | Set-Content -Path (Join-Path $dist 'update.json') -Encoding utf8
Write-Host "Release $version ready in $dist ($($notes.Count) notes), $codeSigned"
