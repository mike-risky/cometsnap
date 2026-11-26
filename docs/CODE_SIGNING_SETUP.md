# Setting up code signing (maintainer notes)

Cometsnap's Windows files are signed through the [SignPath Foundation](https://signpath.org) open source program.
Policy for users: [CODE_SIGNING.md](../CODE_SIGNING.md). This page is the one-time setup.

## How it works

The `sign` job in [`build.yml`](../.github/workflows/build.yml) runs for every `v*` tag (and for a manual run with
**sign** ticked) once the repository variable `SIGNPATH_ORGANIZATION_ID` exists. Until then it is skipped and
releases stay unsigned, exactly as before.

1. Builds `Cometsnap.exe` (x64, arm64) as usual, then asks Inno Setup for the unsigned uninstallers.
2. Signing request **app** on SignPath: both `Cometsnap.exe` and both uninstallers. Approve it on signpath.io.
3. Builds `Cometsnap-Setup-x64.exe` / `-arm64.exe` around the signed files.
4. Signing request **installer**: both setups. Approve it on signpath.io.
5. Checks every signature, then the release job publishes the signed files (and `SHA256SUMS.txt`).

If signing fails or is not approved within 5 hours, the tagged release is not published (nothing unsigned goes out under a tag).

## Settings in GitHub

*Settings > Secrets and variables > Actions*

| Kind | Name | Value |
|---|---|---|
| Secret | `SIGNPATH_API_TOKEN` | API token of a SignPath user with *submitter* rights on the project |
| Variable | `SIGNPATH_ORGANIZATION_ID` | Organization ID from SignPath (*Settings* page) |
| Variable | `SIGNPATH_PROJECT_SLUG` | Optional, defaults to `cometsnap` |
| Variable | `SIGNPATH_SIGNING_POLICY_SLUG` | Optional, defaults to `release-signing`. Use `test-signing` for a dry run |
| Secret | `WINGET_TOKEN` | Optional, see [RELEASING.md](RELEASING.md) |

## Settings in SignPath

- Project slug `cometsnap`, repository URL `https://github.com/mike-risky/cometsnap`.
- Trusted build system **GitHub.com** linked to the project. Install the [SignPath GitHub App](https://github.com/apps/signpath) on the repository.
- Two artifact configurations, pasted from this repository:
  - slug `app`: [`.signpath/artifact-configurations/app.xml`](../.signpath/artifact-configurations/app.xml)
  - slug `installer`: [`.signpath/artifact-configurations/installer.xml`](../.signpath/artifact-configurations/installer.xml)
- Signing policies `test-signing` and `release-signing` (SignPath creates these), with Michael Solioz as approver.

Both configurations take a `version` parameter (the release version, for example `0.2.0`), which must match the
product version of every file. The workflow sends it automatically.

## Notes

- The uninstaller is signed with Inno Setup's `SignedUninstallerDir` mechanism: the first compile writes
  `uninst-<inno version>-<hash>.e32` and stops, the workflow has SignPath sign it (sent as `.e32.exe`, since
  SignPath recognizes PE files by extension), and the second compile embeds that signature.
- Only GitHub-hosted runners may be used for the jobs leading to signing (SignPath checks this).
- The SignPath Foundation certificate names **SignPath Foundation** as the publisher.
