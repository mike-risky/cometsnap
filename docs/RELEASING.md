# Releasing Cometsnap for Windows

## Cut a release

1. Make sure `main` is green and the `nightly` build works on a real PC.
2. Open <https://github.com/mike-risky/cometsnap/releases/new?tag=v1.0.0&target=main> (change the version),
   pick **Create new tag on publish**, and click **Publish release**. Title and notes can stay empty: the workflow fills them.
   (Or from a terminal: `git tag v1.0.0 && git push origin v1.0.0`.)
3. The `build` workflow builds, signs (two approvals on signpath.io once SignPath is set up) and publishes
   the release **Cometsnap v0.2.0** with the installers, the portable exes and `SHA256SUMS.txt`.
4. The `winget` job then writes the winget manifests and, if `WINGET_TOKEN` exists, opens the pull request on winget-pkgs.
5. Optional, recommended for the first signed releases: submit the installer to Microsoft (below).

## winget

Package identifier: **`MichaelSolioz.Cometsnap`**. Once accepted, anyone can run `winget install Cometsnap`.

- Manifests come from [`packaging/winget/New-WingetManifests.ps1`](../packaging/winget/New-WingetManifests.ps1)
  (installer type Inno, per-user scope, x64 and arm64, SHA256 of the release installers).
- Automatic submission: create a [classic personal access token](https://github.com/settings/tokens/new?scopes=public_repo&description=Cometsnap%20winget)
  with only the `public_repo` scope and save it as the repository secret `WINGET_TOKEN`. The workflow uses
  [wingetcreate](https://github.com/microsoft/winget-create), which forks `microsoft/winget-pkgs` under your account and opens the pull request.
- Without the token, download the `winget-manifests` artifact from the run and submit it by hand
  (`wingetcreate submit --token <token> <folder>`), or re-run the **winget** workflow by hand for a tag.
- The first pull request is reviewed by the winget maintainers (usually a few days). Later versions go faster.

## Microsoft malware analysis (SmartScreen reputation)

Even signed files can show SmartScreen for a while, until the certificate and the file earn reputation.
Submitting each new release to Microsoft speeds that up and clears any false Defender detection.

1. Go to <https://www.microsoft.com/en-us/wdsi/filesubmission> and sign in with a Microsoft account.
2. Choose **Software developer**.
3. Product: **Microsoft Defender SmartScreen** (or *Microsoft Defender Antivirus* if Defender flags the file).
4. Upload `Cometsnap-Setup-x64.exe` (zip several files together if you want to send more at once).
5. "What do you believe this file is?": **Incorrectly detected as malware/malicious** (or "unknown/warning").
6. Notes, for example:
   > Cometsnap is an open source (MIT) screenshot tool: https://github.com/mike-risky/cometsnap.
   > This installer is built by GitHub Actions from the public source and signed by SignPath Foundation.
   > Users see a SmartScreen warning on download. Please review and clear it.
7. You get a submission ID and an email when the analysis is done (usually within a few days).
