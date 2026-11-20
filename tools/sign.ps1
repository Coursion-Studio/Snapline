# Code-signs files with the Coursion Studio certificate (Authenticode), so Windows shows a named publisher
# instead of "Unknown publisher". Called by build.ps1; safe to run on its own.
#
# Setup, once per PC (see README, "Code signing"):
#   %APPDATA%\Coursion\keys\snapline-codesign.json
#   { "thumbprint": "<certificate thumbprint>", "timestamp": "<CA timestamp URL>" }
# The certificate itself stays with your CA's signing app (cloud or USB key). It just has to appear in
# Windows' "Personal" certificate store, which Certum SimplySign and SSL.com eSigner CKA both do.
#
# With no config file, files are left unsigned and a warning is printed (builds still work).
param([Parameter(Mandatory = $true)][string[]]$Files, [switch]$Require)
$ErrorActionPreference = 'Stop'

$config = Join-Path $env:APPDATA 'Coursion\keys\snapline-codesign.json'
if (-not (Test-Path $config)) {
    $msg = "Code signing not set up ($config missing). Files are UNSIGNED."
    if ($Require) { throw $msg }
    Write-Warning $msg
    return
}
$c = Get-Content $config -Raw | ConvertFrom-Json
if (-not $c.thumbprint) { throw "$config has no thumbprint." }
$thumb = ($c.thumbprint -replace '[^0-9A-Fa-f]', '').ToUpperInvariant()
$timestamp = if ($c.timestamp) { $c.timestamp } else { 'http://timestamp.sectigo.com' }

$cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue | Where-Object Thumbprint -eq $thumb | Select-Object -First 1
if (-not $cert) { throw "Certificate $thumb isn't in your Windows certificate store. Is your CA's signing app (SimplySign / eSigner) running and logged in?" }

$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object FullName -like '*\x64\*' | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $signtool) { throw 'signtool.exe not found. Install the Windows SDK (signing tools).' }

foreach ($f in $Files) {
    if (-not (Test-Path $f)) { throw "Nothing to sign at $f" }
    # Timestamping keeps the signature valid after the certificate expires. Timestamp servers are sometimes busy: retry.
    $ok = $false
    for ($i = 1; $i -le 3 -and -not $ok; $i++) {
        & $signtool sign /sha1 $thumb /fd sha256 /tr $timestamp /td sha256 /d 'Snapline' /du 'https://coursion.studio' $f | Out-Host
        $ok = $LASTEXITCODE -eq 0
        if (-not $ok) { Start-Sleep -Seconds (5 * $i) }
    }
    if (-not $ok) { throw "Signing failed for $f" }
    $sig = Get-AuthenticodeSignature $f
    if ($sig.SignerCertificate.Thumbprint -ne $thumb) { throw "$f doesn't carry the expected signature after signing." }
    Write-Host "Signed $(Split-Path $f -Leaf) ($($sig.Status), $($cert.GetNameInfo('SimpleName', $false)))"
}
