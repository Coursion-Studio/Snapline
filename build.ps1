# Builds Snapline into bin\:
#   bin\Snapline.exe        one self-contained exe (.NET 8 runtime included, nothing to install)
#   bin\SnaplineSetup.exe   the per-user installer with Snapline.exe embedded
# Needs the .NET 8 SDK. The installer is built with the C# compiler that ships with Windows.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null

# Keep the app's version in step with the installer's.
$version = [regex]::Match((Get-Content (Join-Path $root 'setup\Installer.cs') -Raw), 'Version = "([^"]+)"').Groups[1].Value
$projVersion = [regex]::Match((Get-Content (Join-Path $root 'Snapline\Snapline.csproj') -Raw), '<Version>([^<]+)</Version>').Groups[1].Value
if ($version -ne $projVersion) { throw "Version mismatch: setup\Installer.cs says $version, Snapline.csproj says $projVersion" }

& dotnet publish (Join-Path $root 'Snapline\Snapline.csproj') -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o (Join-Path $root 'obj\publish') --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Snapline build failed' }
Copy-Item (Join-Path $root 'obj\publish\Snapline.exe') (Join-Path $bin 'Snapline.exe') -Force
Write-Host "Built $bin\Snapline.exe ($version)"

# Code signing (skipped with a warning until tools\sign.ps1 is set up). The app is signed BEFORE it is packed
# into the installer, and the installer (which is also the uninstaller) after.
$sign = Join-Path $root 'tools\sign.ps1'
& $sign -Files (Join-Path $bin 'Snapline.exe')

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build-setup.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Setup build failed' }
& $sign -Files (Join-Path $bin 'SnaplineSetup.exe')
