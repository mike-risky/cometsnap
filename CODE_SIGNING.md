# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

> Status: Cometsnap has applied to the SignPath Foundation open source program. Until the first
> signed release is out, Windows builds are unsigned and SmartScreen may warn about an unknown publisher.

## What gets signed

Only files built by this repository's GitHub Actions workflow
([`.github/workflows/build.yml`](.github/workflows/build.yml)), from the source code in this repository,
on GitHub-hosted runners:

| File | What it is |
|---|---|
| `Cometsnap-Setup-x64.exe`, `Cometsnap-Setup-arm64.exe` | The Windows installer (Inno Setup), including its uninstaller |
| `Cometsnap-x64.exe`, `Cometsnap-arm64.exe` | The Cometsnap app, also shipped as a portable exe |

Tagged releases (`v*`) are signed. The automatic `nightly` and `test-prN` pre-releases are not.
The signing certificate is issued to SignPath Foundation, so Windows shows **SignPath Foundation** as the publisher.

The app bundles the .NET runtime and libraries from open source upstream projects (Microsoft .NET, MIT licensed),
which are included as they are. No proprietary components are included.

## Team roles

Cometsnap is maintained by one person.

| Role | Member |
|---|---|
| Committers and reviewers | [Michael Solioz](https://github.com/mike-risky) |
| Approvers | [Michael Solioz](https://github.com/mike-risky) |

- **Committers** may change the source code directly.
- **Reviewers** review every change proposed by someone who is not a committer (for example a pull request from a fork) before it is merged.
- **Approvers** approve each signing request by hand on SignPath before anything is signed.

All team members use multi-factor authentication on GitHub and on SignPath.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested
by the user or the person installing or operating it. See [PRIVACY.md](PRIVACY.md).

## Changes to your system

Everything Cometsnap changes is announced and can be undone:

- **Start with Windows**: a checkbox in Setup (on by default), and a switch in Cometsnap's Settings.
- **Print Screen key**: Cometsnap *offers* to take over Print Screen from your old capture tool after install; nothing changes unless you accept.
- **Uninstall**: *Settings > Apps > Cometsnap > Uninstall* removes the app, the startup entry and the Print Screen takeover. Your captures are kept.
  The portable exe installs nothing: quit it from the tray and delete the file.

## Reporting a problem

If you think a signed Cometsnap file is malicious or was signed by mistake, open an
[issue](https://github.com/mike-risky/cometsnap/issues) or contact SignPath Foundation at support@signpath.org.
