<#
.SYNOPSIS
  Writes the winget manifests for a tagged Cometsnap release.
.EXAMPLE
  ./packaging/winget/New-WingetManifests.ps1 -Tag v0.2.0
  Creates out/winget/manifests/m/MichaelSolioz/Cometsnap/0.2.0/ with the three YAML files,
  ready for "wingetcreate submit" or a manual pull request to microsoft/winget-pkgs.
#>
param(
  [Parameter(Mandatory)] [string] $Tag,
  [string] $Repo = "mike-risky/cometsnap",
  [string] $OutRoot = "out/winget"
)
$ErrorActionPreference = "Stop"
$version = $Tag.TrimStart("v")
$id = "MichaelSolioz.Cometsnap"
$dir = Join-Path $OutRoot "manifests/m/MichaelSolioz/Cometsnap/$version"
New-Item -ItemType Directory -Force $dir | Out-Null
$schema = "1.9.0"
$tmp = Join-Path ([IO.Path]::GetTempPath()) "cometsnap-winget"
New-Item -ItemType Directory -Force $tmp | Out-Null

$installers = foreach ($arch in "x64", "arm64") {
  $url = "https://github.com/$Repo/releases/download/$Tag/Cometsnap-Setup-$arch.exe"
  $file = Join-Path $tmp "Cometsnap-Setup-$arch.exe"
  Invoke-WebRequest $url -OutFile $file
  $sha = (Get-FileHash $file -Algorithm SHA256).Hash
  @"
- Architecture: $arch
  InstallerUrl: $url
  InstallerSha256: $sha
"@
}

$header = "# yaml-language-server: `$schema=https://aka.ms/winget-manifest"

Set-Content -Encoding utf8NoBOM (Join-Path $dir "$id.yaml") @"
$header.version.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
"@

Set-Content -Encoding utf8NoBOM (Join-Path $dir "$id.installer.yaml") @"
$header.installer.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $version
InstallerType: inno
Scope: user
InstallModes:
- interactive
- silent
- silentWithProgress
UpgradeBehavior: install
ProductCode: '{F967EC12-96EC-4899-8998-A59765E250CA}_is1'
ReleaseDate: $(Get-Date -Format yyyy-MM-dd)
Installers:
$($installers -join "`n")
ManifestType: installer
ManifestVersion: $schema
"@

Set-Content -Encoding utf8NoBOM (Join-Path $dir "$id.locale.en-US.yaml") @"
$header.defaultLocale.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $version
PackageLocale: en-US
Publisher: Michael Solioz
PublisherUrl: https://github.com/mike-risky
PublisherSupportUrl: https://github.com/mike-risky/cometsnap/issues
PrivacyUrl: https://github.com/mike-risky/cometsnap/blob/main/PRIVACY.md
Author: Michael Solioz
PackageName: Cometsnap
PackageUrl: https://cometsnap.com
License: MIT
LicenseUrl: https://github.com/mike-risky/cometsnap/blob/main/LICENSE
Copyright: Copyright (c) 2026 Michael Solioz
ShortDescription: A free, fun and snappy screenshot and screen recording tool.
Description: |-
  Press Print Screen and it's instant: smart window detection, a pixel-perfect magnifier,
  annotations, MP4 and GIF recording, and a panel of your recent shots you can drag into any app.
  No paywall, no branding, no telemetry.
Moniker: cometsnap
Tags:
- screenshot
- screen-capture
- screen-recorder
- gif
- annotation
- print-screen
ReleaseNotesUrl: https://github.com/$Repo/releases/tag/$Tag
ManifestType: defaultLocale
ManifestVersion: $schema
"@

Get-ChildItem $dir | ForEach-Object { "--- $($_.Name)"; Get-Content $_.FullName }
"MANIFEST_DIR=$((Resolve-Path $dir).Path)" | Out-File -Append -Encoding utf8 ($env:GITHUB_ENV ?? (Join-Path $tmp "env.txt"))
