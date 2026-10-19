#!/usr/bin/env bash
# Convertino installer for Linux — per-user, no root needed.
#   ./install.sh            install from this folder (release tarball) or build from source
#   ./install.sh --uninstall
set -euo pipefail

APP_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/convertino"
BIN_DIR="$HOME/.local/bin"
APPS_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
ICON_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

say() { printf '\033[1;35m✿\033[0m %s\n' "$*"; }

integrations() {
  local action="$1"   # install | remove
  local nautilus="$HOME/.local/share/nautilus/scripts/Convertino"
  local nemo="$HOME/.local/share/nemo/actions/convertino.nemo_action"
  local dolphin="$HOME/.local/share/kio/servicemenus/convertino.desktop"
  local caja="$HOME/.config/caja/scripts/Convertino"
  if [ "$action" = remove ]; then rm -f "$nautilus" "$nemo" "$dolphin" "$caja"; return; fi
  mkdir -p "$(dirname "$nautilus")" "$(dirname "$nemo")" "$(dirname "$dolphin")"
  cat > "$nautilus" <<'S'
#!/bin/sh
# Nautilus → right-click → Scripts → Convertino
IFS='
'
exec convertino menu $NAUTILUS_SCRIPT_SELECTED_FILE_PATHS
S
  chmod +x "$nautilus"
  [ -d "$HOME/.config/caja" ] && { mkdir -p "$(dirname "$caja")"; cp "$nautilus" "$caja"; sed -i 's/NAUTILUS_SCRIPT/CAJA_SCRIPT/' "$caja"; }
  cat > "$nemo" <<S
[Nemo Action]
Name=Convertino
Comment=Convert or edit with Convertino
Exec=$BIN_DIR/convertino menu %F
Icon-Name=convertino
Selection=notnone
Extensions=any;
S
  cat > "$dolphin" <<S
[Desktop Entry]
Type=Service
MimeType=all/allfiles;inode/directory;
Actions=convertino
X-KDE-Priority=TopLevel

[Desktop Action convertino]
Name=Convertino…
Icon=convertino
Exec=$BIN_DIR/convertino menu %F
S
  chmod +x "$dolphin"
}

if [ "${1:-}" = "--uninstall" ]; then
  pkill -f "$APP_DIR/convertino" 2>/dev/null || true
  rm -rf "$APP_DIR" "$BIN_DIR/convertino" "$APPS_DIR/convertino.desktop" "$HOME/.config/autostart/convertino.desktop"
  find "$ICON_DIR" -name 'convertino.png' -delete 2>/dev/null || true
  integrations remove
  say "Convertino removed (settings kept in ~/.config/convertino)."
  exit 0
fi

# 1. get the binaries: a release tarball has ./app, a source checkout gets built
if [ -x "$HERE/app/convertino" ]; then
  SRC="$HERE/app"
elif [ -f "$HERE/../../src/Convertino.Linux/Convertino.Linux.csproj" ]; then
  command -v dotnet >/dev/null || { echo "Building from source needs the .NET 10 SDK (https://dot.net)."; exit 1; }
  say "Building Convertino…"
  dotnet publish "$HERE/../../src/Convertino.Linux/Convertino.Linux.csproj" -c Release -r linux-x64 --self-contained true -o "$HERE/../../dist/linux-x64" -v q -nologo
  SRC="$HERE/../../dist/linux-x64"
else
  echo "Can't find Convertino binaries next to this script."; exit 1
fi

pkill -f "$APP_DIR/convertino" 2>/dev/null || true
say "Installing to $APP_DIR"
rm -rf "$APP_DIR"; mkdir -p "$APP_DIR" "$BIN_DIR" "$APPS_DIR"
cp -r "$SRC/." "$APP_DIR/"
chmod +x "$APP_DIR/convertino"
ln -sf "$APP_DIR/convertino" "$BIN_DIR/convertino"

for s in 64 128 256 512; do
  if [ -f "$HERE/icons/convertino-$s.png" ]; then
    mkdir -p "$ICON_DIR/${s}x${s}/apps"
    cp "$HERE/icons/convertino-$s.png" "$ICON_DIR/${s}x${s}/apps/convertino.png"
  fi
done
cp "$HERE/convertino.desktop" "$APPS_DIR/convertino.desktop"
sed -i "s|^Exec=convertino|Exec=$BIN_DIR/convertino|" "$APPS_DIR/convertino.desktop"
command -v update-desktop-database >/dev/null && update-desktop-database "$APPS_DIR" >/dev/null 2>&1 || true
command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -q "$ICON_DIR" >/dev/null 2>&1 || true
integrations install

# 2. optional helpers
missing=()
for t in ffmpeg:ffmpeg pdftoppm:poppler-utils tesseract:tesseract-ocr soffice:libreoffice; do
  command -v "${t%%:*}" >/dev/null || missing+=("${t##*:}")
done
if [ ${#missing[@]} -gt 0 ]; then
  say "Optional extras not found: ${missing[*]}"
  echo "   Debian/Ubuntu: sudo apt install ${missing[*]}"
  fedora="${missing[*]}"; fedora="${fedora/tesseract-ocr/tesseract}"
  arch="${missing[*]}"; arch="${arch/poppler-utils/poppler}"; arch="${arch/tesseract-ocr/tesseract tesseract-data-eng}"; arch="${arch/libreoffice/libreoffice-fresh}"
  echo "   Fedora:        sudo dnf install $fedora"
  echo "   Arch:          sudo pacman -S $arch"
fi

case ":$PATH:" in *":$BIN_DIR:"*) ;; *) say "Add $BIN_DIR to your PATH to use the 'convertino' command.";; esac
if [ "${XDG_SESSION_TYPE:-}" = "wayland" ]; then
  say "Wayland session: drag + Shift works over X11 (XWayland) apps; right-click files → Convertino works everywhere."
fi
nohup "$APP_DIR/convertino" >/dev/null 2>&1 &
say "Convertino is running — drag any file and tap Shift."
