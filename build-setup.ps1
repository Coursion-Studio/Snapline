# Builds bin\SnaplineSetup.exe: one self-contained per-user installer with the app files embedded.
# Call it at the end of the app's own build (after bin\ has the app files):
#   & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build-setup.ps1')
# Needs: setup\*.cs, Snapline\Shared\SnapUi.cs, LICENSE.md, a 256 px app icon PNG and the app .ico.
param(
    [string]$Bin = (Join-Path $PSScriptRoot 'bin'),
    [string]$IconPng = (Join-Path $PSScriptRoot 'assets\icon.png'),   # shown in the setup window
    [string]$IconIco = (Join-Path $PSScriptRoot 'assets\app.ico')     # the setup .exe's own icon
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wpf = "$fw\WPF"
$payload = @('Snapline.exe')   # files from bin\ that get installed

$res = @()
foreach ($f in $payload) {
    $p = Join-Path $Bin $f
    if (-not (Test-Path $p)) { throw "Missing payload file: $p (build the app first)" }
    $res += "/resource:$p,payload.$f"
}
if (-not (Test-Path $IconPng)) { throw "Missing setup icon PNG: $IconPng" }
$res += "/resource:$IconPng,Setup.icon.png"
$res += "/resource:$(Join-Path $root 'LICENSE.md'),Setup.LICENSE.md"
$iconArg = @(); if (Test-Path $IconIco) { $iconArg = @("/win32icon:$IconIco") }

$src = @(Get-ChildItem (Join-Path $root 'setup') -Filter *.cs) + @(Get-Item (Join-Path $root 'Snapline\Shared\SnapUi.cs')) |
       ForEach-Object { $_.FullName }
& "$fw\csc.exe" /nologo /target:winexe /optimize+ /platform:anycpu /nowarn:1998 "/out:$Bin\SnaplineSetup.exe" @iconArg @res `
  "/r:$wpf\PresentationFramework.dll" "/r:$wpf\PresentationCore.dll" "/r:$wpf\WindowsBase.dll" `
  /r:System.Xaml.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:Microsoft.CSharp.dll /r:System.Core.dll /r:System.dll @src
if ($LASTEXITCODE -ne 0) { throw 'Setup build failed' }
Write-Host "Built $Bin\SnaplineSetup.exe"
