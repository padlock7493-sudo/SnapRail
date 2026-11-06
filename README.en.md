# SnapRail

<img src="src/SnapRail.App/Assets/SnapRail-mist-blue-v1.png" width="112" alt="SnapRail icon">

[简体中文](README.md) | [English](README.en.md)

**Capture it. Hang it up.**

**Windows users can download the portable application; the macOS implementation is provided as source for developers to build locally.**

A desktop screenshot utility for Windows and macOS. Capture and annotate a region, then save it, copy it to the clipboard, and hang it on a clothesline at the top of the screen. Includes screenshot history, floating reference images, on-device OCR, and scrolling capture.

## Choose your download

Visit **[GitHub Releases](https://github.com/padlock7493-sudo/SnapRail/releases)** and choose the attachment for your system. Available downloads depend on the assets actually uploaded to a release; repository source code must be built locally.

| System | Version | Download file | Instructions |
| --- | --- | --- | --- |
| Windows x64 | 1.0.0 stable | `SnapRail-v1.0.0-Portable-win-x64.zip` | [Windows guide](README_WINDOWS.en.md) |
| macOS developers | 1.0.0 source | `SnapRail-v1.0.0-source-windows-macos.zip`, or clone the repository | [macOS developer guide](README_MACOS.en.md) |

Windows: extract the entire ZIP and run `SnapRail.exe`. No separate .NET installation is needed.

macOS: obtain the source, install Apple Command Line Tools or Xcode, and build locally using the developer guide. This release does not distribute a Mac installer to general users. Local builds require Screen Recording permission; automatic scrolling also requires Accessibility.

Version 1.0.0 provides a Windows portable application and a source archive containing both native implementations.

## Features

- Region, window, and full-screen capture; rectangles, ellipses, arrows, freehand drawing, text, pixelation, undo, and redo.
- A clothesline along the top of the screen, image ordering, favorites, and floating reference images.
- History search, filters, multiple selection, system Trash/Recycle Bin deletion, and undo for the last deletion.
- Preview, copy, export, editable copies, local Chinese/English OCR, and scrolling capture.
- Configurable shortcuts, storage location, hover behavior, and animation.

Both native implementations share the visual style and core interactions. System windows, permissions, fonts, and key names follow platform conventions. Windows annotation JSON cannot currently be imported directly into the Mac library; PNG files can be imported.

## Default shortcuts and storage

| Action or location | Windows | macOS |
| --- | --- | --- |
| Capture | Ctrl+Alt+A | Control+Option+A |
| Expand/collapse | Ctrl+Alt+T | Control+Option+T |
| Screenshots | `images/` beside the portable executable | `SnapRail-data/images/` beside the app |
| Editable originals | `documents/` beside the portable executable | `SnapRail-data/documents/` beside the app |

Shortcuts and storage can be changed in Settings. When the app's parent directory is not writable, or macOS runs it from an App Translocation path, the Mac default falls back to `~/Library/Application Support/SnapRail/`. Existing custom paths remain in use.

## Developers: one branch, two native implementations

Both implementations live on the same `main` branch:

```text
SnapRail/
├── README.md / README.en.md                 Project overview
├── README_WINDOWS.md / README_WINDOWS.en.md Windows guide
├── README_MACOS.md / README_MACOS.en.md     macOS guide
├── src/SnapRail.App/                        Windows: C# / WPF / Win32
├── tests/                                  Windows tests
├── macos/                                  macOS: Swift / AppKit / SwiftUI
│   ├── Sources/
│   ├── Resources/
│   └── Tests/
├── scripts/                                Build, test, and packaging scripts
└── docs/                                   Guides and verification records
```

Clone once to obtain both implementations:

```sh
git clone https://github.com/padlock7493-sudo/SnapRail.git
cd SnapRail
```

**On Windows**, use .NET SDK 10.0.403:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\package.ps1 -IncludeSource
```

**On macOS**, install Apple Command Line Tools or Xcode:

```sh
./scripts/test-macos.sh
./scripts/build-macos.sh
open release/macos/SnapRail.app
./scripts/package-macos.sh --skip-build
```

Run each build on its corresponding system. Windows WPF and Mac AppKit are separate native implementations, so feature changes need to be maintained in both.

## Privacy and compatibility

Screenshots and OCR are processed locally. There are no accounts, cloud synchronization, or automatic uploads. Copying, dragging, and exporting send content to the clipboard, destination app, or selected file location at the user's request.

Windows was primarily tested on Windows 11 x64. The locally built Mac implementation was tested on Apple Silicon with macOS 26.5.1 and targets macOS 13 or later; older macOS versions have not received hardware testing. See each platform guide for limitations.

[Windows verification record, Chinese](docs/RELEASE_VERIFICATION.md) · [macOS verification record, Chinese](docs/MACOS_VERIFICATION.md)

## Publishing and feedback

One GitHub Release contains the Windows portable ZIP, shared source ZIP, Chinese/English guides, and checksums. See the [publishing guide](docs/PUBLISHING.en.md).

Report issues through [GitHub Issues](https://github.com/padlock7493-sudo/SnapRail/issues). Include the OS, app version, architecture, and reproduction steps. Check screenshots for private information before sharing.

## License and acknowledgments

[MIT License](LICENSE). The self-contained Windows package includes .NET runtime notices; the Mac app has no third-party Swift package dependencies.

Interaction ideas were inspired by [PicRope](https://github.com/Renly1994/PicRope) and [Snipaste](https://www.snipaste.com/), with an independent implementation. System fonts are not redistributed.
