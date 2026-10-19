#!/usr/bin/env bash
# Build Convertino.app for one Mac architecture (needs macOS: sips, iconutil, codesign).
#   packaging/macos/build-app.sh arm64|x64 [out-dir]     → <out-dir>/Convertino.app (default dist/macos-<arch>)
set -euo pipefail

ARCH="${1:-arm64}"
case "$ARCH" in arm64|x64) ;; *) echo "usage: $0 arm64|x64 [out-dir]"; exit 2;; esac
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="${2:-$ROOT/dist/macos-$ARCH}"
APP="$OUT/Convertino.app"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/src/Directory.Build.props" | head -1)"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
dotnet publish "$ROOT/src/Convertino.Mac/Convertino.Mac.csproj" -c Release -r "osx-$ARCH" --self-contained true \
  -o "$APP/Contents/MacOS" -v q -nologo
rm -f "$APP/Contents/MacOS/"*.pdb
chmod +x "$APP/Contents/MacOS/convertino"

sed "s/@VERSION@/$VERSION/g" "$ROOT/packaging/macos/Info.plist" > "$APP/Contents/Info.plist"
printf 'APPL????' > "$APP/Contents/PkgInfo"

# icon: every size the .icns wants, from the 512 px master
ICONSET="$(mktemp -d)/convertino.iconset"
mkdir -p "$ICONSET"
SRC="$ROOT/packaging/linux/icons/convertino-512.png"
for s in 16 32 128 256 512; do
  sips -z "$s" "$s" "$SRC" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  d=$((s * 2))
  sips -z "$d" "$d" "$SRC" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/convertino.icns"
rm -rf "$(dirname "$ICONSET")"

# Ad-hoc signature (no Apple Developer ID): every file in MacOS/ (codesign wants the managed .dlls signed too),
# then the bundle, which signs the main executable and seals Info.plist, so macOS keeps one identity for Convertino
# in Privacy & Security. Gatekeeper still treats it as unsigned — see the README.
find "$APP/Contents/MacOS" -type f ! -name convertino -print0 | xargs -0 codesign --force --sign - --timestamp=none
codesign --force --sign - --timestamp=none "$APP"
codesign --verify --strict --verbose=1 "$APP"

echo "$APP ($VERSION, $ARCH)"
