# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

## What gets signed

Only files built from this repository by its GitHub Actions [Release workflow](.github/workflows/release.yml):

| File | What it is |
|---|---|
| `Snapline.exe` | the app (signed before it is packed into the installer and the portable zip) |
| `SnaplineSetup-<version>.exe` | the installer, which is also the uninstaller |

Snapline ships no separate scripts or libraries; everything is inside these two files. No third-party
binaries are signed. The app bundles the .NET runtime and the Windows SDK projection from Microsoft, both
MIT licensed.

## How a release is built and signed

1. A maintainer pushes a version tag (`vX.Y.Z`) to this repository.
2. The Release workflow checks that the tag, the app, the installer and `CHANGELOG.md` name the same version,
   runs the tests, builds the app, and sends it to SignPath for signing.
3. The signed app is packed into the installer, and the installer is sent to SignPath for signing.
4. The workflow writes SHA-256 checksums of the signed files and creates a **draft** GitHub release.
5. A maintainer checks the draft and publishes it (see [docs/RELEASING.md](docs/RELEASING.md)).

Nothing is signed outside this workflow, and no signing keys are stored in the repository or on build machines.

## Roles

| Role | Who |
|---|---|
| Author, committer and reviewer | [Kuray Öğün](https://github.com/kuraydev) (Coursion Studio) |
| Approves each signing request | [Kuray Öğün](https://github.com/kuraydev) |

Contributions from others arrive as pull requests and are reviewed by a maintainer before they are merged.
All maintainers use two-factor authentication on GitHub and SignPath.

## Privacy

Snapline doesn't collect or send any data. Captures, recordings and recognised text stay on your PC. The only
network request is a daily check for updates (`update.json` from this repository's latest release), which
can be turned off in **Settings → Updates**. See the [README](README.md#-faq).
