#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUTPUT="$ROOT/release/macos"
APP="$OUTPUT/SnapRail.app"
case "${1:-}" in
    '') "$ROOT/scripts/build-macos.sh" ;;
    --skip-build) ;;
    *) printf 'Usage: %s [--skip-build]\n' "$0" >&2; exit 2 ;;
esac
codesign --verify --deep --strict "$APP"
VERSION=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$APP/Contents/Info.plist")
ARCH=$(lipo -archs "$APP/Contents/MacOS/SnapRail")
case "$ARCH" in arm64|x86_64) ;; *) printf 'Unsupported package architecture: %s\n' "$ARCH" >&2; exit 1 ;; esac
NAME="SnapRail-v$VERSION-macos-$ARCH"
STAGE=$(mktemp -d "$OUTPUT/.package.XXXXXX")
trap 'rm -rf "$STAGE"' EXIT
mkdir -p "$STAGE/$NAME"
ditto "$APP" "$STAGE/$NAME/SnapRail.app"
cp "$ROOT/README_MACOS.md" "$STAGE/$NAME/README_MACOS.md"
cp "$ROOT/README_MACOS.en.md" "$STAGE/$NAME/README_MACOS.en.md"
cp "$ROOT/LICENSE" "$STAGE/$NAME/LICENSE"
ln -s /Applications "$STAGE/$NAME/Applications"
ditto -c -k --sequesterRsrc --keepParent "$STAGE/$NAME" "$OUTPUT/$NAME.zip"
hdiutil create -volname "SnapRail $VERSION" -srcfolder "$STAGE/$NAME" -ov -format UDZO "$OUTPUT/$NAME.dmg"
hdiutil verify "$OUTPUT/$NAME.dmg"
cp "$ROOT/README_MACOS.md" "$OUTPUT/README_MACOS.md"
cp "$ROOT/README_MACOS.en.md" "$OUTPUT/README_MACOS.en.md"
(
    cd "$OUTPUT"
    shasum -a 256 "$NAME.zip" "$NAME.dmg" README_MACOS.md README_MACOS.en.md > SHA256SUMS.txt
)
printf 'Packaged: %s/{%s.zip,%s.dmg,README_MACOS.md,SHA256SUMS.txt}\n' "$OUTPUT" "$NAME" "$NAME"
