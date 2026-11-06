# SnapRail · 随手挂 — macOS

[简体中文](README_MACOS.md) | [English](README_MACOS.en.md)

**截一张，随手挂一下！**

macOS **1.0.0 源码**，Swift / AppKit / SwiftUI 原生桌面应用。沿用 Windows 版的晾衣绳、夹子、配色、功能入口与主要交互，可截图、标注、置顶参考、管理历史、识别文字及拼接长截图。无需 .NET，无第三方 Swift 包依赖，MIT 开源。

## 获取源码并在本机运行

macOS **1.0.0 源码**面向开发者提供，当前 Releases 不分发 Mac DMG 或应用安装 ZIP。可从 [Releases](https://github.com/padlock7493-sudo/SnapRail/releases) 下载 `SnapRail-v1.0.0-source-windows-macos.zip`，或克隆同一个 main 分支：

```sh
git clone https://github.com/padlock7493-sudo/SnapRail.git
cd SnapRail
./scripts/build-macos.sh
open release/macos/SnapRail.app
```

需要 Apple Command Line Tools（`xcode-select --install`）或 Xcode，无需 .NET 或第三方 Swift 包。当前在 **Apple Silicon、macOS 26.5.1** 上验证，最低部署目标为 **macOS 13**。

本机构建脚本默认使用临时签名，尚无 Developer ID 签名和 Apple 公证。重新构建后可能需要重新授权；如果开发者将自己的二进制分发给其他 Mac 用户，应自行处理签名、公证和分发测试。

这是菜单栏应用，不显示 Dock 图标。启动后可在菜单栏找到“随手挂”；关闭窗口不会退出程序，退出请使用菜单栏菜单或 Command+Q。

## 首次授权

截图需要在“系统设置 → 隐私与安全性 → 屏幕与系统音频录制”（旧版名称为“屏幕录制”）允许 **SnapRail**。系统要求时请退出并重新打开应用。

自动滚动还需要在“系统设置 → 隐私与安全性 → 辅助功能”允许 **SnapRail**。不授予辅助功能权限时，可手动滚动正文，再点击“追加当前画面”。这些权限只用于用户发起的截图与滚动，不需要麦克风权限。系统身份验证应由用户直接通过触控 ID 或本机密码完成。

应用界面目前为中文；英文版 Markdown 提供对应的使用说明。

## 快速使用

1. 按 **Control+Option+A** 开始截图。
2. 拖动框选区域，单击选窗口，或按 Command+A / Control+A 选鼠标所在屏幕；拖动边缘或手柄调整范围。
3. 选择矩形、椭圆、箭头、画笔、文字或马赛克进行标注。
4. 按 Enter 或“完成并挂上”：保存 PNG、复制完整图片，并挂到屏幕顶部。
5. 按 **Control+Option+T** 展开/收起挂图，也可按设置中的顶部悬停方式展开。
6. 单击挂图复制，双击预览，点击夹子取下为置顶贴图。

## 功能与操作

| 功能 | 操作与行为 |
| --- | --- |
| 晾衣绳 | 整屏双色弧线、卡通夹子、欢迎字卡；收藏靠左，最多 3 张；溢出入口进入历史 |
| 标注 | 六种标注、六色调色板、线宽/字号、撤销重做；对象工具移动、改色和删除，双击文字修改 |
| 中文文字 | 支持多行；Enter 换行，Command+Enter 完成输入 |
| 挂图整理 | 沿绳拖动排序，Esc 取消；向支持文件拖放的应用拖出 PNG |
| 桌面贴图 | 置顶参考、拖动上沿移动、滚轮缩放、Control+滚轮调透明度；挂回绳子并保留位置与尺寸记录 |
| 历史 | 每页 24 张；全部/收藏/挂图/待挂筛选；按日期或文件名搜索；Cmd / Ctrl 多选，Shift 连选 |
| 批量操作 | 挂图、贴图、收藏、隐藏、移至系统废纸篓及撤销上次删除；删除前确认 |
| 预览 | 深色背景、适应窗口、实际大小、缩放、长图滚动、复制、另存为、编辑副本；Esc 关闭 |
| OCR | Apple Vision 在本机识别中英文，可修改结果后复制；小字、标点和识别顺序需要校对 |
| 设置 | 两栏布局，自定义两组快捷键、保存目录、目标屏幕、登录启动、悬停、动画、尺寸与延迟；保存并重启 |

编辑副本会生成新挂图，保留旧版本及可编辑资料。系统开启“减少动态效果”时，挂图动画关闭。新的快捷键注册失败时会尝试恢复原绑定。

## 滚动长截图

框选正文，避开固定导航栏与悬浮控件，然后点击“长截图”。可“自动向下滚动”，也可手动滚动后“追加当前画面”；相邻画面需要保留重叠。点击“停止 → 预览并编辑”，检查后保存。

画面不变、重叠匹配不可靠或达到 **80 帧 / 30000 像素高 / 5000 万像素** 时停止，并保留已拼接内容。动画、重复纹理、固定栏或滚动过远可能造成失败，请检查拼接结果。

## 快捷键

| 场景 | 快捷键 | 功能 |
| --- | --- | --- |
| 桌面 | Control+Option+A | 截图，可修改 |
| 桌面 | Control+Option+T | 展开/收起，可修改 |
| 截图 | Enter / Esc / 右键 | 完成 / 取消当前操作 |
| 截图 | Command+A / Control+A | 选择鼠标所在屏幕 |
| 编辑 | Command+Z / Command+Shift+Z | 撤销 / 重做，兼容对应 Ctrl 编辑快捷键 |
| 编辑 | Command+S / Control+S | 另存为 |
| 对象 | Delete / 方向键 | 删除 / 微调 |
| 文字输入 | Enter / Command+Enter | 换行 / 完成 |
| 预览、OCR | Esc | 关闭 |

## 本地数据与隐私

截图、标注和 OCR 均在本机处理。应用无账号、云同步或截图上传功能；主动复制、另存为或拖出时，内容交由剪贴板、用户选择的位置或目标应用处理。

默认资料目录：`SnapRail.app` 所在文件夹下的 `SnapRail-data/`，截图保存在其中的 `images/`。例如应用安装在 `~/Applications/SnapRail.app` 时，截图位于 `~/Applications/SnapRail-data/images/`。

目录不可写（例如只读 DMG）或应用处于系统随机隔离路径时，默认回退到 `~/Library/Application Support/SnapRail/`。已有自定义路径继续使用；可在设置中迁移。设置文件仍固定保存于用户的 Application Support/SnapRail 目录，应用移动后不会擅自移动原资料。

```text
SnapRail-data/
├── images/              完整 PNG
├── documents/           可编辑原图 PNG 与标注 JSON
├── library.json         历史、收藏、顺序与贴图位置
├── library.json.bak     可用的上一版资料库清单（写入后生成）
├── recovery.json        最近一次删除的废纸篓映射
├── welcome-seeded       欢迎图一次性标记
└── app.lock             单实例锁
```

设置中可迁移到空目录。迁移使用临时目录并逐文件校验 SHA-256，拒绝嵌套目录与符号链接，成功后保留原数据。迁移后原目录可能仍保留旧清单，不要同时运行两个副本编辑同一份资料。

清单损坏时优先尝试备份；两份都不可读时进入保护模式，阻止写入和重复生成欢迎图。应先完整备份目录再恢复。“从绳子取下”只隐藏挂图。删除移至系统废纸篓，清空废纸篓后不能保证恢复。

## 与 Windows 版的差异

晾衣绳、夹子、工具栏、历史和设置按 Windows 版移植；Mac 系统标题栏、文件选择器、权限、废纸篓、显示器标识和登录项目采用 macOS 机制。中文使用 Mac 本机字体，字体和系统窗口边框不会逐像素相同。

Windows 的便携目录和标注 JSON 目前不能直接作为 Mac 资料库使用。可以导入 PNG；Mac 自己创建的原图和标注可持续编辑。Windows 源码保留在 `src/`。

## 开发、测试与打包

需要 Apple Command Line Tools，或包含相应工具的 Xcode。首次准备环境可运行 `xcode-select --install`。

```sh
./scripts/test-macos.sh
./scripts/build-macos.sh
open release/macos/SnapRail.app
./scripts/package-macos.sh --skip-build
```

构建脚本生成当前机器架构的应用；测试程序独立编译，不包含在应用内，无需 XCTest 或完整 Xcode。打包输出位于 `release/macos/`，仅包含应用、中英文说明和许可证，不含截图历史、测试资料库或编译缓存。

隔离测试与只读诊断：

```sh
open -n release/macos/SnapRail.app --args --profile /tmp/SnapRail-review
release/macos/SnapRail.app/Contents/MacOS/SnapRail --diagnostics
# 以下演示参数必须带独立资料库，不读取真实桌面
open -n release/macos/SnapRail.app --args --profile /tmp/SnapRail-demo --demo-capture
```

代码结构：

| 目录/文件 | 职责 |
| --- | --- |
| `macos/Sources/SnapRail/App*.swift`、`main.swift` | 生命周期、菜单与快捷键 |
| `Capture.swift`、`EditorController.swift`、`Annotation*.swift` | 屏幕采集、选区与标注 |
| `Rail.swift`、`Pin.swift`、`History*.swift`、`PreviewView.swift` | 挂图、贴图、历史和预览 |
| `Library.swift`、`Models.swift`、`DataMigration.swift` | 资料库事务、删除恢复与迁移 |
| `Configuration.swift`、`SettingsView.swift`、`ShortcutRecorder.swift` | 配置与快捷键录制 |
| `ScrollCapture.swift`、`ScrollStitcher.swift` | 真实滚动采集与重叠匹配 |
| `macos/Tests/` | 独立回归测试和本地演示页面 |
| `scripts/*-macos.sh` | 构建、测试与打包 |

## 1.0.0 更新

点击“＋号 / 更多挂图”“设置”或“历史”时自动收起晾衣绳；鼠标未离开顶部触发区时不会立即重开。

### 0.2.5 修复

新安装默认将图片和编辑资料保存到应用同级的 `SnapRail-data/`，设置中仍可更改。已有自定义目录继续保留；不可写和系统隔离路径回退到用户资料目录。

### 0.2.4 修复

设置中明确显示当前版本的屏幕录制权限状态。临时签名版本更新后，旧授权可能不再匹配；即使系统列表仍显示开启，也可能需要重新开启 SnapRail 并退出重开。稳定的 Apple 开发/分发签名可避免临时签名按版本变化的问题，当前包尚未具备此签名。

### 0.2.3 修复

快捷键录制、设置、挂图栏及相关提示使用 Mac 键名 Control、Option、Shift、Command；设置内显示对应符号。沿用已有按键绑定，无需重新配置。

### 0.2.2 修复

修复顶部悬停展开后图片持续摆动：每次移入顶部触发区只展开一次，离开后才允许再次触发；停在顶部保持展开，手动收起后不会因鼠标未离开而立即重开。新截图挂上与正常展开的轻摆动画保留。

## 本次验证与限制

2026-11-06：最新功能回归为 **29/29 组通过**，1.0.0 基于已验证功能发布并统一版本号。

2026-11-05，Apple Silicon、macOS 26.5.1、Swift 6.3.2：完成构建、回归测试、格式/签名检查与原生界面操作。真实 Chrome 本地页面测试完成截图、中英 OCR、自动滚动 9 帧拼接、停止、预览编辑和保存；长图为 **2150×3614 像素**，正文第 1～5 节连续。

更多测试项目与范围见源码仓库的 `docs/MACOS_VERIFICATION.md`。未覆盖的项目包括 macOS 13 实机、真实多屏/混合缩放、显示器热插拔、注销后登录启动及不同外部应用的完整拖放兼容性。本机已通过系统身份验证，为独立 SnapRail 开启屏幕录制和辅助功能，并正常启动应用复测真实截图与自动滚动；运行中记录到 26 帧、2150×10300 像素，停止后可进入编辑器。其他 Mac 安装后仍需各自完成首次权限授权。

## 反馈与许可证

请在 [GitHub Issues](https://github.com/padlock7493-sudo/SnapRail/issues) 提供版本、macOS 版本、芯片、显示器与缩放、复现步骤和预期结果。分享截图前请检查个人资料。

MIT License。图标沿用仓库现有素材，不分发系统字体。
