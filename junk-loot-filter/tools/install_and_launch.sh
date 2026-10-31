#!/bin/bash
# Installs the built mod into DFU's Mods folder (inside the app bundle), launches DFU, waits, and prints the
# mod-related log lines. Leaves DFU running; use quit_dfu.sh to stop it.
set -e
DATA="$HOME/Library/Application Support/unity.Daggerfall Workshop.Daggerfall Unity"
HERE="$(cd "$(dirname "$0")/.." && pwd)"
APP_MODS="$HERE/../../DaggerfallUnity/DaggerfallUnity.app/Contents/Resources/Data/StreamingAssets/Mods"
WAIT="${1:-25}"
pkill -f "DaggerfallUnity.app/Contents/MacOS" 2>/dev/null || true
sleep 1
cp "$HERE/build/junk-loot-filter.dfmod" "$APP_MODS/junk-loot-filter.dfmod"
open "/Applications/Daggerfall Unity.app"
sleep 2
osascript -e 'tell application "Daggerfall Unity" to activate' 2>/dev/null || true
sleep "$WAIT"
grep -n -i -E "junkloot|junk and loot|ModManager|CS[0-9]{4}|\[error\]|exception" "$DATA/Player.log" | grep -v "breton" | head -60
