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

- 原生窗口、深色 Fluent 主题、侧栏导航、最小窗口尺寸、可滚动内容区。
- 单个封装表单：视频原生文件选择、简繁字幕路径和输出目录输入；切换页面保留当前表单。
- Core 单任务封装：一个 MKV/MP4 视频与一或两条 ASS，固定 AFS/PyFontTools 子集路径，显式字体目录，逐轨元数据和源 A/V QC，源字幕/附件替换、失败/取消不发布成品。
- AFS 诊断按精确策略分类：仅已验证的 ASS 颜色高字节工具告警可继续，其他未知告警和致命诊断阻止发布。Core 另可在唯一 Regular face 满足合成条件时报告 `FONT_SYNTHETIC_BOLD`；也可在 ASS 明确引用完整/PostScript face、AFS 映射和源/子集元数据证明为同一唯一 native-bold face 时报告 `FONT_INTRINSIC_WEIGHT`。这两类 Core 提示都不扩大 AFS 告警白名单。规则见[工具诊断分类](docs/agents/diagnostics.md)。
- 六项工具路径检测：便携 `bin/` 优先，随后 PATH，再回退 MKVToolNix 默认安装位置。
- 工作区日志、重新检测环境。
- 可供后续工具调用复用的异步进程模块：参数列表传递、stdout/stderr 分流、Unicode 输出、有界捕获与受控子进程收尾。
- 控制台冒烟与阶段一验收入口：真实子进程调用、取消、路径优先级、Avalonia 编译绑定、导航、状态保留、真实工具 fixture 与真实 UI 验收。

Windows `Process.Start()` 和后续 Assign Job 之间存在非原子启动窗口。阶段一覆盖了当前同步 AFS/MKV 工具和受控进程取消/管道收尾场景，但未覆盖 launcher 在归属前快速创建后代或退出；不能据此承诺任意 launcher 的完整进程树终止。该启动竞态是阶段二 P2 可靠性事项。

环境检测当前只确认文件位置，不执行工具或核验版本，不宣称所有依赖已满足。AFS 的 Python/fonttools 后端检测、版本检测及组件安装尚未迁移。

## 后续阶段

字幕自动匹配与体检、预设持久化、批量队列、字幕提取、预览、章节编辑、快速修补、自动安装与后处理命令。界面中的待迁移入口明确显示状态，没有模拟封装成功。

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
# 低成本真实 ffmpeg/AFS/MKVToolNix 颜色告警 fixture：
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-real
# 真实 Regular-only 字体粗体请求 fixture，或附 task.log 对只读原始请求重放：
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-synthetic-bold-real
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-synthetic-bold-real 'artifacts/runs/<task-id>/task.log'
# 可选：从 Core task.log 读取只读源输入，ASS 以字节一致副本重放，输出仍隔离到新目录：
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-real 'artifacts/runs/<task-id>/task.log'
# 对上一步报告的 phase1 run-id 使用真实 MainWindow 和 Core 执行 UI 验收并保存截图：
$fixture = 'artifacts/phase1/<run-id>'
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --ui-integration $fixture (Join-Path $fixture 'real-ui.png')
```

Smoke 与截图的本次运行文件只写入 `desktop/artifacts/smoke-<run-id>/`；端到端验收 fixture、工具 stdout/stderr、截图、报告与最终测试输出写入 `desktop/artifacts/phase1/<run-id>/`。Core 每次任务的原始完整日志位于 `desktop/artifacts/runs/<task-id>/task.log`，集成 harness 会把链路用例的任务日志副本放进该 run 的 `task-logs/`。这些 artifacts 是本机忽略目录，不随 Git 提交；上述链接指向本次工作站证据，重新验收会生成新 run-id。Smoke 或集成用例失败返回非零退出码。端到端验收依赖 ToolLocator 可用的真实 ffmpeg、mkvmerge 和 AssFontSubset/PyFontTools；工具缺失表示尚未验收。自动与真实工具链验收已通过；严格人工验收仍待原生文件选择器与播放器播放检查。Windows Job Object 启动归属竞态仍列为阶段二 P2，不承诺任意 launcher 的进程树终止。

开发前读取 [AGENTS.md](AGENTS.md)。架构、开发阶段与验收要求见 [架构导览](ARCHITECTURE.md)。

## 发布方向

后续以 `win-x64` 自包含发布为主，让用户不必安装 .NET SDK。当前交付阶段一开发版本，尚未制作发布包或移除字体工具的运行时依赖。

参考：[Avalonia MVVM 项目结构](https://docs.avaloniaui.net/docs/get-started/create-your-first-project)、[编译绑定](https://docs.avaloniaui.net/docs/data-binding/compiled-bindings)、[固定包版本](https://www.nuget.org/packages/Avalonia.Desktop/12.1.2)。
