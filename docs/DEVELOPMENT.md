# 开发与验证

## 环境

Windows、.NET SDK 10.0.403（`global.json`）、WPF / Win32。应用无第三方 NuGet 包依赖。首次缺少 SDK 可运行 `scripts/setup.ps1`，SDK 安装在用户目录。

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\package.ps1 -IncludeSource
.\scripts\run.ps1
```

`package.ps1` 创建独立、干净的便携目录，发布文件位于 `release/SnapRail-v1.0.0`，含 Windows ZIP、可选源码 ZIP、图文说明 ZIP、SHA256SUMS 和发布说明。打包仅按白名单复制文件，不读取当前使用中的截图目录。源码不包含 `bin`、`obj`、缓存、日志、用户配置与截图。`release`、`artifacts` 均被 Git 忽略。

## 结构

- `App.xaml.cs`：单实例、截图协调、托盘/快捷键、收藏与资料维护。
- `CaptureWindow` / `WindowTargets`：虚拟桌面物理像素捕获、窗口选择与混合 DPI。
- `Annotations` / `AnnotationCanvas`：标注对象、不可变编辑快照、撤销和像素合成。
- `RailWindow` / `PinWindow` / `ImagePreviewWindow`：绳子、拖动排序、桌面贴图与预览。
- `ImageStore` / `LibraryState`：PNG、编辑资料、分页历史与串行原子状态写入。
- `SettingsStore`：便携标记、相对配置路径与目录复制校验。
- `WelcomeSeed`：空资料库生成 10 张欢迎 PNG，一次性标记防重复；初始化中断可继续。
- `RecycleService`：图片和编辑资料一起回收、失败恢复、整组撤销。
- `OcrWindow` / 嵌入的 `OcrWorker.ps1`：隐藏工作进程调用系统 WinRT OCR，取消/超时清理。
- `ScrollStitcher` / `ScrollCaptureWindow`：相邻画面重叠匹配、条带拼接、模糊匹配拒绝与尺寸上限。

字体引用 Windows 本机字体并提供回退，不附带字体文件。Q 版夹子原图与 ICO 位于 `Assets`；界面夹子、绳子和欢迎文字由 WPF 绘制。创意参考 PicRope / Snipaste 的交互，代码独立实现。

## 测试

`scripts/test.ps1` 为行为测试，涉及 WPF 渲染及剪贴板，在 Windows 桌面会话运行。桌面脚本需要 Python 和 Pillow，并会实际操作鼠标/键盘，应顺序执行，勿并行争用桌面。

使用 `SNAPRAIL_TEST_EXE` 指定发布 EXE；`SNAPRAIL_TEST_PROFILE` 指定独立测试资料夹，应用必须用同一 `--profile <目录>` 启动。`SNAPRAIL_TEST_DATA_ROOT` 可覆盖测试脚本读取的资料目录。正式发布测试不要使用个人资料库。

- `smoke-next.py`：窗口选择、标注对象、OCR、真实滚动拼接与未保存关闭确认。
- `smoke-editor.py`：六种标注、中文多行、撤销重做、像素与旧版本保留。
- `smoke-preview.py`：单击复制、双击预览、缩放、截图隐藏预览与 Esc。
- `smoke-monitors.py`：真实双屏像素、跨屏、贴图迁移和重启恢复。
- `smoke-rail.py` / `smoke-browsers.py`：拖动导出、贴图与浏览器接收。
- `smoke-management.py`：历史、系统回收站、快捷键冲突、迁移与启动项恢复；仅适用于可迁移的非便携测试配置。
- `smoke-stress.py`：默认独立资料夹 100 次截图，支持 `SNAPRAIL_STRESS_COUNT`、`SNAPRAIL_IDLE_SECONDS`、`SNAPRAIL_CRASH_RECOVERY=1`。
- `--scroll-layout <输出目录>`：行为测试 EXE 的可选桌面布局探针，检查 DPI 和按钮视口。

发布前结果见 `RELEASE_VERIFICATION.md`。真实硬件电源与显示器拔插由人工验证，不将模拟检查表述为真实硬件通过。

## 公开文档配图

`scripts/capture-docs.py` 使用隔离的便携目录和演示底图，捕获实际 Windows 窗口到 docs/images。当前演示坐标针对 3072×1920 主屏，依赖 Python、Tk 和 Pillow；脚本操作鼠标/键盘，执行时保持桌面空闲。配图是公开文档资源，不是用户截图资料库。
