#!/usr/bin/env bash
# A throw-away X11 desktop for testing the Linux build without touching your session:
# Xvfb + openbox (window manager) + xcompmgr (compositor, so transparency works) + a file manager.
#
#   tools/x11-sandbox.sh [folder]          # then: DISPLAY=:99 convertino
#   DISPLAY=:99 xdotool …                  # drive it; DISPLAY=:99 import -window root shot.png to look
#
# Needs: xvfb openbox xcompmgr hsetroot pcmanfm xdotool imagemagick  (apt install …)
set -euo pipefail
DIR="${1:-$HOME}"
unset WAYLAND_DISPLAY
pkill -f "Xvfb :99" 2>/dev/null || true; sleep 0.5
setsid Xvfb :99 -screen 0 1600x1000x24 +extension Composite +extension RANDR >/tmp/xvfb.log 2>&1 &
sleep 1.5
export DISPLAY=:99
hsetroot -add "#ff9f45" -add "#e8436b" -add "#8a6cf0" -gradient 135 >/dev/null 2>&1 || true
setsid openbox >/dev/null 2>&1 &
sleep 0.8
setsid xcompmgr -c -r 14 -o 0.35 -l -16 -t -12 >/dev/null 2>&1 &
GDK_BACKEND=x11 setsid pcmanfm --no-desktop "$DIR" >/dev/null 2>&1 &
echo "X11 sandbox on DISPLAY=:99 — run: DISPLAY=:99 CONVERTINO_DEBUG=1 convertino"
