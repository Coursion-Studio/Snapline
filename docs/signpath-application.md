# SignPath Foundation application (draft)

For the open-source code signing programme at https://signpath.org (apply via the form linked there).
Review, adjust and submit it yourself. Nothing has been submitted.

| Field | Answer |
|---|---|
| Project name | Snapline |
| Repository | https://github.com/Coursion-Studio/Snapline |
| Homepage | https://github.com/Coursion-Studio/Snapline |
| Licence | MIT (OSI approved), no dual licensing: [LICENSE.md](../LICENSE.md) |
| Maintainer | Kuray Öğün (GitHub: kuraydev), Coursion Studio, Ankara, Türkiye |
| Contact | support@coursion.studio |
| Platform | Windows 10 and 11 (x64) |
| Language / build | C# / .NET 8 (WPF); installer built with the .NET Framework C# compiler |
| CI | GitHub Actions: [ci.yml](../.github/workflows/ci.yml) on every push and pull request, [release.yml](../.github/workflows/release.yml) on version tags |
| Files to sign | `Snapline.exe` (app) and `SnaplineSetup-<version>.exe` (installer and uninstaller) |
| Code signing policy | [CODE_SIGNING_POLICY.md](../CODE_SIGNING_POLICY.md) |
| Releases | https://github.com/Coursion-Studio/Snapline/releases |

## Project description

Snapline is a free screenshot and screen-recording tool for Windows. Press Print Screen, drag over an area or
click a window, and the capture is copied and shown in a preview card. It can annotate screenshots (arrows,
boxes, text, numbered steps, blur), record the screen as MP4 or GIF, combine several screenshots into one image,
and copy text from the screen with Windows' built-in text recognition. It installs per user without admin
rights and updates itself from this repository's releases.

## Why signing matters for this project

Snapline is distributed as an installer downloaded from GitHub. Unsigned, Windows SmartScreen shows "Windows
protected your PC" and "Unknown publisher", which stops many users or teaches them to click through warnings.
A signed installer from a trusted publisher lets users verify where it came from.

## Third-party components

All bundled code is open source; there are no closed or proprietary components:

- .NET 8 runtime and WPF (Microsoft, MIT), bundled into the self-contained exe
- Microsoft.Windows.SDK.NET projection and WinRT.Runtime (Microsoft, MIT), for Windows media encoding and text recognition
- Windows system libraries (user32, gdi32, dwmapi), used through the operating system, not redistributed

Test-only: xUnit and Microsoft.NET.Test.Sdk (not shipped).

## Requirements checklist

- [x] OSI-approved licence (MIT), no dual licensing
- [x] Public source repository containing everything that's built and shipped
- [x] No proprietary components bundled
- [x] Builds fully automated in GitHub Actions; releases built only by the Release workflow
- [x] Code signing policy published and linked from the README
- [ ] Two-factor authentication on GitHub and SignPath for every maintainer (confirm before submitting)
- [x] Releases already published: https://github.com/Coursion-Studio/Snapline/releases
