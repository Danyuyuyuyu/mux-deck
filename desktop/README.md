# Mux Deck 桌面重写

分支：`v2/avalonia-rewrite`。C# + .NET 10 + Avalonia 12.1.2，面向 Windows 桌面。

桌面版已实现单任务封装第一阶段闭环。现有 Python/Web 版本保留在 `app/`，作为业务行为的参考和迁移前的可用版本。

## 运行

安装 .NET 10 SDK，双击仓库根目录 `start_mux_desktop.bat`，或执行：

```powershell
cd desktop
dotnet restore MuxDeck.sln --locked-mode
dotnet run --project src/MuxDeck.Desktop
# 指定便携工具所在的工作区根目录（其下应有 bin/）：
dotnet run --project src/MuxDeck.Desktop -- --root E:\Video\mux-deck
```

依赖固定在 12.1.2（本机已有完整缓存，便于离线验证），不是浮动的 latest。提交各项目的 `packages.lock.json`，升级 Avalonia 时一起更新 Desktop、Fluent、Headless。

## 已实现

- 原生窗口、浅色 Fluent 主题、侧栏导航、最小窗口尺寸、可滚动内容区。颜色/字号/通用尺寸集中在 `Styles/Resources.axaml`，共享控件样式位于 `Styles/Controls.axaml`；页面与弹窗遵循[浅色视觉规范](docs/design/visual-language.md)。
- 单个封装页按三张已确认布局稿实现：平面视频摘要、统一八列轨道表（纳入/ID/类型/语言/名称/默认/强制/编码，源/外加分组）、并排输出设置与固定 Action Bar；右侧 300 宽全高轨道属性栏贴窗口客户区右边与上下边缘，底部 300 高结果详情止于状态栏上缘，两者覆盖主内容并互斥，主布局不重排，默认列宽下普通 3 源轨 + SC/TC 在 1040×760 无整体滚动。列宽支持拖动、键盘调整和恢复默认，自动存于 v2 用户偏好，关闭再打开保留；加宽后仅表格内部横向滚动。外加行按首次添加顺序从 0 编号，旧草稿 SC/TC 业务身份不变，新导入使用独立轨道身份，空名称留空。目录菜单提供打开和清空，不恢复复制路径。源轨包含状态按策略只读投影，外加字幕可切换，字幕更换/移除在轨道属性面板完成；结果详情的成品计数来自成功结果配套的只读成品探测。面板打开时 Tab/Shift+Tab 跳过被遮挡区域、关闭后焦点回到当前行/入口，轨道清单支持 ↑/↓ 切轨与 Enter/Space 打开属性。工作区自动识别混合视频、音频、多 ASS/SRT 和字体目录；伴随文本不再误拒绝，视频替换保留确认，重复或无效批次整组不提交。
- Core 单任务封装：源视频加多条 ASS/SRT 和音频；ASS 保留 AFS/PyFontTools 子集路径及显式字体目录，SRT/音频无需字体；逐轨元数据、字幕参考全文、音频 raw 数据和源 A/V QC，源字幕/附件替换、失败/取消不发布成品。
- AFS 诊断按精确策略分类：仅已验证的 ASS 颜色高字节工具告警可继续，其他未知告警和致命诊断阻止发布。Core 另可在唯一 Regular face 满足合成条件时报告 `FONT_SYNTHETIC_BOLD`；也可在 ASS 明确引用完整/PostScript face、AFS 映射和源/子集元数据证明为同一唯一 native-bold face 时报告 `FONT_INTRINSIC_WEIGHT`。这两类 Core 提示都不扩大 AFS 告警白名单。规则见[工具诊断分类](docs/agents/diagnostics.md)。
- 环境与组件页使用轻量分组列表和统一状态点，显示经过启动及必要依赖验证的状态、实际版本、来源、路径摘要和核心/扩展组件总览；组件目录优先，随后 PATH，再回退 MKVToolNix 默认安装位置。
- Windows x64 下缺失的 MKVToolNix、FFmpeg、AssFontSubset、assfonts 均可在条目内安装，提供进度、取消、失败重试/详情和自动重新检测；AFS 已有主程序但缺后端时提供“安装依赖”。固定发布包经过大小、SHA-256、解压路径和启动验证后不覆盖地落位，安装日志与缓存使用配置的工作目录。
- 配置组件目录中的手动副本与软件安装均可通过 ⋯ 菜单行内确认卸载，不增加安装归属记录；PATH/系统主程序不提供卸载。AFS 主程序删除随包依赖，独立 fonttools 单独管理并可在重装时复用；卸载后自动检测，失败可查看日志并重试。范围与验收见[组件卸载任务](docs/tasks/component-uninstall.md)。
- 组件安装位置与工作目录可配置，安装版默认使用 `%LOCALAPPDATA%\MuxDeck\tools` 和 `%LOCALAPPDATA%\MuxDeck`，其下自动维护 `config`、`logs`、`cache`；便携 `--root` 继续使用工作区内的 `bin`/`data`。
- 工作区日志、重新检测环境、路径设置持久化。
- 可供后续工具调用复用的异步进程模块：参数列表传递、stdout/stderr 分流、Unicode 输出、有界捕获与受控子进程收尾。
- 控制台冒烟与阶段一验收入口：真实子进程调用、取消、路径优先级、Avalonia 编译绑定、导航、状态保留、真实工具 fixture 与真实 UI 验收。

