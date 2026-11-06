#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP="$ROOT/release/macos/SnapRail.app"
export CLANG_MODULE_CACHE_PATH="$ROOT/macos/.build/ModuleCache"
swift build --package-path "$ROOT/macos" -c release
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$ROOT/macos/.build/release/SnapRail" "$APP/Contents/MacOS/SnapRail"
cp "$ROOT/macos/Resources/Info.plist" "$APP/Contents/Info.plist"
ICONSET="$ROOT/macos/.build/AppIcon.iconset"
mkdir -p "$ICONSET"
for SIZE in 16 32 128 256 512; do
    sips -z "$SIZE" "$SIZE" "$ROOT/src/SnapRail.App/Assets/SnapRail-mist-blue-v1.png" --out "$ICONSET/icon_${SIZE}x${SIZE}.png" >/dev/null
    DOUBLE=$((SIZE * 2))
    sips -z "$DOUBLE" "$DOUBLE" "$ROOT/src/SnapRail.App/Assets/SnapRail-mist-blue-v1.png" --out "$ICONSET/icon_${SIZE}x${SIZE}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
# Local ad-hoc signature; Developer ID notarization is a separate distribution step.
codesign --force --deep --sign - --identifier com.snaprail.macos "$APP"
printf 'Built: %s\n' "$APP"
