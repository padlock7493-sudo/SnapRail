#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUTPUT="$ROOT/macos/.build/regression"
mkdir -p "$OUTPUT"
SOURCES=()
for SOURCE in "$ROOT/macos/Sources/SnapRail/"*.swift; do
    case "$SOURCE" in */main.swift) continue ;; esac
    SOURCES+=("$SOURCE")
done
# A separate test executable keeps test code out of the app and works with
# Command Line Tools alone; the XCTest framework requires full Xcode on macOS.
xcrun swiftc -swift-version 5 -target "$(uname -m)-apple-macosx13.0" -O -module-name SnapRailRegression \
    "${SOURCES[@]}" \
    "$ROOT/macos/Tests/SnapRailTests/LegacyRegressionTests.swift" \
    "$ROOT/macos/Tests/SnapRailTests/RegressionSuite.swift" \
    "$ROOT/macos/Tests/SnapRailTests/main.swift" \
    -framework AppKit -framework SwiftUI -framework Carbon -framework Vision \
    -framework ScreenCaptureKit -framework ServiceManagement \
    -o "$OUTPUT/SnapRailRegression"
"$OUTPUT/SnapRailRegression"
