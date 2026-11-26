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

echo "==> ad-hoc sign"
codesign --force --deep --sign - --timestamp=none "$APP"
codesign --verify --verbose=2 "$APP"

echo "==> zip"
( cd "$OUT" && ditto -c -k --keepParent Cometsnap.app Cometsnap-macOS.zip )

echo "==> dmg"
DMGROOT="$OUT/dmg"
mkdir -p "$DMGROOT"
cp -R "$APP" "$DMGROOT/"
ln -s /Applications "$DMGROOT/Applications"
cp "$HERE/FIRST-LAUNCH.txt" "$DMGROOT/Read me first.txt"
hdiutil create -volname "Cometsnap" -srcfolder "$DMGROOT" -ov -format UDZO "$OUT/Cometsnap-macOS.dmg"

ls -la "$OUT"
