# SnapRail for Windows

[简体中文](README_WINDOWS.md) | [English](README_WINDOWS.en.md)

**Windows 1.0.0 stable · x64 · portable · MIT licensed**

See the [project overview](README.en.md) or the [macOS guide](README_MACOS.en.md) for the other platform. The Windows app interface is currently Chinese; this is its English usage and build guide.

## Download and start

Download `SnapRail-v1.0.0-Portable-win-x64.zip` from [GitHub Releases](https://github.com/padlock7493-sudo/SnapRail/releases), extract the entire archive into a writable directory, and run `SnapRail.exe`. A separate .NET installation is not needed.

1. Press **Ctrl+Alt+A**, drag a region, or click a window.
2. Add rectangles, arrows, text, or other annotations.
3. Press Enter or finish-and-hang to save, copy, and hang the image.
4. Use **Ctrl+Alt+T**, or hover near the top center, to expand/collapse the rail.
5. Click an image to copy it, double-click to preview, or drag along the clothesline to reorder.

Closing the rail does not quit the app. Quit through the SnapRail system-tray menu.

| File | Purpose |
| --- | --- |
| `SnapRail-v1.0.0-Portable-win-x64.zip` | Portable Windows application |
| `SnapRail-v1.0.0-source-windows-macos.zip` | Both implementations, bilingual guides, and development scripts |
| `SHA256SUMS.txt` | Download integrity checks |

The shared source archive contains Windows application source and macOS developer source.

## System requirements

Designed for Windows x64 desktop environments and primarily tested on Windows 11 x64. Windows 10 x64 has not received complete compatibility testing. The portable package includes its .NET runtime.

Multiple monitors, negative coordinates, and mixed scaling are supported; the Windows verification included a 150% primary display and 125% secondary display. Keep `SnapRail.portable` beside the executable and use a writable directory.

## Capture and annotation

Region, window, full-screen, and cross-screen capture are supported. Resize using selection edges/handles and nudge with arrow keys.

Tools include rectangles, ellipses, arrows, freehand drawing, text, and pixelation. The object tool selects existing marks for movement, color/stroke/font changes, or deletion. Double-click text to edit it. Multiline Chinese text, undo, and redo are supported.

The more menu provides export and full-screen selection. Editing an existing image creates a new hung copy while retaining the original version and editable data.

## Clothesline

The curved cords, clips, and images span the screen with side margins. An overflow **+** opens History.

| Action | Result |
| --- | --- |
| Click image | Copy the full image |
| Double-click image | Open preview |
| Drag along rail | Reorder and save the new position |
| Esc during reorder | Cancel the reorder |
| Drag into another app | Export a separate PNG while retaining the original |
| Click clip | Turn the image into a floating reference |
| Favorite in context menu | Move into the favorites group on the left |

Up to **three favorites** use gold star clips and a favorite badge. New images and rail expansion produce a brief swing when animation is enabled.

## Preview and floating references

Preview provides fit-to-window, actual size, zoom, and scrolling for long images. Esc closes it.

Floating references remain on top. Drag the header to move, scroll to resize, and Ctrl+scroll to adjust opacity. Close or return-to-rail to hang them again. Placement, size, opacity, favorites, and ordering are stored locally.

## History

Search by date, time, or filename, and filter all/favorite/hung/hidden images. Pages contain 24 items. Ctrl selects multiple items; Shift selects a range.

Actions include rehang, copy, export, float, favorite, hide from rail, edit a copy, OCR, delete to the system Recycle Bin, and undo the last deletion.

Hiding only removes the image from the rail. Deletion includes the image, editable original, and annotations. Emptying the Recycle Bin can make recovery impossible.

## Scrolling capture

Select the scrolling body, excluding fixed navigation and floating elements, then choose scrolling capture. Scroll automatically or manually append frames with overlapping content. Stop, preview/edit, inspect the joins, and save.

The process stops for unchanged content, unreliable matching, or a limit of **80 frames, 30,000 pixels in height, or 50 million total pixels**. Repeating patterns, fixed headers, or animation may prevent reliable stitching.

## Local OCR

The capture toolbar and History provide on-device Windows OCR with an editable result that can be copied. Required language components must be installed; missing components are reported.

Images with a long edge over 4000 pixels are reduced for recognition. Small text and punctuation require proofreading.

## Settings and shortcuts

The two-column Settings window controls capture/toggle shortcuts, display, image size, top offset, delays, animation, and startup. Save to restart and apply changes. A failed shortcut registration attempts to restore the previous binding.

| Context | Shortcut | Action |
| --- | --- | --- |
| Desktop | Ctrl+Alt+A | Capture; configurable |
| Desktop | Ctrl+Alt+T | Expand/collapse; configurable |
| Capture | Enter | Finish and hang |
| Capture | Esc / right-click | Cancel current operation or capture |
| Capture | Ctrl+A | Select the screen under the pointer |
| Editor | Ctrl+Z | Undo |
| Editor | Ctrl+Y / Ctrl+Shift+Z | Redo |
| Editor | Ctrl+S | Export |
| Text entry | Enter / Ctrl+Enter | New line / finish text |
| Selected object | Delete / arrow keys | Delete / nudge |
| Preview | Esc | Close |

## Portable data and upgrades

```text
SnapRail-Portable/
├── SnapRail.exe
├── SnapRail.portable
├── images/                  Completed PNGs
├── documents/               Editable originals and annotation JSON
├── settings.json
├── library.json
├── 使用说明.md               Bundled Chinese usage guide
├── LICENSE
└── THIRD-PARTY-NOTICES.txt
```

Release packages contain no developer screenshots, history, settings, or test data. A new empty library generates ten welcome-card PNGs once. Deleting them does not cause automatic reseeding.

Quit through the tray before moving or backing up the directory. Back up first, replace the executable, and retain the data folders and configuration. Save startup settings again after moving the executable if necessary.

## Privacy and limitations

Screenshots and Windows OCR stay local. There are no accounts, cloud synchronization, or automatic uploads. Copy/drag operations give content to the clipboard or selected destination app at the user's request.

The application is not code-signed. Windows 10, HDR, exclusive full-screen and protected content, display hot-plugging/sleep-wake, and complete compatibility with different messaging apps are not fully verified. OCR can contain errors and scrolling joins can fail; inspect the output.

Windows screenshots in the Chinese guide show the actual 1.0.0 interface with generated demonstration content.

## Build and package

Use Windows and **.NET SDK 10.0.403**. The app uses WPF/Win32 with no third-party NuGet dependencies.

```powershell
.\scripts\setup.ps1
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\package.ps1 -IncludeSource
.\scripts\run.ps1
```

Output: `release/SnapRail-v1.0.0`. Packaging uses a source allowlist and excludes user data and build caches. English and Chinese documentation are included in the guide/source exports when generated by the updated packaging script.

## Feedback and license

Use [GitHub Issues](https://github.com/padlock7493-sudo/SnapRail/issues), including app/Windows versions, display count/scaling, reproduction steps, and expected versus actual behavior. Remove private information before sharing screenshots.

[MIT License](LICENSE). .NET runtime notices accompany the portable package. Interaction ideas were inspired by [PicRope](https://github.com/Renly1994/PicRope) and [Snipaste](https://www.snipaste.com/), with an independent implementation. System fonts are not redistributed.
