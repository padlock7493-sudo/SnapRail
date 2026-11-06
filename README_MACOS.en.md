# SnapRail for macOS

[简体中文](README_MACOS.md) | [English](README_MACOS.en.md)

**Capture it. Hang it up.**

**Version 1.0.0 source for developers.** A native Swift / AppKit / SwiftUI desktop app that carries over the Windows version's clothesline, clips, colors, and core interactions. Includes capture, annotations, floating references, history, on-device OCR, and scrolling capture. No .NET or third-party Swift packages are required. MIT licensed.

## Get the source and build locally

The **macOS 1.0.0 source** is intended for developers. This release does not provide a public Mac DMG or application ZIP. Download `SnapRail-v1.0.0-source-windows-macos.zip` from [GitHub Releases](https://github.com/padlock7493-sudo/SnapRail/releases), or clone the same main branch:

```sh
git clone https://github.com/padlock7493-sudo/SnapRail.git
cd SnapRail
./scripts/build-macos.sh
open release/macos/SnapRail.app
```

Install Apple Command Line Tools with `xcode-select --install`, or use Xcode. No .NET or third-party Swift packages are required. Local testing used **Apple Silicon and macOS 26.5.1**; the deployment target is **macOS 13 or later**.

Local build scripts use ad-hoc signing, without Developer ID signing or Apple notarization. Rebuilding may require renewed permissions. Developers who distribute their own binaries to other Mac users are responsible for signing, notarization, and distribution testing.

SnapRail is a menu-bar app and has no Dock icon. Closing a window does not quit it. Quit through the menu-bar menu or Command+Q.

## Permissions

Enable **SnapRail** in System Settings > Privacy & Security > Screen & System Audio Recording, called Screen Recording on older systems. Quit and reopen the app if macOS requests it.

Automatic scrolling also requires **SnapRail** under Privacy & Security > Accessibility. Without it, scroll manually and use the append-current-frame action. A microphone permission is not required. Complete Touch ID or password verification directly in the system dialog.

An ad-hoc signed update can invalidate the previous permission record even when its switch still appears enabled. If necessary, remove the old SnapRail entry, add the current app again, and quit/reopen it. Settings displays an explicit warning when the running build lacks screen capture permission.

## Quick start

1. Press **Control+Option+A** to capture.
2. Drag a region, click a window, or use Command+A / Control+A to select the screen under the pointer. Drag edges and handles to adjust the selection.
3. Add rectangles, ellipses, arrows, freehand strokes, text, or pixelation.
4. Press Return or the finish-and-hang action to save a PNG, copy the full image, and hang it on the clothesline.
5. Use **Control+Option+T** to expand/collapse the rail, or enable top-edge hover in Settings.
6. Click a hung image to copy it, double-click to preview it, or click its clip to create a floating reference.

The app interface is currently **Chinese**. This English guide explains the same operations; it does not indicate that the application UI has been localized into English.

## Features and controls

| Feature | Behavior |
| --- | --- |
| Clothesline | Curved cords, cartoon clips, welcome cards, up to three favorites, and a history entry for overflow |
| Annotations | Six tools, six colors, stroke width/font size, undo/redo; select objects to move, restyle, or delete them |
| Multiline text | Return inserts a line break; Command+Return finishes text entry; double-click text to edit |
| Ordering and export | Drag along the rail to reorder, Esc to cancel, or drag a PNG to apps that accept file drops |
| Floating references | Always on top; drag the header to move, scroll to resize, Control+scroll to change opacity; return to the rail |
| History | 24 items per page; date/file search; all/favorite/hung/hidden filters; Command/Control multi-select and Shift range select |
| Bulk actions | Hang, pin, favorite, hide, move to the system Trash, and undo the last deletion; deletion requires confirmation |
| Preview | Dark background, fit/actual size/zoom, long-image scrolling, copy, export, and edit a copy; Esc closes |
| OCR | Local Apple Vision Chinese/English recognition, editable and copyable results; proofread small text and punctuation |
| Settings | Shortcut recording, storage migration, target display, login startup, hover, animation, sizes, and delays; save and restart |

Editable copies create a new image and retain the previous version. Reduce Motion disables hanging animations. Failed shortcut registration attempts restore the old binding.

Opening History, Settings, or the plus/more entry collapses the rail. Remaining in the top trigger strip does not restart the swing or immediately reopen a manually collapsed rail; leave the strip before triggering it again.

## Scrolling capture

Select the scrolling body while avoiding fixed headers and floating controls, then use the scrolling-capture action. Choose automatic downward scrolling, or scroll manually and append the current frame. Consecutive frames need overlap. Stop, preview/edit, inspect the result, and save.

Capture stops when the image is unchanged, overlap is unreliable, or a limit is reached: **80 frames, 30,000 pixels in height, or 50 million total pixels**. Previously stitched content is preserved. Animation, repeated patterns, fixed headers, and excessive scrolling can prevent reliable stitching.

## Keyboard reference

| Context | Shortcut | Action |
| --- | --- | --- |
| Desktop | Control+Option+A | Capture; configurable |
| Desktop | Control+Option+T | Expand/collapse; configurable |
| Capture | Return / Esc / right-click | Finish / cancel the current operation |
| Capture | Command+A / Control+A | Select the screen under the pointer |
| Editor | Command+Z / Command+Shift+Z | Undo / redo; corresponding Control editing shortcuts are also accepted |
| Editor | Command+S / Control+S | Export |
| Selected object | Delete / arrow keys | Delete / nudge |
| Text entry | Return / Command+Return | New line / finish |
| Preview or OCR | Esc | Close |

## Storage and privacy

Screenshots, annotations, and OCR stay on the local machine. There are no accounts, cloud sync, or automatic image uploads. Explicit copy/export/drag operations pass data to the clipboard, chosen location, or target app.

New configurations default to **`SnapRail-data/` beside `SnapRail.app`**, with screenshots in its `images/` directory. For example, `~/Applications/SnapRail.app` uses `~/Applications/SnapRail-data/images/`.

A non-writable parent directory, such as a read-only DMG, or App Translocation falls back to `~/Library/Application Support/SnapRail/`. Existing custom paths are retained. Settings remain in `~/Library/Application Support/SnapRail/settings.json`; moving the app does not silently move existing data.

```text
SnapRail-data/
├── images/             Completed PNG files
├── documents/          Editable original PNGs and annotation JSON
├── library.json        History, favorites, ordering, and floating-window placement
├── library.json.bak    Previous valid library manifest, when available
├── recovery.json       Most recent Trash recovery mapping
├── welcome-seeded      One-time welcome-card marker
└── app.lock            Single-instance lock
```

Settings can migrate data into an empty directory using staging and per-file SHA-256 verification. Nested directories and symbolic links are rejected. Original data is retained. Do not run two copies against the same data at once.

A damaged manifest can be recovered from a valid backup. If neither is readable, protection mode blocks writes and welcome-card reseeding. Back up the entire directory before recovery. Hiding an image does not delete it. Emptying the system Trash can make deletion recovery impossible.

## Platform differences

The clothesline, clips, toolbar, history, and settings follow the Windows design. Titles, file dialogs, permissions, Trash, display identifiers, login items, and fonts use macOS mechanisms and will not be pixel-identical.

Windows portable-library JSON and annotation JSON cannot directly replace the Mac library. PNG import is supported; documents created by the Mac app remain editable. Windows source stays in `src/`.

## Build, test, and package

Install Apple Command Line Tools with `xcode-select --install`, or use Xcode:

```sh
./scripts/test-macos.sh
./scripts/build-macos.sh
open release/macos/SnapRail.app
./scripts/package-macos.sh --skip-build
```

The build targets the host architecture. Regression tests compile separately and are not shipped in the app; XCTest and full Xcode are not required. Optional local test packages are written to `release/macos/` and contain the app, both language guides, and license, without user history, test profiles, or build caches.

```sh
open -n release/macos/SnapRail.app --args --profile /tmp/SnapRail-review
release/macos/SnapRail.app/Contents/MacOS/SnapRail --diagnostics
# Demo capture requires an isolated profile and does not capture the desktop:
open -n release/macos/SnapRail.app --args --profile /tmp/SnapRail-demo --demo-capture
```

Lifecycle, menus, and shortcuts are in `App*.swift`; capture and editing in `Capture.swift`, `EditorController.swift`, and `Annotation*.swift`; rail/history/preview in their named files; storage in `Library.swift`, `Models.swift`, and `DataMigration.swift`; configuration in `Configuration.swift`, `SettingsView.swift`, and `ShortcutRecorder.swift`; scrolling in `ScrollCapture.swift` and `ScrollStitcher.swift`. Tests and local fixtures are under `macos/Tests/`.

## Version 1.0.0 and verification

Includes one activation per hover visit, automatic rail collapse when opening navigation windows, native Mac key names, app-adjacent data defaults, and explicit permission warnings.

The latest functional regression run passed **29/29 groups**. Real local Chrome tests covered capture, Chinese/English OCR, automatic scrolling, stop, preview/edit, and save. An isolated 9-frame capture produced **2150×3614 pixels**. A normal-launch scrolling run recorded 26 frames and 2150×10300 pixels while running. Long images are downscaled for OCR and small text can contain substantial errors; use shorter sections when needed.

Detailed records are in `docs/MACOS_VERIFICATION.md` in the source repository. Older-system hardware tests, physical multi-display/mixed scaling, hot-plugging, login-after-logout, and complete cross-app drag compatibility remain outside the verified scope. Other Macs must grant their own permissions.

## Feedback and license

Use [GitHub Issues](https://github.com/padlock7493-sudo/SnapRail/issues), including version, macOS, chip, display/scaling, and reproduction steps. Check screenshots for private data before sharing.

MIT License. Existing repository artwork is reused; system fonts are not redistributed.
