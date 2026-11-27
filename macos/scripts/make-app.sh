#!/bin/bash
# Builds Cometsnap.app (universal: Apple Silicon + Intel), then a .dmg and a .zip.
# Usage: scripts/make-app.sh <version> [build-number]
# Runs on macOS with Xcode command line tools (GitHub Actions macos runner).
set -euo pipefail

VERSION="${1:-0.1.0}"
BUILD="${2:-1}"
HERE="$(cd "$(dirname "$0")/.." && pwd)"
REPO="$(cd "$HERE/.." && pwd)"
OUT="$HERE/build"
APP="$OUT/Cometsnap.app"

cd "$HERE"
rm -rf "$OUT"
mkdir -p "$OUT"

echo "==> swift build (arm64 + x86_64)"
swift build -c release --arch arm64 --arch x86_64 2>&1 | tee "$OUT/build.log"
BIN="$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)/Cometsnap"
lipo -info "$BIN"

echo "==> assemble bundle"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/Fonts"
cp "$BIN" "$APP/Contents/MacOS/Cometsnap"
cp "$REPO/assets/fonts/"*.ttf "$APP/Contents/Resources/Fonts/"
cp "$REPO/assets/fonts/OFL.txt" "$APP/Contents/Resources/Fonts/"
for s in 20 40 256; do cp "Resources/cometsnap-$s.png" "$APP/Contents/Resources/"; done

ICONSET="$OUT/AppIcon.iconset"
mkdir -p "$ICONSET"
cp Resources/cometsnap-16.png   "$ICONSET/icon_16x16.png"
cp Resources/cometsnap-32.png   "$ICONSET/icon_16x16@2x.png"
cp Resources/cometsnap-32.png   "$ICONSET/icon_32x32.png"
cp Resources/cometsnap-64.png   "$ICONSET/icon_32x32@2x.png"
cp Resources/cometsnap-128.png  "$ICONSET/icon_128x128.png"
cp Resources/cometsnap-256.png  "$ICONSET/icon_128x128@2x.png"
cp Resources/cometsnap-256.png  "$ICONSET/icon_256x256.png"
cp Resources/cometsnap-512.png  "$ICONSET/icon_256x256@2x.png"
cp Resources/cometsnap-512.png  "$ICONSET/icon_512x512.png"
cp Resources/cometsnap-1024.png "$ICONSET/icon_512x512@2x.png"
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"

sed -e "s/__VERSION__/$VERSION/g" -e "s/__BUILD__/$BUILD/g" Resources/Info.plist > "$APP/Contents/Info.plist"
plutil -lint "$APP/Contents/Info.plist"
printf 'APPL????' > "$APP/Contents/PkgInfo"

# Signing. With a Developer ID certificate (SIGN_IDENTITY, imported by CI from secrets) the app
# gets the hardened runtime and a secure timestamp, ready for notarization. A self-signed
# certificate works the same way minus notarization: macOS then keeps the screen permission
# across updates. Without either, an ad-hoc signature.
if [ -n "${SIGN_IDENTITY:-}" ]; then
  echo "==> sign with \"$SIGN_IDENTITY\""
  if [[ "$SIGN_IDENTITY" == "Developer ID Application"* ]]; then
    codesign --force --options runtime --timestamp --sign "$SIGN_IDENTITY" "$APP"
  else
    codesign --force --sign "$SIGN_IDENTITY" --timestamp=none "$APP"
  fi
else
  echo "==> ad-hoc sign"
  codesign --force --deep --sign - --timestamp=none "$APP"
fi
codesign --verify --verbose=2 "$APP"

# Notarization (needs a Developer ID signature and an App Store Connect API key).
notarize() {
  [[ "${SIGN_IDENTITY:-}" == "Developer ID Application"* && -n "${NOTARY_KEY_PATH:-}" ]] || return 0
  echo "==> notarize $(basename "$1")"
  xcrun notarytool submit "$1" --key "$NOTARY_KEY_PATH" --key-id "$NOTARY_KEY_ID" --issuer "$NOTARY_ISSUER" --wait
}
if [[ "${SIGN_IDENTITY:-}" == "Developer ID Application"* && -n "${NOTARY_KEY_PATH:-}" ]]; then
  ( cd "$OUT" && ditto -c -k --keepParent Cometsnap.app notarize.zip )
  notarize "$OUT/notarize.zip"
  xcrun stapler staple "$APP"
  rm -f "$OUT/notarize.zip"
fi

echo "==> zip"
( cd "$OUT" && ditto -c -k --keepParent Cometsnap.app Cometsnap-macOS.zip )

echo "==> dmg"
DMGROOT="$OUT/dmg"
mkdir -p "$DMGROOT"
cp -R "$APP" "$DMGROOT/"
ln -s /Applications "$DMGROOT/Applications"
cp "$HERE/FIRST-LAUNCH.txt" "$DMGROOT/Read me first.txt"
hdiutil create -volname "Cometsnap" -srcfolder "$DMGROOT" -ov -format UDZO "$OUT/Cometsnap-macOS.dmg"
if [ -n "${SIGN_IDENTITY:-}" ]; then codesign --force --sign "$SIGN_IDENTITY" "$OUT/Cometsnap-macOS.dmg"; fi
if [[ "${SIGN_IDENTITY:-}" == "Developer ID Application"* && -n "${NOTARY_KEY_PATH:-}" ]]; then
  notarize "$OUT/Cometsnap-macOS.dmg"
  xcrun stapler staple "$OUT/Cometsnap-macOS.dmg"
fi

ls -la "$OUT"
