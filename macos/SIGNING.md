# Signing and notarizing the Mac app

The macOS workflow signs and notarizes automatically once these repository secrets exist
(GitHub › Settings › Secrets and variables › Actions › New repository secret). Without them,
builds stay ad-hoc signed, exactly as today.

| Secret | What it is |
|---|---|
| `MACOS_CERT_P12` | The **Developer ID Application** certificate with its private key, exported as .p12, then base64: `base64 -i cert.p12 \| pbcopy` |
| `MACOS_CERT_PASSWORD` | The password chosen when exporting the .p12 |
| `NOTARY_KEY_P8` | An App Store Connect API key (.p8), base64: `base64 -i AuthKey_XXXX.p8 \| pbcopy` |
| `NOTARY_KEY_ID` | That key's ID (10 characters) |
| `NOTARY_ISSUER` | The Issuer ID shown above the keys list (a UUID) |

## One-time setup (about 30 minutes, once the Apple account is approved)

1. Join the Apple Developer Program at developer.apple.com/programs (99 USD a year, as an individual).
   Approval usually takes a day or two.
2. On a Mac, in Xcode › Settings › Accounts, sign in with that Apple ID, select the team, click
   **Manage Certificates…**, then **+ › Developer ID Application**.
3. Open Keychain Access › My Certificates, right-click "Developer ID Application: Michael Solioz
   (TEAMID)" › Export… › .p12 with a password. Add it as `MACOS_CERT_P12` and `MACOS_CERT_PASSWORD`.
4. In App Store Connect › Users and Access › Integrations › App Store Connect API, create a key with
   the **Developer** role, download the .p8 (only possible once), and add `NOTARY_KEY_P8`,
   `NOTARY_KEY_ID` and `NOTARY_ISSUER`.

The next build on `main` is then signed with the hardened runtime, notarized by Apple and stapled:
the DMG opens with no warnings, and macOS keeps the screen permission across updates.

## Free in-between option

A self-signed code signing certificate in `MACOS_CERT_P12` (without the notary secrets) keeps the
same signature from build to build, so macOS stops asking for screen access again after each update.
The first-launch Gatekeeper step stays.
