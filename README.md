# SnapRail · 随手挂

<img src="src/SnapRail.App/Assets/SnapRail-mist-blue-v1.png" width="112" alt="随手挂图标">

[简体中文](README.md) | [English](README.en.md)

**截一张，随手挂一下！**

**Windows 用户下载免安装包即可使用；macOS 版本提供源码，面向开发者自行构建。**

桌面截图工具：框选、标注并确认后，图片自动保存、复制到剪贴板，并挂在屏幕顶部的晾衣绳上。支持历史管理、桌面置顶贴图、本机文字识别和滚动长截图。

SnapRail is a desktop screenshot utility for Windows and macOS, with annotations, a clothesline, pinned images, local OCR and scrolling capture.

## 按系统下载

前往本仓库的 **[Releases 下载页面](https://github.com/padlock7493-sudo/SnapRail/releases)**，根据系统选择对应附件。以 Releases 中实际上传的附件为准，仓库中的源码需要自行构建。

| 系统 | 当前版本 | 对应下载文件 | 使用与构建说明 |
| --- | --- | --- | --- |
| Windows x64 | 1.0.0 正式版 | `SnapRail-v1.0.0-Portable-win-x64.zip` | [Windows 说明](README_WINDOWS.md) |
| macOS（开发者） | 1.0.0 源码 | `SnapRail-v1.0.0-source-windows-macos.zip`，或克隆仓库 | [macOS 开发者说明](README_MACOS.md) |

Windows：完整解压 ZIP，运行 `SnapRail.exe`，无需另装 .NET。

macOS：获取源码后，在 Mac 上安装 Apple Command Line Tools 或 Xcode，并按开发者说明构建。当前 Releases 不提供面向普通 Mac 用户的安装包。自行构建后需在系统设置授予屏幕录制权限，自动滚动还需辅助功能权限。

本次 1.0.0 发布提供 Windows 免安装包，以及包含 Windows、macOS 两套实现的源码包。

## 功能

- 区域、窗口和全屏截图；矩形、椭圆、箭头、画笔、文字和马赛克标注，支持撤销重做。
- 屏幕顶部晾衣绳、挂图排序和收藏；取下为桌面置顶参考图。
- 历史搜索、筛选、多选、回收和撤销上次删除。
- 图片预览、复制、另存为、编辑副本、本机中英文字识别及滚动长截图。
- 可修改快捷键、保存位置、悬停与动画设置。

两平台沿用同一套界面与主要交互，系统窗口、权限、字体和快捷键名称使用各平台机制。Windows 标注 JSON 目前不能直接导入 Mac；PNG 可以导入。

## 默认快捷键与保存位置

| 项目 | Windows | macOS |
| --- | --- | --- |
| 截图 | Ctrl+Alt+A | Control+Option+A |
| 展开 / 收起 | Ctrl+Alt+T | Control+Option+T |
| 默认截图目录 | 便携版程序文件夹中的 `images/` | 应用同级 `SnapRail-data/images/` |
| 默认编辑资料 | 便携版程序文件夹中的 `documents/` | 应用同级 `SnapRail-data/documents/` |

快捷键和保存目录均可在设置修改。Mac 默认位置不可写或处于系统随机隔离路径时，回退至 `~/Library/Application Support/SnapRail/`；已有自定义目录继续保留。

## 开发者：一个分支，两套平台源码

两套实现保存在同一个 `main` 分支，通过目录区分：

```text
SnapRail/
├── README.md / README.en.md    中英文双平台首页
├── README_WINDOWS*.md          中英文 Windows 说明
├── README_MACOS*.md            中英文 macOS 说明
├── src/SnapRail.App/           Windows · C# / WPF / Win32
├── tests/                     Windows 测试
├── macos/                     macOS · Swift / AppKit / SwiftUI
│   ├── Sources/
│   ├── Resources/
│   └── Tests/
├── scripts/                   各平台构建、测试与打包脚本
└── docs/                      使用说明和验证记录
```

克隆一次即可取得两套源码：

```sh
git clone https://github.com/padlock7493-sudo/SnapRail.git
cd SnapRail
```

**在 Windows 上**，使用 .NET SDK 10.0.403：

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\package.ps1 -IncludeSource
```

**在 Mac 上**，安装 Apple Command Line Tools 或 Xcode：

```sh
./scripts/test-macos.sh
./scripts/build-macos.sh
open release/macos/SnapRail.app
./scripts/package-macos.sh --skip-build
```

构建脚本在对应系统运行。Windows WPF 和 Mac AppKit 为两套原生实现，新增功能时需分别维护。

## 隐私、兼容性与验证

截图和 OCR 在本机处理，无账号、云同步或自动上传。用户主动复制、拖出或另存为时，内容交由剪贴板、目标应用或选择的位置处理。

Windows 主要在 Windows 11 x64 验证；Mac 源码构建在 Apple Silicon、macOS 26.5.1 验证，最低部署目标 macOS 13，较旧 macOS 尚未实机验收。其他限制请阅读对应平台说明。

[Windows 验证记录](docs/RELEASE_VERIFICATION.md) · [macOS 验证记录](docs/MACOS_VERIFICATION.md)

## 发布与反馈

同一个 GitHub Release 提供 Windows 免安装 ZIP、双平台源码 ZIP、中英文说明和校验文件。上传步骤及本次附件清单见 [双平台发布说明](docs/PUBLISHING.md)。

通过 [GitHub Issues](https://github.com/padlock7493-sudo/SnapRail/issues) 反馈问题时，请提供操作系统、应用版本、芯片或架构、复现步骤。分享截图前请检查个人资料。

## 许可证与致谢

[MIT 许可证](LICENSE)。Windows 自包含包附带 .NET 运行时声明；Mac 版没有第三方 Swift 包依赖。

创意参考 [PicRope](https://github.com/Renly1994/PicRope) 与 [Snipaste](https://www.snipaste.com/) 的交互，独立实现。图标沿用仓库素材，不分发系统字体。
