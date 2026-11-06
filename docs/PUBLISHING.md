# SnapRail 1.0.0 发布说明

[简体中文](PUBLISHING.md) | [English](PUBLISHING.en.md)

## 发布定位

- Windows：面向普通用户，提供可直接下载、完整解压并运行的 x64 免安装 ZIP。
- macOS：面向开发者，提供源码及本机构建说明，不在本次 Releases 分发 Mac DMG 或应用安装 ZIP。
- 两套源码均在同一个 main 分支维护，中英文 README 分别说明下载和构建方式。

## 先上传源码到 main

发布新版本前，先检查并将源码提交、推送到 main：

```sh
git status
git add .gitignore README.md README.en.md README_WINDOWS.md README_WINDOWS.en.md README_MACOS.md README_MACOS.en.md macos scripts docs
git commit -m "Release Windows portable app and macOS developer source"
git push origin main
```

先处理远端新提交和冲突，不要强制覆盖他人修改。源码包含 src/、tests/、macos/、scripts/、docs/ 和中英文 README；不上传 release/、artifacts/、macos/.build/、.git/ 或个人截图资料。

如果使用 GitHub 网页上传，解压 SnapRail-v1.0.0-source-windows-macos.zip，打开其中的 SnapRail/，将内部文件和目录上传至仓库根目录并提交到 main。

## Releases 附件

上传目录为 release/Releases-v1.0.0/，其中所有文件均可作为附件：

| 文件 | 用途 |
| --- | --- |
| SnapRail-v1.0.0-Portable-win-x64.zip | Windows x64 免安装应用，用户下载 |
| SnapRail-v1.0.0-source-windows-macos.zip | 两套源码，Mac 开发者下载后自行构建 |
| README_WINDOWS.md / README_WINDOWS.en.md | Windows 中英文使用说明 |
| README_MACOS.md / README_MACOS.en.md | Mac 中英文开发者说明 |
| SHA256SUMS.txt | 以上附件的完整性校验值 |

不要上传 release/macos/ 或 release/local-macos-builds-v1.0.0/ 中的本地测试安装包。本次不承诺向普通 Mac 用户提供下载即用的应用，因此 Mac 本地构建的未公证状态不是源码发布阻塞项。

## 创建发布

选择已推送的 main 提交，标题建议“SnapRail 1.0.0 · Windows 免安装版 / macOS 开发者源码”，标签可使用 v1.0.0。如标签已存在，先核对对应提交，不覆盖已有标签。

发布正文可以使用：

```markdown
# SnapRail 1.0.0

截一张，随手挂一下！

- Windows 用户：下载 SnapRail-v1.0.0-Portable-win-x64.zip，完整解压后运行 SnapRail.exe，无需另装 .NET。
- macOS 开发者：下载 SnapRail-v1.0.0-source-windows-macos.zip，或克隆 main 分支，按 README_MACOS.md / README_MACOS.en.md 在 Mac 上自行构建。
- 本次不提供面向普通 Mac 用户的安装包。
- 中英文说明见 README.md / README.en.md，校验值见 SHA256SUMS.txt。
```

## 发布前最后核对

1. main 已包含 Mac 源码与中英文文档，再从该提交创建标签，确保 GitHub 自动源码包包含最新文件。
2. Windows 包包含 EXE、便携标记、许可证、运行时声明和空数据目录，没有个人截图与配置。
3. 双平台源码包包含两套源码和中英文说明，没有编译缓存与个人数据。
4. Releases 上传目录不含 Mac DMG / 应用 ZIP；README 对 Mac 开发者的指引明确。
5. 校验值全部通过。Windows 运行验收参考原 Windows 1.0.0 记录；本次在 Mac 上做归档、版本和完整性检查，没有重新运行 Windows EXE。

Mac 开发者构建后仍需本机授予录屏权限，自动滚动需要辅助功能权限；如果开发者另行分发二进制，应自行处理 Developer ID 签名、公证和分发验证。
