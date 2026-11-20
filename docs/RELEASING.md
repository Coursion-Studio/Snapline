# Releasing Snapline

## The short version

1. Bump the version in three places: `Snapline/Snapline.csproj` (`<Version>`), `setup/Installer.cs`
   (`Version` and the two assembly attributes), and a new `## x.y.z — date` section at the top of `CHANGELOG.md`.
   `tools/check-version.ps1` and the tests fail if they disagree.
2. Commit, then tag and push: `git tag vX.Y.Z` and `git push origin main vX.Y.Z`.
3. The [Release workflow](../.github/workflows/release.yml) tests, builds, signs (once SignPath is set up)
   and creates a **draft** release with the installer, a portable zip and `.sha256` files.
4. On your PC: `tools\finish-release.ps1 vX.Y.Z`. It downloads the installer, checks its checksum, signs
   `update.json` with your update key, uploads it and publishes the release.

Installed copies check `releases/latest/download/update.json` once a day, so they're offered the update
only after step 4.

## Why the last step is local

Two different signatures protect users:

| Signature | Proves | Key lives |
|---|---|---|
| Code signing (Authenticode) | to Windows, who published the exe and installer | SignPath, used by CI |
| Update signature (RSA, in `update.json`) | to Snapline's updater, that an update really comes from this project | `%APPDATA%\Coursion\keys\snapline-update.xml` on the maintainer's PC only |

The update key must never be uploaded to GitHub, so CI can't write `update.json`. The public half is in
`Snapline/Updates/UpdateKey.cs`. **Back up the private key**: without it, installed copies can't be updated.

## Turning on SignPath signing

After SignPath approves the project (application draft: [signpath-application.md](signpath-application.md)):

1. In SignPath, create the project `snapline` with two artifact configurations:
   - `app`: a zip containing `Snapline.exe`, sign it with Authenticode.
   - `installer`: a zip containing `SnaplineSetup.exe`, sign it with Authenticode.
   Add the GitHub Actions trusted build system and a `release-signing` policy that requires manual approval.
2. In this repository's **Settings → Secrets and variables → Actions**:
   - secret `SIGNPATH_API_TOKEN`
   - variables `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG` (`snapline`),
     `SIGNPATH_SIGNING_POLICY_SLUG` (`release-signing`)
   - variable `SIGNING` = `signpath` (this switches the signing steps on)
3. Push the next tag. Approve the two signing requests in SignPath when the workflow asks.
4. Check: `Get-AuthenticodeSignature` on the downloaded `Snapline.exe` and installer says `Valid`, and the
   `.sha256` files match the downloads.

SmartScreen still warns about a new certificate until it builds reputation from downloads; that takes days
to weeks.

## Releasing entirely on your PC

`release.ps1` still builds, (optionally) signs with a certificate in your Windows certificate store
(`tools/sign.ps1`), and writes the installer, `.sha256` and `update.json` to `dist\`. Upload all three with
`gh release create`. Use this only if CI is unavailable.

## Self-test

`bin\Snapline.exe --dev <folder> [all|overlay|quick|editor|combine|flyout|mp4|gif|ocr|ui|docs|ota <feed url>]`
writes screenshots and a `report.txt`. `ui` and `docs` render windows off-screen (`docs` makes the README
images, with made-up content); `ota` checks an update feed, including three kinds of tampering.
