#!/bin/sh
# Starts Cometsnap from its install folder (AppImage: AppRun, .deb: /usr/bin/cometsnap).
HERE="$(dirname "$(readlink -f "$0")")"
APPDIR="${COMETSNAP_DIR:-$HERE/usr/lib/cometsnap}"
[ -x "$APPDIR/cometsnap" ] || APPDIR="/usr/lib/cometsnap"

# .NET needs ICU for culture data; fall back to invariant mode on systems without it.
if ! ldconfig -p 2>/dev/null | grep -q 'libicuuc\.so'; then
  export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
fi

exec "$APPDIR/cometsnap" "$@"
