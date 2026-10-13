# 补齐组件行内安装

本轮输入为当前组件检测和用户配置的组件/工作目录；输出为 MKVToolNix、FFmpeg、AssFontSubset 的实际安装结果，与既有 assfonts 使用相同的行内进度、取消、重试、详情和自动检测。正常组件不增加操作。

使用固定 Windows x64 发布包并核对大小和 SHA-256。MKVToolNix 验证三个子工具，FFmpeg 验证字幕滤镜，AssFontSubset 配套独立 Python/fonttools，既有主程序只缺依赖时提供“安装依赖”。依赖由 Mux Deck 目录管理，不调用用户 Python 的 pip，不修改系统 PATH 或注册表。

沿用暂存验证、同卷不覆盖提交、工作目录缓存/日志和取消清理；拒绝覆盖已有未知目录。保留现有路径保存与页面结构。每种组件覆盖安装布局、关键失败与真实包验证，并验证 AFS 使用独立后端的真实封装链路。完成后记录版本/哈希、执行结果、截图和限制。

## 实现与目录

四组件共用 `ComponentInstaller`；所有包下载并通过大小/哈希检查后才进入解压和安装，随后验证候选、同卷不覆盖提交，再按原入口自动重新检测。Windows 原生可执行路径过长时在下载前提示选择较短目录。

组件目录内分别使用 `mkvtoolnix/`、`ffmpeg/bin/`、`assfontsubset/`、`assfonts/`。完整 AFS 安装的独立后端位于 `assfontsubset/python/Scripts/`；只补依赖时新增 `fonttools/Scripts/`，原有 AFS 主程序保持不变。`FontToolsLocator` 由组件检测和实际封装共用，优先独立后端，再保留旧后端查找。Python `_pth` 只包含其标准库、自身目录和随包 fonttools；使用 distlib 相对启动器，因此暂存落位、中文/空格目录及整个组件目录移动均不残留旧路径。

真实封装检查发现此前工作目录拆分后生成 `cache/<run-id>`，但清理守卫仅接受 `work` 子目录。已恢复 `cache/<run-id>/work`，仍保留目录拥有权检查，避免任务结束留下中间文件。封装参数、QC 与 AFS 告警规则未调整。

## 固定发布包

版本、URL、大小、SHA-256、解压根目录和必需文件的唯一清单位于 [ComponentReleases.cs](../../src/MuxDeck.Core/Components/ComponentReleases.cs)。本轮发布包全部独立下载并核对摘要；FFmpeg 使用 [FFmpeg 官网](https://ffmpeg.org/download.html)列出的 Gyan Windows 构建来源。

| 组件/依赖 | 固定发布版本 | 验证结果 |
|---|---|---|
| [MKVToolNix](https://mkvtoolnix.download/downloads.html#windows) | 101.0 Windows x64 ZIP | 三个子工具版本一致 |
| [FFmpeg Gyan](https://github.com/GyanD/codexffmpeg/releases/tag/8.1.1) | 8.1.1 essentials ZIP | subtitles 滤镜、真实素材生成通过 |
| [AssFontSubset](https://github.com/AmusementClub/AssFontSubset/releases/tag/v2.2.0) | v2.2.0 win-x64 ZIP | 包内部返回 `2.1.1+abeaf2a431ab95572379e8501a06b09389f5173d`，页面显示实际返回值 |
| [Python](https://www.python.org/downloads/release/python-31210/) | 3.12.10 embeddable x64 | 隔离加载，不修改系统 Python |
| [fonttools](https://pypi.org/project/fonttools/4.61.1/) | 4.61.1 纯 Python wheel | pyftsubset、ttx 与真实子集/QC 通过 |
| [pip wheel 启动器](https://pypi.org/project/pip/25.3/) | 25.3 中的 distlib 0.4.0 | 只使用已校验启动器与许可证，不运行 pip 安装 |

## 验证结果（2026-10-12）

Core、Desktop、Smoke 的 Release 构建均为 0 warnings / 0 errors，未新增 .NET 包依赖。受限沙箱构建继续复用现有 restore assets，输出在首轮记录的 `desktop-build2/` 目录。

| 检查 | 结果 | 证据 |
|---|---|---|
| `--components` | 31/31 | `%TEMP%/mux-deck-components-70f8aa31625d47178fe05c4eba33f320/`，实际控制台输出与安装日志 |
| `--component-install-all` | 41/41 | `%TEMP%/mux-deck-install-eaee36cbd31f466e8fb0cd61e2cb6576/report.json` |
| `--component-install-all-real` | 15/15 | `%TEMP%/mux-deck-install-4cc8ba3e24ac45f18ceee6bd92bdc56e/report.json` |
| `--component-install-ui` | 37/37 | `desktop-build2/bin-smoke/desktop/artifacts/component-ui/ebfa719fda314c6f9a6e57eb2f11df36/` |
| `--environment-ui` | 23/23 | `desktop-build2/bin-smoke/desktop/artifacts/environment-ui/fe6ba1439c624e419f3c12f15d28e7a8/` |
| 默认 Smoke | 28/28 | 实际控制台输出 |
| `--warning-policy` | 57/57 | `desktop-build2/bin-smoke/desktop/artifacts/warning-policy/20261012-145032-e91139ab8b7742dc98993bb814086d97/report.json` |
| `--single-mux-ui-round1` | 176/176 | `desktop-build2/bin-smoke/desktop/artifacts/single-mux-round1-b/ui-2674232ea52d44759d0b8a4db15d2540/report.json` |

真实检查下载所有固定包并验证正式落位；以新安装的 FFmpeg 生成视频，使用 Arial、AFS 与管理的 fonttools 执行真实子集化、封装、字幕/字体/源 A/V QC，验证任务实际使用独立后端以及缓存文件清理。也覆盖目录整体移动后运行和保留已有 AFS 主程序的依赖安装。本机仍有用户 Python，所以证据通过清空测试进程 PATH、强制候选后端和核对实际调用路径证明管理后端使用，不声称在无 Python 的全新 Windows 系统完成测试。

已目视检查 1280×900 和 1040×760 的多个缺失组件及 AFS 缺少依赖截图，安装动作与状态列对齐，无横向溢出。UI 受控场景覆盖各组件行内反馈、依赖补齐、取消、失败、重试、详情、切页及关闭。安装验收均在唯一临时目录中，不改变当前用户组件配置。原生目录选择器、真实辅助技术、高 DPI 仍待人工检查。组件更新、已有目录覆盖修复、ARM64 和 assfonts 封装备份后端不在本轮范围。