Windows `Process.Start()` 和后续 Assign Job 之间存在非原子启动窗口。阶段一覆盖了当前同步 AFS/MKV 工具和受控进程取消/管道收尾场景，但未覆盖 launcher 在归属前快速创建后代或退出；不能据此承诺任意 launcher 的完整进程树终止。该启动竞态是阶段二 P2 可靠性事项。

环境检测验证 MKVToolNix 三个子工具和版本一致性、AssFontSubset 与 pyftsubset/ttx 后端、FFmpeg 的 subtitles 滤镜以及 assfonts 启动信息。绿色“可用”表示这些检查通过，实际素材仍由封装任务校验。AFS 完整安装携带独立 Python/fonttools，检测和封装共用其定位；不修改系统 PATH、注册表或用户 Python。assfonts 尚未接入 V2 封装备份后端，也未提供组件更新或覆盖修复。当前范围与验收见[补齐组件安装](docs/tasks/component-installation-complete.md)，首轮记录见[组件安装任务](docs/tasks/component-installation.md)。

## 后续阶段

字幕自动匹配与体检、预设持久化、批量队列、字幕提取、预览、章节编辑、快速修补、组件更新与后处理命令。界面中的待迁移入口明确显示状态，没有模拟封装成功。

当前自动化验收的日期、计数与证据见[验证要求](docs/agents/verification.md#当前自动化能力)。严格 ASS transport 参考比较已通过最新[阶段一真实工具链报告](artifacts/phase1/20261010-015647-daefe8254a624279bdb71f726b161b95/report.json)；真实 UI 10/10 的[历史截图](artifacts/phase1/20261009-113607-f1ca9f2ec2694a72aa06fbdc0a31deb3/real-ui-success.png)来自 2026-10-09。

2026-10-10 完整电影重放已通过 9/9：[真实报告](artifacts/warning-policy/real-e223f0011b774942bf40123b7b1c76a3/report.json)记录 SC/TC 字幕、字形与 ASS 参考全文校验、16 个字体附件提取、源哈希和视频元数据检查。此前因 MKVToolNix 将四条 Comment 移位而失败的[旧报告](artifacts/warning-policy/real-52d8443120a74073b2438784789bc798/report.json)已由同工具参考比较修复。阶段进度见[开发流程](docs/agents/migration.md#当前进度)；原生文件选择器与真实播放器播放仍待人工检查。

## 验证

在 PowerShell 中将临时文件和 .NET CLI 状态放在仓库约定目录，再执行串行 Release 构建：

```powershell
cd desktop
$env:TEMP = Join-Path $PWD 'artifacts/temp'
$env:TMP = $env:TEMP
$env:DOTNET_CLI_HOME = Join-Path (Split-Path $PWD -Parent) 'data/dotnet-cli'
# 仅当受限沙箱明确需要禁用 workload resolver 时，在当前 PowerShell 会话临时启用：
# $env:MSBuildEnableWorkloadResolver = 'false'
New-Item -ItemType Directory -Force $env:TEMP | Out-Null
dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release
# 可选：生成无窗口截图，保存到 desktop/artifacts/smoke-<run-id>/：
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --screenshot
# 使用真实 ffmpeg、mkvmerge 和 AssFontSubset 顺序执行隔离端到端验收：
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --integration
# AFS 告警策略的公共 SingleMuxService 回归（fake runner，不启动媒体工具）：
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-policy
# 单个封装第一轮：A 接纳/状态行为及 B 控件/模拟拖入/布局检查：
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-round1
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-ui-round1
# 自动识别拖放受控回归与真实混合封装：
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --media-drop
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --media-drop-real
# 全窗口拖放遮罩、右上角通知、真实计时与浮层遮挡：
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --input-feedback
# 工作台列宽、数字显示 ID、语言与目录菜单：
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --workbench-ui
# 环境列表、路径保存和组件安装的受控行为与界面检查：
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --environment-ui
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --components
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-ui
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-all
# 组件卸载、依赖关系、操作互斥和行内确认界面：
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall-ui
# 使用固定包缓存，在唯一临时目录执行真实安装/卸载循环（文件名见组件卸载任务）：
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall-real '<固定包缓存目录>'
# 可选联网验收：真实工具验证，并在唯一临时目录安装固定 assfonts 包：
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --components-real
# 可传 workspace-root，只读取其 bin 工具；测试安装仍写入独立临时目录：
# dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --components-real E:\Video\mux-deck
# 真实下载三个组件、验证独立 Python/fonttools、执行真实 ASS 封装和 QC：
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-all-real
# 低成本真实 ffmpeg/AFS/MKVToolNix 颜色告警 fixture：
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-real
# 真实 Regular-only 字体粗体请求 fixture，或附 task.log 对只读原始请求重放：
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-synthetic-bold-real
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-synthetic-bold-real 'artifacts/runs/<task-id>/task.log'
# 可选：从 Core task.log 读取只读源输入，ASS 以字节一致副本重放，输出仍隔离到新目录：
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-real 'artifacts/runs/<task-id>/task.log'
# 对完整 phase1 小 fixture 使用当前新 MainWindow、新入口和真实 Core 串行验收单/双/TC-only：
$fixture = 'artifacts/phase1/<run-id>'
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --ui-integration $fixture (Join-Path $fixture 'real-ui.png')
```

第一轮 UI 使用“封装 / 字体附件”页签，Debug 默认额外显示“日志”，Release 默认隐藏；可在启动进程前设置 `$env:MUX_DECK_DEV_LOG = '1'` 明确开启开发日志。Release 的结果详情仍保留原始错误、所有校验与提示，并可复制诊断、打开成品位置和定位任务日志。字体页不预检或列出未经检查的字体。视频支持 MKV/MP4，ASS/SRT 与音频可多条自动追加，已有视频或所选外加轨道更换仍需确认；无效候选整体拒绝，取消不改变原配置。输出目录通过选择按钮设置。

`--single-mux-ui-round1` 的报告与 1280×900 / 1040×760 多状态截图（空态、普通、长路径、轨道属性、多轨、运行、取消、失败、成功附提示）写入 `artifacts/single-mux-round1-b/ui-<id>/`；模拟 DragDrop 和原生交互替身会明确标注，并读取新行控件的真实 `AutomationPeer.GetName()`。更新后的 `--ui-integration` 从指定 phase1 fixture 复制视频、ASS、字体到独立 `artifacts/single-mux-round1-b/real-<id>/`，经更换视频按钮、自动添加轨道菜单、字幕加入开关和输出选择按钮提交真实 Core。输出、逐场景请求快照、探测数据、日志副本、截图和 JSON 报告保留在该目录，并检查结果详情成品概览、再次封装的目标冲突和输入哈希。选择器返回由 StorageProvider 本地项目替代，因此这些证据证明新控件与真实封装，不证明原生选择器或 Explorer 拖入。

第一轮 UI 代码与自动化收尾已通过负责人验收（UI 151/151），详见[验收记录](docs/tasks/single-mux-ui-approved-layout-acceptance.md)。窗口级双向 Tab 保留面板操作与侧栏，跳过遮挡控件；方向键支持清单跨视口滚动，未选字幕变更保持源轨选择。Windows 原生选择器、Explorer 拖入、系统剪贴板/资源管理器、125%/150% DPI、原生键盘/辅助技术与播放器播放须在交互桌面记录；headless 截图不能代替这些项目。当前窗口最小逻辑尺寸为 1040×760，封装页主区不整体滚动（多轨仅在轨道清单内部滚动），状态区与主操作固定。

Smoke 与截图的本次运行文件只写入 `desktop/artifacts/smoke-<run-id>/`；端到端验收 fixture、工具 stdout/stderr、截图、报告与最终测试输出写入 `desktop/artifacts/phase1/<run-id>/`。Core 每次任务的原始完整日志位于 `desktop/artifacts/runs/<task-id>/task.log`，集成 harness 会把链路用例的任务日志副本放进该 run 的 `task-logs/`。这些 artifacts 是本机忽略目录，不随 Git 提交；上述链接指向本次工作站证据，重新验收会生成新 run-id。Smoke 或集成用例失败返回非零退出码。端到端验收依赖 ToolLocator 可用的真实 ffmpeg、mkvmerge 和 AssFontSubset/PyFontTools；工具缺失表示尚未验收。自动与真实工具链验收已通过；严格人工验收仍待原生文件选择器与播放器播放检查。Windows Job Object 启动归属竞态仍列为阶段二 P2，不承诺任意 launcher 的进程树终止。

开发前读取 [AGENTS.md](AGENTS.md)。架构、开发阶段与验收要求见 [架构导览](ARCHITECTURE.md)。

## 发布方向

后续以 `win-x64` 自包含发布为主，让用户不必安装 .NET SDK。当前交付阶段一开发版本，尚未制作发布包或移除字体工具的运行时依赖。

参考：[Avalonia MVVM 项目结构](https://docs.avaloniaui.net/docs/get-started/create-your-first-project)、[编译绑定](https://docs.avaloniaui.net/docs/data-binding/compiled-bindings)、[固定包版本](https://www.nuget.org/packages/Avalonia.Desktop/12.1.2)。
