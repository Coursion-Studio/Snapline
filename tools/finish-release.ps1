# Finishes a release drafted by the Release workflow. Runs on the maintainer's PC, because the update key
# (%APPDATA%\Coursion\keys\snapline-update.xml) never leaves it.
#
#   tools\finish-release.ps1 v1.2.3            download, verify, sign update.json, upload, publish
#   tools\finish-release.ps1 v1.2.3 -NoPublish  same, but leave the release as a draft
#
# Installed copies of Snapline read releases/latest/download/update.json, so they only see the new
# version once this has run.
param([Parameter(Mandatory = $true)][string]$Tag, [switch]$NoPublish, [string]$Repo = 'Coursion-Studio/Snapline')
$ErrorActionPreference = 'Stop'
$version = $Tag.TrimStart('v')
$keyFile = Join-Path $env:APPDATA 'Coursion\keys\snapline-update.xml'
if (-not (Test-Path $keyFile)) { throw "Update key not found at $keyFile. Restore it from your backup." }

$work = Join-Path ([IO.Path]::GetTempPath()) ("snapline-release-" + $version)
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $work | Out-Null
$name = "SnaplineSetup-$version.exe"
& gh release download $Tag -R $Repo -p $name -p "$name.sha256" -D $work
if ($LASTEXITCODE) { throw "Couldn't download $name from release $Tag." }

$exe = Join-Path $work $name
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
$published = ((Get-Content "$exe.sha256" -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
if ($hash -ne $published) { throw "Checksum mismatch: the download is $hash, the release says $published." }
$auth = Get-AuthenticodeSignature $exe
if ($auth.Status -eq 'Valid') { Write-Host "Installer is code-signed by $($auth.SignerCertificate.GetNameInfo('SimpleName', $false))." }
else { Write-Warning "Installer is not code-signed ($($auth.Status)). Users will see the SmartScreen warning." }

$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider -ArgumentList (New-Object System.Security.Cryptography.CspParameters 24)
$rsa.PersistKeyInCsp = $false
$rsa.FromXmlString((Get-Content $keyFile -Raw))
$signature = [Convert]::ToBase64String($rsa.SignData([IO.File]::ReadAllBytes($exe), 'SHA256'))

$feed = [ordered]@{
    version   = $version
    published = (Get-Date -Format 'yyyy-MM-dd')
    url       = "https://github.com/$Repo/releases/download/$Tag/$name"
    sha256    = $hash
    signature = $signature
    notes     = @(& (Join-Path $PSScriptRoot 'release-notes.ps1') -Version $version -Bullets)
}
$json = Join-Path $work 'update.json'
[IO.File]::WriteAllText($json, ($feed | ConvertTo-Json -Depth 3), (New-Object Text.UTF8Encoding $false))
& gh release upload $Tag $json -R $Repo --clobber
if ($LASTEXITCODE) { throw 'Uploading update.json failed.' }
Write-Host "Signed update.json uploaded to $Tag."

if ($NoPublish) { Write-Host 'Left as a draft (-NoPublish).'; return }
& gh release edit $Tag -R $Repo --draft=false --latest
if ($LASTEXITCODE) { throw 'Publishing the release failed.' }
Write-Host "Published $Tag. Installed copies will be offered $version within a day."
