# Publishing SnapRail 1.0.0

[简体中文](PUBLISHING.md) | [English](PUBLISHING.en.md)

## Audience and distribution

- Windows: a portable x64 ZIP for general users to download, extract, and run.
- macOS: source code and local build instructions for developers. No public Mac DMG or application ZIP is included in this release.
- Both native implementations remain on the same main branch, with Chinese and English documentation.

## Push source to main first

Before publishing a new version, review and push the source changes to main:

```sh
git status
git add .gitignore README.md README.en.md README_WINDOWS.md README_WINDOWS.en.md README_MACOS.md README_MACOS.en.md macos scripts docs
git commit -m "Release Windows portable app and macOS developer source"
git push origin main
```

Reconcile remote commits and conflicts first; do not force-push over others' changes. Include src/, tests/, macos/, scripts/, docs/, and bilingual README files. Exclude release/, artifacts/, macos/.build/, .git/, and personal image libraries.

For GitHub web upload, extract SnapRail-v1.0.0-source-windows-macos.zip, open its SnapRail/ directory, and upload the contents to the repository root on main.

## Release assets

All files in release/Releases-v1.0.0/ are prepared as public attachments:

| File | Purpose |
| --- | --- |
| SnapRail-v1.0.0-Portable-win-x64.zip | Portable Windows application for general users |
| SnapRail-v1.0.0-source-windows-macos.zip | Both implementations; Mac developers build locally |
| README_WINDOWS.md / README_WINDOWS.en.md | Chinese and English Windows usage guides |
| README_MACOS.md / README_MACOS.en.md | Chinese and English Mac developer guides |
| SHA256SUMS.txt | Integrity checks for these assets |

Do not upload the local Mac installers in release/macos/ or release/local-macos-builds-v1.0.0/. Since this release does not distribute a ready-to-run Mac app to general users, notarization of those local binaries is not a blocker for publishing the source.

## Create the release

Use the pushed main commit. Suggested title: "SnapRail 1.0.0 - Windows Portable / macOS Developer Source", with tag v1.0.0. If the tag exists, inspect its commit first and do not overwrite it.

Suggested release body:

```markdown
# SnapRail 1.0.0

Capture it. Hang it up.

- Windows users: download SnapRail-v1.0.0-Portable-win-x64.zip, extract it, and run SnapRail.exe. No separate .NET installation is needed.
- macOS developers: download SnapRail-v1.0.0-source-windows-macos.zip or clone main, then build locally following README_MACOS.md / README_MACOS.en.md.
- No installer for general Mac users is included in this release.
- Chinese and English overviews are in README.md / README.en.md. Check downloads with SHA256SUMS.txt.
```

## Final checks

1. Push current Mac source and bilingual docs before tagging, so GitHub's automatic source archives include the current files.
2. The Windows ZIP contains its EXE, portable marker, license, runtime notices, and empty data directories, without personal images or configuration.
3. The shared source archive contains both implementations and bilingual docs, without caches or personal data.
4. The public upload folder has no Mac DMG/application ZIP, and the README clearly addresses Mac developers.
5. All checksums pass. Windows runtime testing is documented in the original Windows 1.0.0 verification record; this Mac review checked archive structure, version, and integrity without rerunning the Windows EXE.

Mac developers must grant permissions to their locally built app. Screen capture requires Screen Recording; automatic scrolling also requires Accessibility. Developers who redistribute binaries are responsible for Developer ID signing, notarization, and distribution testing.
