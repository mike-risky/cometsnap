#!/usr/bin/env bash
# Builds the Linux packages: Cometsnap-<arch>.AppImage and cometsnap_<debarch>.deb.
#   packaging/package.sh x86_64|aarch64 [version]
# Needs the .NET 8 SDK, curl, xz, dpkg-deb. Output lands in linux/build/.
set -euo pipefail

ARCH="${1:?arch: x86_64 or aarch64}"
VERSION="${2:-0.1.0}"
case "$ARCH" in
  x86_64)  RID=linux-x64;   DEBARCH=amd64; FFMPEG=linux64 ;;
  aarch64) RID=linux-arm64; DEBARCH=arm64; FFMPEG=linuxarm64 ;;
  *) echo "unknown arch $ARCH" >&2; exit 1 ;;
esac

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PKG="$ROOT/packaging"
OUT="$ROOT/build"
WORK="$OUT/work-$ARCH"
rm -rf "$WORK"; mkdir -p "$WORK" "$OUT"

echo "== publish $RID"
dotnet publish "$ROOT/src/Cometsnap.Linux/Cometsnap.Linux.csproj" -c Release -r "$RID" \
  -p:Version="$VERSION" -o "$WORK/app"
mkdir -p "$WORK/app/icons"
cp "$PKG"/icons/cometsnap-*.png "$WORK/app/icons/"
rm -f "$WORK/app"/*.pdb

echo "== static ffmpeg ($FFMPEG)"
mkdir -p "$WORK/ff"
curl -fsSL --retry 4 -o "$WORK/ff/ffmpeg.tar.xz" \
  "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-$FFMPEG-gpl.tar.xz"
tar -xJf "$WORK/ff/ffmpeg.tar.xz" -C "$WORK/ff" --wildcards '*/bin/ffmpeg'
mkdir -p "$WORK/app/libexec"
install -m 755 "$(find "$WORK/ff" -path '*/bin/ffmpeg' -type f | head -1)" "$WORK/app/libexec/ffmpeg"
rm -rf "$WORK/ff"

# ---------------------------------------------------------------- AppImage
echo "== AppImage"
APPDIR="$WORK/Cometsnap.AppDir"
mkdir -p "$APPDIR/usr/lib" "$APPDIR/usr/share/applications"
cp -a "$WORK/app" "$APPDIR/usr/lib/cometsnap"
install -m 755 "$PKG/launcher.sh" "$APPDIR/AppRun"
cp "$PKG/cometsnap.desktop" "$APPDIR/cometsnap.desktop"
cp "$PKG/cometsnap.desktop" "$APPDIR/usr/share/applications/cometsnap.desktop"
for s in 16 24 32 48 64 128 256 512; do
  mkdir -p "$APPDIR/usr/share/icons/hicolor/${s}x${s}/apps"
  cp "$PKG/icons/cometsnap-$s.png" "$APPDIR/usr/share/icons/hicolor/${s}x${s}/apps/cometsnap.png"
done
cp "$PKG/icons/cometsnap-256.png" "$APPDIR/cometsnap.png"
ln -sf cometsnap.png "$APPDIR/.DirIcon"

TOOL="$OUT/appimagetool-$(uname -m).AppImage"
if [ ! -x "$TOOL" ]; then
  curl -fsSL --retry 4 -o "$TOOL" \
    "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-$(uname -m).AppImage"
  chmod +x "$TOOL"
fi
RUNTIME="$OUT/runtime-$ARCH"
if [ ! -f "$RUNTIME" ]; then
  curl -fsSL --retry 4 -o "$RUNTIME" "https://github.com/AppImage/type2-runtime/releases/download/continuous/runtime-$ARCH"
fi
ARCH="$ARCH" APPIMAGE_EXTRACT_AND_RUN=1 "$TOOL" --no-appstream --runtime-file "$RUNTIME" \
  "$APPDIR" "$OUT/Cometsnap-$ARCH.AppImage"

# ---------------------------------------------------------------- .deb
echo "== .deb"
DEB="$WORK/deb"
mkdir -p "$DEB/DEBIAN" "$DEB/usr/lib" "$DEB/usr/bin" "$DEB/usr/share/applications"
cp -a "$WORK/app" "$DEB/usr/lib/cometsnap"
install -m 755 "$PKG/launcher.sh" "$DEB/usr/bin/cometsnap"
cp "$PKG/cometsnap.desktop" "$DEB/usr/share/applications/cometsnap.desktop"
for s in 16 24 32 48 64 128 256 512; do
  mkdir -p "$DEB/usr/share/icons/hicolor/${s}x${s}/apps"
  cp "$PKG/icons/cometsnap-$s.png" "$DEB/usr/share/icons/hicolor/${s}x${s}/apps/cometsnap.png"
done
mkdir -p "$DEB/usr/share/icons/hicolor/scalable/apps"
cp "$PKG/icons/cometsnap.svg" "$DEB/usr/share/icons/hicolor/scalable/apps/cometsnap.svg"
SIZE=$(du -sk "$DEB/usr" | cut -f1)
cat > "$DEB/DEBIAN/control" <<CONTROL
Package: cometsnap
Version: $VERSION
Section: graphics
Priority: optional
Architecture: $DEBARCH
Installed-Size: $SIZE
Maintainer: Michael Solioz <260403229+mike-risky@users.noreply.github.com>
Homepage: https://github.com/mike-risky/cometsnap
Depends: libx11-6, libice6, libsm6, libfontconfig1, libc6
Recommends: xdg-desktop-portal, gstreamer1.0-pipewire, wl-clipboard, pulseaudio-utils | pipewire-bin
Description: Snappy screenshots, annotations and screen recordings
 Cometsnap lives in the tray. Press Print Screen, pick a window or drag an
 area, and the picture is copied and saved at once. Edit it later with arrows,
 boxes, text, blur and more, or record the screen as an MP4 or a GIF.
 Works on X11 and Wayland (through the desktop portal).
CONTROL
cat > "$DEB/DEBIAN/postinst" <<'POST'
#!/bin/sh
set -e
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q /usr/share/applications || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -f /usr/share/icons/hicolor || true
POST
chmod 755 "$DEB/DEBIAN/postinst"
dpkg-deb --root-owner-group --build "$DEB" "$OUT/cometsnap_$DEBARCH.deb"

rm -rf "$WORK"
ls -la "$OUT"
