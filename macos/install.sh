#!/bin/bash
# Installs (or updates) Cometsnap for macOS without the Gatekeeper detour:
#   curl -fsSL https://raw.githubusercontent.com/mike-risky/cometsnap/main/macos/install.sh | bash
# Downloads the latest build from GitHub Releases, puts it in Applications and opens it.
set -euo pipefail

URL="https://github.com/mike-risky/cometsnap/releases/download/macos-nightly/Cometsnap-macOS.zip"
DEST="/Applications"
[ -w "$DEST" ] || DEST="$HOME/Applications"
mkdir -p "$DEST"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

echo "☄️  Downloading Cometsnap…"
curl -fL --progress-bar "$URL" -o "$TMP/Cometsnap.zip"
ditto -x -k "$TMP/Cometsnap.zip" "$TMP"
[ -d "$TMP/Cometsnap.app" ] || { echo "The download did not contain Cometsnap.app." >&2; exit 1; }

if pgrep -xq Cometsnap; then
  echo "Closing the running Cometsnap…"
  osascript -e 'tell application id "com.cometsnap.mac" to quit' >/dev/null 2>&1 || pkill -x Cometsnap || true
  sleep 1
fi

rm -rf "$DEST/Cometsnap.app"
ditto "$TMP/Cometsnap.app" "$DEST/Cometsnap.app"
xattr -dr com.apple.quarantine "$DEST/Cometsnap.app" 2>/dev/null || true

echo "✓ Installed in $DEST. Opening Cometsnap…"
open "$DEST/Cometsnap.app"
