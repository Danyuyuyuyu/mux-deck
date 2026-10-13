# 架构分层重构

按 [ADR-0001](../adr/0001-layered-architecture.md) 将 v2 从 `MuxDeck.Core` / `MuxDeck.Desktop` 两项目迁移为 Domain / Application / Infrastructure / Desktop 四层。分层、依赖与契约规则以[代码骨架](../agents/skeleton.md)为准，阶段位置与完成条件以[开发流程](../agents/migration.md#架构分层重构)为准；本文是逐步骤的实施计划与结果记录。下文“PR”指一个实施步骤（如 1b），按 [AGENTS.md](../../AGENTS.md#提交与分支) 直接提交到 `v2/avalonia-rewrite`，不另开 PR。

## 范围

- 输入：`v2/avalonia-rewrite` 当前代码（基线提交 `3f42228`）与现有 Smoke、UI、真实工具链验收。
- 输出：四个生产项目、按层测试项目与架构测试；单任务流水线、草稿会话、操作协调与集中装配落位；`MuxDeck.Core` 移除。
- 范围外：用户可见行为、工具参数、QC 判定与诊断白名单不变；阶段二进程启动归属竞态（P2）在本重构完成后处理；不新增功能。
- 失败行为：任一步骤的构建、架构测试或既有回归在本地失败即不推送，推送后 CI 失败即修复或回退，未恢复前不开始下一步；真实工具链对照不一致时停止后续步骤，先定位差异来源。

## 实施原则

1. **先改命名空间，再移动文件**：对应项目建立前，代码先在 Core 内改为目标命名空间（`MuxDeck.Domain`、`MuxDeck.Contracts`、`MuxDeck.Application`、`MuxDeck.Infrastructure`），搬迁时 `git mv` 保留历史。
2. **旧入口兼容**：Smoke 有 16 处直接构造 `SingleMuxService`、7 个文件使用 `IMuxToolRunner`。迁移期间 `SingleMuxService` 保留为旧入口，内部转换为新契约并调用新用例，旧回归不改；第 5 步移除。
3. **每个步骤可独立提交**：推送前本地 Release 无告警/错误、既有回归不退化；推送后 CI 通过。
4. **只搬迁不修复**：发现旧缺陷记录在本文“发现的旧缺陷”，单独提交修复。
5. **同步骨架**：搬迁哪部分，就在同一 PR 中改写 `skeleton.md` “迁移前现状”对应条目。

## 依赖顺序

```text
1a → 1b → 2a / 2b / 2c（可并行）→ 3a → 3b → 3c → 3d → 3e → 4a → 4b → 4c → 4d → 5
```

高风险步骤为 **3c**（流水线改写）与 **4b**（草稿会话），推送前须经用户确认，分别要求真实工具链对照与 UI 截图对照。

## 第 1 步：骨架与护栏

### 1a 项目骨架（小）

- 新建 `src/MuxDeck.Domain`、`src/MuxDeck.Application`、`src/MuxDeck.Infrastructure`，加入 `MuxDeck.sln`。
- 引用：Application → Domain；Infrastructure → Application、Domain；过渡期 Core → Domain、Application；Desktop 增加 → Application。
- 新建 `tests/MuxDeck.Domain.Tests`、`tests/MuxDeck.Application.Tests`、`tests/MuxDeck.Architecture.Tests`：xUnit；架构测试使用 NetArchTest（需检查命名空间级规则，如 ViewModels 不使用 Infrastructure，仅项目引用无法覆盖）。
- 新包需联网 restore 一次后提交各项目 `packages.lock.json`；`--locked-mode` 保持可用。
- `.github/workflows/desktop.yml` 增加 `dotnet test` 步骤。
- 验证：Release 构建、Smoke、架构测试、CI。

### 1b 契约（中）

- Domain 值类型：`TrackKind`、`LanguageTag`、`TrackFlags`、`TrackId`。
- `Application/Contracts/`：`MuxJobSpec` 及子规格、`MuxJobResult`、`CheckResult`、`JobFailure`、`Diagnostic`、`JobProgress`、`JobStage`、`ErrorCodes`、`MediaProbeResult`、`InputItem`、`ImportPlan`、`IRunSingleMux`、`IProbeMedia`、`IPrepareImport`、`IManageComponents`、`OperationKind`、`IOperationLease`、`IOperationCoordinator`。
- Core 新增旧模型转换：`MuxRequest ↔ MuxJobSpec`、`MuxResult → MuxJobResult`，附转换测试。迁移期间旧文本检查项映射为单一过渡码，第 3 步改为结构化检查。
- 验证：Release 构建、转换测试、架构测试、Smoke。

## 第 2 步：抽取 Domain 规则

| PR | 迁出内容（迁移前位置） | Domain 落位 |
|---|---|---|
| **2a 轨道与命名**（小） | SC/TC 默认轨（`SingleMuxViewModel.cs` 默认规则）、纳入策略（`TrackRowViewModel.InclusionBox`）、文件名语言推断（`MediaImport.cs` `ImportedLanguage`）、`SingleMuxService.LanguageMatches`、`ResolvedOutputFileName` | `TrackPolicy`、`LanguageRules`、`OutputNaming` |
| **2b 输入识别与接纳**（小） | `MediaFileTypes`、`IsFont`；接纳中的完整路径、去重、数量、类型判定 | `InputRules` |
| **2c 诊断与 QC**（中） | `AfsDiagnosticClassifier`；`SingleMuxService.CheckTracks`；ASS 换行归一与全文比较；`AssGlyphValidator` 的 ASS 解析部分 | `DiagnosticPolicy`、`OutputQcRules`、`AssDocument` |

- 文件存在性等 IO 事实不进 Domain：由 Application 经端口收集后作为参数传入。
- `AssGlyphValidator` 拆分：ASS 样式/覆盖字体/码点统计归 Domain；ttx 字体表读取在第 3 步归 Infrastructure。
- 验证：新增规则单元测试（用例取自既有 Smoke 场景，含关键反例）；Smoke、UI、接纳回归；2c 另跑 `--warning-policy`。

## 第 3 步：单任务流水线与操作协调

### 3a 端口（小）

在 Application 定义 `IMediaProber`、`IFontSubsetter`、`IMuxer`、`IMediaExtractor`、`IFontInspector`、`IArtifactPublisher`、`ITaskWorkspaceFactory`、`IToolCatalog`、`IClock`。

### 3b Infrastructure 适配（大）

- **用户已批准的 3b 调整**（先记录，再实施）：
  1. 新增过渡引用 Core → Infrastructure，第 5 步移除；同步 skeleton 过渡表与 ProjectDependencyTests。Infrastructure 不引用 Core / Desktop / Avalonia 的护栏保持不变。
  2. 下表列出的 Desktop 直接依赖在本步留在 Core，标记为 **4a 迁移**（4a 替换 Desktop 的直接依赖与装配；旧单任务模型的兼容入口仍按 3d/5 处理，不把 Core 模型反向引入 Infrastructure）。其余进程/工具能力按本节迁入 Infrastructure；AfsDiagnosticClassifier 和字体 XML/字形校验辅助随其适配器搬迁，纯规则仍调用 Domain。
  3. Smoke **仅改命名空间引用**，不改测试逻辑、断言、用例或预期计数；旧 ProcessRunner / WindowsProcessJob 进程树用例完整保留。

| Desktop 装配入口外仍直接使用的 Core 类型 | 检索位置（src/MuxDeck.Desktop） | 本步处置 |
|---|---|---|
| StoragePaths | App.axaml.cs、Preferences/UserPreferencesStore.cs、StorageSettingsStore.cs、ViewModels/MainWindowViewModel.cs、EnvironmentViewModel.cs、EnvironmentComponentViewModel.cs | 留 Core，4a 迁移 |
| ToolLocator、ToolStatus | ViewModels/EnvironmentViewModel.cs、EnvironmentComponentViewModel.cs、MainWindowViewModel.cs | 留 Core，4a 迁移；共用路径查找辅助移入 Infrastructure |
| Workspace | App.axaml.cs、ViewModels/MainWindowViewModel.cs、EnvironmentViewModel.cs、SingleMuxViewModel.cs | 留 Core，4a 迁移；向已存在 ToolConfiguration 做过渡转换 |
| ComponentInstaller/Verifier/Uninstaller、IComponentInstaller/Verifier/Uninstaller、ComponentDirectories/Catalog | ViewModels/EnvironmentViewModel.cs、EnvironmentComponentViewModel.cs | 留 Core，4a 迁移其 Desktop 依赖 |
| ComponentState/InstallPhase/Definition/Inspection/InstallProgress/RemovalKind/RemovalPlan/RemovalProgress/InstallException/RemovalException | ViewModels/EnvironmentViewModel.cs、EnvironmentComponentViewModel.cs | 留 Core，4a 迁移其 Desktop 依赖 |
| SingleMuxService、MuxRequest/Result/Status/Stage/Progress、SubtitleInput、AudioInput、MediaProbe/Track/Attachment | ViewModels/MainWindowViewModel.cs、SingleMuxViewModel.cs、MediaImport.cs、Views/SingleMuxInteractions.cs、SingleMuxView.axaml.cs | 本步留 Core，4a 替换直接依赖；旧入口/模型兼容按 3d/5 |
| MediaFileTypes、SubtitleText | ViewModels/MediaImport.cs、SingleMuxViewModel.cs | 本步留 Core，4a 替换直接依赖 |



- 整体移动：`ProcessRunner`、`WindowsProcessJob`、`IMuxToolRunner`/`MuxToolRunner`、`AfsDiagnosticCollector`、`FontToolsLocator`；原计划的 `ToolLocator`、`StoragePaths` 依上述用户批准调整留 Core，到 4a 处理。
- 从 `SingleMuxService` 私有方法拆出：

| 来源 | 适配 |
|---|---|
| `ParseProbe` 与 `mkvmerge -J` | `MkvmergeProber` |
| `LocateFontTools`、`LocateSourceFont` 与 AFS 参数 | `AfsSubsetter` |
| mkvmerge 参数构建 | `MkvmergeMuxer` |
| mkvextract 提取 | `MkvextractExtractor` |
| ttx 字体表读取 | `TtxFontInspector` |
| 同目录临时文件与不覆盖 rename | `NoOverwritePublisher` |
| `TaskLog`、`DeleteWorkDirectory`、`TryDelete` | `FileTaskWorkspace` |

- 验证：Release 构建、架构测试、Infrastructure 集成测试（真实进程参数/输出/取消）、Smoke。

### 3c 单任务用例（大，高风险）

- `SingleMuxService.RunAsync`（迁移前约 260 行）改写为 `RunSingleMuxUseCase`：检查输入 → 探测源 → 子集化 → 生成封装计划 → 封装临时成品 → 成品校验 → 不覆盖落位，阶段间用 record 传递。
- 工具与工作目录配置随任务快照传入，删除 `UpdateWorkspace`。
- QC 输出结构化 `CheckResult`；失败输出 `JobFailure`（含阶段与错误码）。
- Application 测试：假端口覆盖各阶段失败、取消、清理、目标已存在与落位期间新出现目标。

### 3d 旧入口（小）

`SingleMuxService` 只保留旧模型转换并调用新用例；Desktop 与 Smoke 不改。

3c/3d 合并前真实工具链对照：

- 运行 `--integration`、`--media-drop-real`、`--ui-integration`、`--warning-real` 与完整电影重放，与基线 `3f42228` 的报告对照。
- 对照项：逐轨元数据（类型、编码、语言、名称、default/forced）、附件身份与 SHA-256、ASS 提取全文、音频 fullraw 哈希、检查项与告警。
- 不比较成品 MKV 文件哈希：mkvmerge 每次写入的日期与 UID 不同。

### 3e 操作协调与组件（中）

- 实现 `IOperationCoordinator`（内存租约），替换 `ComponentOperations.Gate`。
- 组件检测/安装/卸载经 `IManageComponents`；`ComponentVerifier`、`ComponentInstaller`、`ComponentUninstaller`、`ComponentDirectories`、`ComponentReleases` 移入 Infrastructure；`AssfontsInstaller` 兼容入口随第 5 步评估移除。
- 验证：组件受控、安装 UI、卸载、卸载 UI、环境 UI 回归；固定缓存真实卸载循环 `--component-uninstall-real`。

## 第 4 步：拆分 Desktop

### 4a 集中装配（中）

- 新增 `Composition/AppComposition.cs` 创建对象图，`Program` 只调用它；移除静态 `App.Workspace`、`App.Storage`。
- `EnvironmentViewModel` 等改为构造函数注入，不再创建默认依赖。
- `StorageSettingsStore` 移入 Infrastructure；界面偏好 `UserPreferencesStore` 留在 Desktop。

### 4b 草稿会话 `MuxDraft`（大，高风险）

- 从 `SingleMuxViewModel` 迁出：轨道集合、SC/TC 槽位、`DraftRevision`、编辑锁、准备→提交、`ToSpec()`。
- Application 不引用 Avalonia：草稿以普通 C# 事件（携带修订号）通知变化，ViewModel 订阅后投影。
- 验证：Application 草稿测试；接纳 88/89、UI 176、工作台 38、输入反馈 24；两尺寸截图对照。

### 4c 拆分 ViewModel（大）

- 拆出 `VideoSummaryViewModel`、`TrackListViewModel`、`TrackInspectorViewModel`、`OutputSettingsViewModel`、`RunPanelViewModel`、`ResultDetailsViewModel`。
- `SingleMuxViewModel` 保留为页面组合 ViewModel，暂时转发旧属性；XAML 绑定与 `UiSingleMuxContractChecks` 按区域逐块迁移。

### 4d 结果与关闭（中）

- 结果详情展示结构化检查项、诊断与失败阶段，不解析文案。
- 跨页面忙碌状态从协调器读取；`MainWindow` 关闭只等待 `IOperationCoordinator.ShutdownAsync()`。
- 验证：UI 全套、`--ui-integration`、1040×760 / 1280×900 截图检查。

## 第 5 步：收尾（中）

- 删除 `MuxDeck.Core`、旧模型、转换类与 `SingleMuxService` 旧入口；`IMuxToolRunner` 收为 Infrastructure 内部。
- Smoke 改为通过用例与契约调用。
- 改写 `skeleton.md` “迁移前现状”、两份 README 与 `verification.md` 计数。
- 完成后进入阶段二 P2：在 Infrastructure 以假进程端口覆盖极快 launcher 场景。

## 验收

整体完成条件以[开发流程](../agents/migration.md#架构分层重构)为准。本节给出对照基准、每步通用清单和各步骤的完成标准；每步全部满足才可推送，并在下方“实施结果”逐步骤记录提交链接、实际命令与计数、真实工具链对照结论和未覆盖项。

### 对照基准

“既有回归不退化”指计数与下表一致，且全部通过。计数取自重构开始前最近一次记录；受控套件已在 CI 运行（[提交 `65da1f7` 的运行](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38377836030)全部通过）。第一个实际运行某套件的步骤，须把实测计数写入结果表；与下表不同时先说明原因，确认后以实测值为新基准。

| 套件 | 实际命令 / 参数 | 基准 | 运行位置 | 本轮日期 / 计数 | 工具版本 / 证据 / 备注 |
|---|---|---|---|---|---|
| Smoke | （无参数） | 28/28 | CI + 本地 | — | — |
| 分层与架构测试 | `dotnet test` | 32/32（1a） | CI + 本地 | — | — |
| 告警策略 | `--warning-policy` | 57/57 | CI + 本地 | — | — |
| 单个封装接纳 | `--single-mux-round1` | 89/89 | CI + 本地 | — | — |
| 单个封装 UI | `--single-mux-ui-round1` | 176/176 | CI + 本地 | — | — |
| 工作台 | `--workbench-ui` | 38/38 | CI + 本地 | — | — |
| 输入反馈 | `--input-feedback` | 24/24 | CI + 本地 | — | — |
| 自动导入（受控） | `--media-drop` | 25/25 | CI + 本地 | — | — |
| 环境 UI | `--environment-ui` | 23/23 | CI + 本地 | — | — |
| 组件检测（受控） | `--components` | 31/31 | CI + 本地 | — | — |
| 组件安装 UI | `--component-install-ui` | 37/37 | CI + 本地 | — | — |
| 组件安装（受控） | `--component-install-all` | 41/41 | CI + 本地 | — | — |
| 组件卸载（受控） | `--component-uninstall` | 26/26 | CI + 本地 | — | — |
| 组件卸载 UI | `--component-uninstall-ui` | 61/61 | CI + 本地 | — | — |
| 真实工具链 | `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --integration` | 121/121 | 仅本地 | 2026-10-13：121/121，失败 0 | MKV 100.0 / FFmpeg 8.1.1 / AFS 2.1.1 / fontTools 4.61.1 / Python 3.12.10；[报告](../../artifacts/phase1/20261013-142859-82b3d25cf1e54a029ee505f2922a67b2/report.json) |
| 真实多轨导入 | `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --media-drop-real` | 37/37 | 仅本地 | 2026-10-13：37/37，失败 0 | MKV 100.0 / FFmpeg 8.1.1 / AFS 2.1.1 / fontTools 4.61.1 / Python 3.12.10；[报告](../../artifacts/automatic-drop/real-1f9252ddf7484002b912bd2b8ce067db/report.json) |
| 真实 UI | `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --ui-integration artifacts/phase1/20261013-142859-82b3d25cf1e54a029ee505f2922a67b2 artifacts/arch-corrections/real-baseline/new-ui.png` | 39/39 | 仅本地 | 2026-10-13：39/39，失败 0 | MKV 100.0 / FFmpeg 8.1.1 / AFS 2.1.1 / fontTools 4.61.1 / Python 3.12.10；[报告](../../artifacts/single-mux-round1-b/real-0894ff063db94e0c9d57b4d3358cd596/report.json) |
| 完整电影重放 | `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-synthetic-bold-real artifacts/runs/20261009-115919-18aeaf51f58d43e5a4b935b9ce8b8f5b/task.log` | 9/9 | 仅本地 | 2026-10-13：9/9，失败 0，SyntheticBoldExpected=true | MKV 100.0 / FFmpeg 8.1.1 / AFS 2.1.1 / fontTools 4.61.1 / Python 3.12.10；[9 项报告](../../artifacts/warning-policy/real-99bfc72a8373477dacf51a8e00524b70/report.json)。原表配错命令，是文档错误、不是回归；本次 `--warning-real` 的 [8/8](../../artifacts/warning-policy/real-2a100db1e6e6472cacd2ec5f22286dd7/report.json) 保留为补充记录，不替代 9 项基准。 |
| 真实组件安装 | `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-all-real` | 15/15 | 仅本地，需联网 | 2026-10-13：15/15，失败 0 | 受管 MKV 101.0 / FFmpeg 8.1.1-essentials_build / AFS 2.1.1 / fontTools 4.61.1 / Python 3.12.10；[报告](../../artifacts/arch-corrections/real-baseline/component-install-all-real-report.json)、[实际版本](../../artifacts/arch-corrections/real-baseline/installed-tool-versions.json) |
| 真实卸载循环 | `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall-real artifacts/arch-corrections/real-baseline/packages` | 17/17 | 仅本地 | 2026-10-13：17/17，失败 0 | 受管 MKV 101.0（系统回退 100.0）/ FFmpeg 8.1.1-essentials_build / AFS 2.1.1 / assfonts 0.7.3 / fontTools 4.61.1 / Python 3.12.10；[报告](../../artifacts/arch-corrections/real-baseline/component-uninstall-real-report.json)、[七包校验](../../artifacts/arch-corrections/real-baseline/package-manifest-verified.json) |

真实工具链基准须在开始 3b 前，于当时的分支头重跑一遍并记录到结果表，作为 3b–3e 的对照起点；报告文件保留在本机 `artifacts/`。

### 每步通用清单

1. **范围**：只改本步骤列出的文件；共享文件（`MuxDeck.sln`、锁文件、`Directory.Build.props`、`docs/agents/`）的修改在提交正文中逐项说明。
2. **本地验证**：`dotnet restore --locked-mode` 成功；Release 构建 0 警告 / 0 错误；`dotnet test` 全部通过；表中“CI + 本地”套件全部等于基准。
3. **CI**：推送后 Desktop 工作流通过；失败时立即修复或 `git revert`，恢复前不开始下一步。
4. **计数变化**：只允许因新增用例而增加，或因迁移用例而在套件间转移（注明来源与去向）；任何减少都必须说明原因，不能静默删除用例。
5. **护栏不放宽**：不新增架构测试例外、跳过或条件豁免；确需过渡例外时注明原因及在哪一步移除。
6. **行为不变**：用户可见文案、默认值、外部工具参数、QC 判定与[诊断白名单](../agents/diagnostics.md)不变；`diagnostics.md` 无改动。
7. **一份实现**：迁出的规则或流程在原位置不保留副本，原位置只剩调用。
8. **文档同步**：更新本文结果表对应行；搬迁哪部分，就改写 `skeleton.md` “迁移前现状”的对应条目。
9. **提交**：提交摘要带步骤号，正文含 `Validation:`（实际命令与计数）；发现的旧缺陷写入“发现的旧缺陷”，不混入本步骤。

### 各步骤完成标准

| 步骤 | 完成标准（在通用清单之外） |
|---|---|
| **1a** | 三个空生产项目与三个测试项目建立；`skeleton.md` 每条依赖规则至少 1 个架构测试，且每条都经本地故意违反确认会失败；CI 执行 `dotnet test`（已完成，见结果表） |
| **1b** | 契约位于 `src/MuxDeck.Application/Contracts/`、命名空间 `MuxDeck.Contracts`，值类型位于 Domain；新增架构测试：Contracts 只依赖 `System.*` 与 Domain，类型均为 public，并经故意违反确认；转换测试覆盖 `MuxRequest ↔ MuxJobSpec` 往返（字幕/音频集合、语言、default/forced、输出名为空）及 `MuxResult → MuxJobResult` 的成功/失败/取消各至少 1 例，旧文本检查项原文保留；除新增转换类外，现有调用方零改动 |
| **2a** | 默认轨、纳入策略、语言推断、`LanguageMatches`、输出文件名规则均有 Domain 单元测试，每条规则至少 1 个有效输入和 1 个关键反例，并注明对应的既有 Smoke 场景；新增架构测试：Domain 不使用文件、目录、进程、时间等副作用 API |
| **2b** | `MediaFileTypes`、字体扩展名识别与接纳中的路径/去重/数量/类型判定迁入 `InputRules` 并有单元测试；文件存在性仍在 Domain 之外，判定只接收传入的事实；接纳 89/89 与自动导入 25/25 等于基准 |
| **2c** | 诊断分类、`CheckTracks`、ASS 归一与全文比较、ASS 解析迁入 Domain 并有单元测试，含 Dialogue 文本/时序/Style/字体/顺序/末尾空格变化的拒绝反例；`--warning-policy` 57/57；`diagnostics.md` 白名单未变 |
| **3a** | 端口接口全部位于 Application，名称表达业务能力；无通用仓储、事件总线、中介者或万能任务接口；Application 不引用 Infrastructure 的架构测试保持通过 |
| **3b** | 每个适配有 Infrastructure 集成测试：参数以列表传递、stdout/stderr 分流、退出码、取消；`ProcessRunner`/`WindowsProcessJob` 原有进程树用例保留通过；工具调用代码搬迁后，本地 `--integration`、`--media-drop-real` 等于基准 |
| **3c** | 七个阶段各有 Application 假端口测试，覆盖成功、本阶段失败（`JobFailure.Stage` 与 `Code` 正确）、取消、临时目录无残留、目标已存在、落位期间新出现目标；`UpdateWorkspace` 删除，并有“运行中修改配置不影响已提交任务”的测试；全部“仅本地”真实套件等于基准，且[3c/3d 对照项](#3d-旧入口小)逐项一致，对照报告路径写入结果表；**推送前经用户确认** |
| **3d** | `SingleMuxService` 只含旧模型转换与对用例的调用；Desktop 与 Smoke 零改动；全部受控与真实套件等于基准 |
| **3e** | `ComponentOperations.Gate` 删除，所有互斥经 `IOperationCoordinator`；协调器测试覆盖同类/异类互斥矩阵、异常与取消后的租约释放、`ShutdownAsync` 等待全部租约；组件与环境相关套件等于基准；本地 `--component-uninstall-real` 17/17 |
| **4a** | `App.Workspace`、`App.Storage` 删除；新增架构测试：ViewModels/Views 不引用 Infrastructure，装配只在 `Program`/`Composition/`；`--root` 便携模式与默认存储路径行为不变（有测试或记录手动验证）；全部 UI 套件等于基准 |
| **4b** | `MuxDraft` 测试覆盖：同值赋值不增修订号、一次接纳批次只增一次、准备→提交、运行中锁定拒绝、`ToSpec()` 快照与后续修改隔离；ViewModel 中不再有 SC/TC 默认规则等业务代码；UI、接纳、工作台、输入反馈、自动导入套件等于基准；1040×760 与 1280×900 截图与基线逐张对照无差异（差异须逐项说明）；本地 `--ui-integration` 等于基准；**推送前经用户确认** |
| **4c** | `SingleMuxViewModel` 只做组合与过渡转发；六个区域 ViewModel 的绑定均为编译绑定且构建通过；可访问名称不变；全部 UI 套件等于基准 |
| **4d** | 结果详情直接展示结构化检查项、诊断与失败阶段，代码中不再对检查文本做字符串匹配；`MainWindow` 关闭只等待 `IOperationCoordinator.ShutdownAsync()`；关闭等待相关用例通过；两尺寸截图检查 |
| **5** | `MuxDeck.Core`、旧模型、转换类与旧入口删除；架构测试无过渡例外；全部受控与真实套件等于基准（计数变化逐项说明）；`skeleton.md` “迁移前现状”改写为现状、两份 README 与 `verification.md` 已更新 |

## 实施结果

| PR | 状态 | 验证 | 备注 |
|---|---|---|---|
| [1a · PR #3](https://github.com/Danyuyuyuyu/mux-deck/pull/3) | 已合并（`c4f932e`） | 锁定 restore：9/9 项目；Release build：0 警告 / 0 错误；dotnet test：32/32（架构规则 27 + 扫描反例 5），失败/跳过 0；Smoke：28/28 | 三个空生产项目、三个 xUnit 测试项目、过渡引用、NetArchTest 护栏、CI test 步骤与全部新增项目锁文件；按已接受 ADR-0001 实施，未移动代码、定义契约或修改行为。Domain/Application 业务测试 0；未重放真实工具链或原生 UI。 |
| [1b · `aa85458`](https://github.com/Danyuyuyuyu/mux-deck/commit/aa85458a3caf7b11e76ae5f7d60317d3c862ffe8) | 已完成，已推送，Desktop CI 通过 | restore 10/10；Release 0 警告 / 0 错误；dotnet test 71/71（转换 26、Domain 8、架构 37）；受控 13/13 套件、656/656；契约两条故意违反均被拒绝 | 公共不可变契约、四个 Domain 值类型与 Core 兼容转换；旧调用方零改动；详见下方命令与计数。未重放真实工具链或原生 UI。 |
| [2a · `34c4c18`](https://github.com/Danyuyuyuyu/mux-deck/commit/34c4c1827169270c715a955fe027a0bd6622509d) | 已完成，已推送，[Desktop CI 通过](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38390147903) | locked restore 10/10；Release 0 警告 / 0 错误；dotnet test 208/208；受控 13/13、656/656 | TrackPolicy、LanguageRules、OutputNaming 已迁入 Domain；新增 Domain 69、架构 3，原 136 全部保留；详见 2a 实际验证。 |
| [2b · `90fff6c`](https://github.com/Danyuyuyuyu/mux-deck/commit/90fff6c47d8a3fcef6c509fa81ec91f80425b865) | 已完成，已推送，[Desktop CI 通过](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38393673757) | locked restore 10/10；Release 0 警告 / 0 错误；dotnet test 343/343；受控 13/13、656/656；接纳 89/89、自动导入 25/25 | InputRules 已落位；新增 Domain 134、架构 1，原 208 保留；IO 事实留边界，原入口差异不变；详见 2b 实施与验证。 |
| [2c · `90fea6b`](https://github.com/Danyuyuyuyu/mux-deck/commit/90fea6b02e421105cd3c7bdb45c69c4c69d1df8e) | 已完成，已推送，[Desktop CI 通过](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38400687784) | restore 10/10；Release 0 警告 / 0 错误；test 427/427；受控 13/13、656/656；warning-policy 57/57 | DiagnosticPolicy、OutputQcRules、AssDocument 已落位；新增 Domain 76、架构 3；原 348 保留，白名单未变。 |
| [3a · `da0f334`](https://github.com/Danyuyuyuyu/mux-deck/commit/da0f3342f2b50bccfdcf31f19212ee3bb63e9f91) | 已完成，已推送，[Desktop CI 通过](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38406784353) | locked restore 10/10；Release 0 警告 / 0 错误；test 485/485；受控 13/13、656/656；warning-policy 57/57 | 九个 Application 业务端口；Domain 统一 MediaTrackInfo，删除重复 TrackQcFacts；新增 29，原 456 全部保留；详见 3a 实施与验证。 |
| [3b · `a2b76d9`](https://github.com/Danyuyuyuyu/mux-deck/commit/a2b76d9588646265bfab68d0521928c8f881f94c) | 已完成，已推送，[Desktop CI 通过](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38427342392) | locked restore 11/11；Release 0 警告 / 0 错误；test 601/601；受控 13/13、656/656；真实 integration 121/121、media-drop-real 37/37 | 依用户批准保留 Desktop 直接依赖；Core → Infrastructure 过渡；新增 47，原 554 保留。 |
| 3c | 未开始 | | |
| 3d | 未开始 | | |
| 3e | 未开始 | | |
| 4a | 未开始 | | |
| 4b | 未开始 | | |
| 4c | 未开始 | | |
| 4d | 未开始 | | |
| 5 | 未开始 | | |

### 1a 实际验证（2026-10-13）

输入基线为 `e360510`（包含 `7133b8c` 与 `e360510` 的 ADR/开发约定更新）。依照已接受的 [ADR-0001](../adr/0001-layered-architecture.md) 建立骨架，不修改 ADR。输出、失败行为与验收范围按上方 1a：依赖或既有回归失败即停止；本轮 NuGet 联网 restore 成功，未发现现有代码违反架构规则。

以下命令均在 `desktop/` 执行：

| 实际命令 | 结果 |
|---|---|
| `dotnet restore MuxDeck.sln` | 首次联网生成新锁文件，9/9 项目成功 |
| `dotnet restore MuxDeck.sln --locked-mode` | 9/9 项目成功 |
| `dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false` | 9/9 项目构建成功，0 警告 / 0 错误 |
| `dotnet test MuxDeck.sln --no-build -c Release` | Architecture.Tests 32/32，通过 32、失败 0、跳过 0；Domain.Tests / Application.Tests 各 0 业务用例 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release` | 28/28 PASS |

三层生产项目为空，命名空间护栏会在 1b 及后续迁移中继续生效；本轮不声称已完成业务分层或真实媒体工具链验收。CI 的最新结果见 [PR #3 检查](https://github.com/Danyuyuyuyu/mux-deck/pull/3/checks)。

#### 修正 C：收紧架构护栏

Application / Contracts 禁止 System.IO、System.Diagnostics.Process、System.Net；Domain 额外禁止 Console / Environment，Infrastructure 禁止 Avalonia。NoCurrentTimeRule 保留原规则并增加 Guid.NewGuid、Stopwatch（含泛型引用）、TickCount / TickCount64，同时检查 Domain / Application / Contracts 与编译生成的 lambda / async 嵌套类型。契约集合属性及公开集合字段采用 EquatableArray 白名单，string 与 static 类按用户规则处理；可写 setter / 字段仍拒绝。CompositionEntryRule 同时核对类型与 PDB 源码路径，可参数化根目录；缺 PDB、映射为 /_/ 或无法读取源码时返回明确失败原因，生产架构断言显示原因，不抛出未处理异常。

82 → 136，仅新增架构 54：生产护栏 3、时间/ID 15、禁止命名空间与纯数据反例 12、集合/可写数据与允许类型反例 13、装配/PDB 路径反例 11。原 82 项全部保留，Architecture 37 → 91，Domain 15 / Application 4 / Core 26 不变；没有跳过、删除或放宽原有用例。受控 13/13、656/656。提交 [7ed9493](https://github.com/Danyuyuyuyu/mux-deck/commit/7ed9493b8d649d3086ff031f91d6851f4124b638)，[Desktop CI 已通过](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38385963643)。

| 实际命令（desktop/） | 结果 |
|---|---|
| `dotnet restore MuxDeck.sln --locked-mode` | 10/10 成功 |
| `dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false` | 0 警告 / 0 错误 |
| `dotnet test MuxDeck.sln --no-build -c Release` | 136/136（Domain 15、Application 4、Core 26、Architecture 91），失败/跳过 0 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release` | 28/28 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-policy` | 57/57 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-round1` | 89/89 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-ui-round1` | 176/176 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --workbench-ui` | 38/38 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --input-feedback` | 24/24 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --media-drop` | 25/25 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --environment-ui` | 23/23 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --components` | 31/31 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-ui` | 37/37 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-all` | 41/41 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall` | 26/26 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall-ui` | 61/61 |

本机日志：`artifacts/arch-corrections/C/`。反例位于测试程序集，装配 fixture 与便携 PDB 只在本测试唯一 artifacts 子目录内生成并清理。共享文件仅骨架与本任务验证记录；ADR、步骤计划、sln、锁文件、props、生产代码均不变。

#### 修正 D：Environment 改为成员级禁令

授权：用户明确同意，授权范围仅此项。经用户同意放宽：Environment 整类型禁令改为成员级。原因：C# yield 状态机会调用 CurrentManagedThreadId，该只读成员应放行。输入基线 fa5065b；输出为 Domain 成员级护栏与反例，保留 MachineName 等既有反例和全部时间/ID 禁令。禁止环境变量读取/写入/展开、机器/用户身份、当前目录、特殊目录、命令行、退出/FailFast 与进程身份；CurrentManagedThreadId、NewLine、ProcessorCount 放行。TickCount/TickCount64 仍由 NoCurrentTimeRule 禁止。失败行为：任何构建、护栏或回归失败不推送；本项 CI 通过后才实施 3a。不修改其他架构规则或生产行为。



实际验证（在 desktop/）：427 → 456，仅新增 29 项，原 427 项全部保留；Architecture 98 → 127，新增整程序集扫描 1 项、成员反例 22 项、放行样例 4 项、保留时间禁令样例 2 项。受控 13/13 套件、656/656，与基准一致。日志：`artifacts/arch-corrections/1a-environment/`。实现提交 [ab01d98](https://github.com/Danyuyuyuyu/mux-deck/commit/ab01d987332b954eade08c7a15a2c78388c88caa) 已在本地完整验证后 pull --rebase 并推送；[Desktop CI](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38404977003) 完成 success，workflow、desktop job 及全部验证步骤通过，head_sha 已核对一致。确认通过后才开始 3a。

| 实际命令 | 结果 |
|---|---|
| `dotnet restore MuxDeck.sln --locked-mode` | 10/10 |
| `dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false` | 0 警告 / 0 错误 |
| `dotnet test MuxDeck.sln --no-build -c Release` | 456/456（Domain 294、Application 9、Core 26、Architecture 127）；失败/跳过 0 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release` | 28/28 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-policy` | 57/57 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-round1` | 89/89 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-ui-round1` | 176/176 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --workbench-ui` | 38/38 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --input-feedback` | 24/24 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --media-drop` | 25/25 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --environment-ui` | 23/23 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --components` | 31/31 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-ui` | 37/37 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-all` | 41/41 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall` | 26/26 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall-ui` | 61/61 |

#### 修正 E：Application / Contracts 放行 Path 纯字符串成员

授权：用户明确同意，范围仅限此项。经用户同意放宽：Application/Contracts 的 System.IO 禁令改为放行 Path 纯字符串成员。原因：Application 编排需要组合和拆分已给定的路径字符串，不应因此承担文件、目录或环境 IO。白名单仅 Combine、Join、GetFileName、GetFileNameWithoutExtension、GetExtension、GetDirectoryName、ChangeExtension、HasExtension、DirectorySeparatorChar、AltDirectorySeparatorChar；未列出的 Path 成员全部拒绝，尤其 GetFullPath、GetTempPath、GetTempFileName、GetRandomFileName。System.IO 其他类型，包括 File、Directory、FileInfo、DirectoryInfo、Stream/派生类、InvalidDataException 仍禁用；Domain 原命名空间禁令不变。输入为 1b27a94，输出为成员与签名级 IL 护栏、全部原有反例和新增逐成员/lambda/async 反例及放行样例；任何本地或 CI 失败不开始下一项。

实际验证：485 → 520，仅新增 Architecture 35（实际命名空间扫描 2、白名单/拒绝样例 32〔放行 12、拒绝 20〕、Domain 原禁令保留 1），原 485 项全部保留。严格构建 0 警告 / 0 错误，受控 13/13、656/656；日志 `artifacts/arch-corrections/1a-path/`。实现提交 [9a45de2](https://github.com/Danyuyuyuyu/mux-deck/commit/9a45de2c92f182e4015c984568317ad27103a5f2) 本地全部通过后 pull --rebase 并推送；[Desktop CI](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38410800344) 完成 success，head_sha 与实现提交一致，确认通过后才开始端口失败约定修正。

| 实际命令（desktop/） | 结果 |
|---|---|
| `dotnet restore MuxDeck.sln --locked-mode` | 10/10 |
| `dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false` | 0 警告 / 0 错误 |
| `dotnet test MuxDeck.sln --no-build -c Release` | 520/520（Domain 298、Application 10、Core 37、Architecture 175）；失败/跳过 0 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release` | 28/28 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-policy` | 57/57 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-round1` | 89/89 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-ui-round1` | 176/176 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --workbench-ui` | 38/38 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --input-feedback` | 24/24 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --media-drop` | 25/25 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --environment-ui` | 23/23 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --components` | 31/31 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-ui` | 37/37 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-all` | 41/41 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall` | 26/26 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall-ui` | 61/61 |



### 1b 实际验证（2026-10-13）

输入：分支头 `93a2c9b`、现有旧模型与已接受 ADR-0001。输出：Domain 四个值类型、Application 公共不可变契约、Core 新增转换类、转换/值类型/契约护栏测试；现有调用方零改动。失败行为：新规格若超出旧请求表达能力则明确拒绝；旧检查文案不猜测判定，旧失败不猜测阶段。验收场景包括双向往返、字幕/音频、全部 default/forced 组合、原语言、空输出名、成功/失败/取消、集合快照与无法表达的数据拒绝。

以下命令均在 `desktop/` 实际执行；本机详细命令/计数与各套件日志保存在 `artifacts/arch-1b/verification/summary.json` 和同目录文本日志（不随 Git 提交）。

| 实际命令 | 结果 |
|---|---|
| `dotnet restore MuxDeck.sln --locked-mode` | 10/10 项目成功 |
| `dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false` | 10/10 项目，0 警告 / 0 错误 |
| `dotnet test MuxDeck.sln --no-build -c Release` | 71/71：Core 转换 26、Domain 8、Architecture 37；失败/跳过 0，Application 流程用例 0 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release` | 28/28 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-policy` | 57/57 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-round1` | 89/89 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-ui-round1` | 176/176 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --workbench-ui` | 38/38 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --input-feedback` | 24/24 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --media-drop` | 25/25 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --environment-ui` | 23/23 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --components` | 31/31 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-ui` | 37/37 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-all` | 41/41 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall` | 26/26 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall-ui` | 61/61 |

计数由 1a 的 32 增至 71，仅新增用例：Core 转换 26、Domain 8、架构 5（Core.Tests 项目依赖 1、契约护栏 4）；原 32 项保留。受控套件 13/13、656/656，与基准一致。两个可构建的临时反例（非允许命名空间依赖、internal 契约）运行 `dotnet test tests/MuxDeck.Architecture.Tests --no-build -c Release --filter 'FullyQualifiedName~ContractBoundaryTests.Contracts_have_only_system_domain_and_contract_dependencies|FullyQualifiedName~ContractBoundaryTests.All_contract_types_are_public'`，如预期 2/2 拒绝；移除后最终全套 71/71 通过，未留例外、跳过或临时反例。

共享文件变更：`MuxDeck.sln` 增加只引用 Core 的转换测试项目；新增该项目锁文件，其他锁文件和 `Directory.Build.props` 无改动；`docs/agents/skeleton.md` 同步已落位契约与兼容约束。Core.Tests 随第 5 步移除旧转换时处理。未移动业务实现、改变旧调用方/工具参数/QC/诊断白名单；本轮未重放真实媒体工具链或原生 UI。

实现提交 [`aa85458`](https://github.com/Danyuyuyuyu/mux-deck/commit/aa85458a3caf7b11e76ae5f7d60317d3c862ffe8) 已按约定在本地验证后 `git pull --rebase origin v2/avalonia-rewrite` 并推送。[Desktop CI](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38380994829) 的唯一 desktop job 已完成且 conclusion 为 success：locked restore、build、分层/架构测试、Smoke 及全部 12 个带参数受控回归步骤均通过，失败产物上传步骤按预期跳过。此处 CI 证据对应实现提交；随后只补充结果链接的文档提交按工作流 paths 排除规则不重复触发构建。

#### 修正 A：限定 Avalonia 基类

Desktop 全部 C# 源码检索仅发现 App 的一处未限定基类，已改为 `Avalonia.Application`。在 Desktop 临时加入 `MuxDeck.Application.ApplicationNameProbe` 后 Release 构建成功（0 警告 / 0 错误），撤销后再执行下列完整验收；没有保留临时类型。原 71 个用例全部保留，计数不变；受控 13/13 套件、656/656。提交 [c636e91](https://github.com/Danyuyuyuyu/mux-deck/commit/c636e91d3825ceeba8b4d935b9e72503e12c4f56)，[Desktop CI 已通过](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38383347658)。

| 实际命令（desktop/） | 结果 |
|---|---|
| `dotnet restore MuxDeck.sln --locked-mode` | 10/10 成功 |
| `dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false` | 0 警告 / 0 错误 |
| `dotnet test MuxDeck.sln --no-build -c Release` | 71/71（Domain 8、Core 26、Architecture 37），失败/跳过 0 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release` | 28/28 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-policy` | 57/57 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-round1` | 89/89 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-ui-round1` | 176/176 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --workbench-ui` | 38/38 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --input-feedback` | 24/24 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --media-drop` | 25/25 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --environment-ui` | 23/23 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --components` | 31/31 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-ui` | 37/37 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-all` | 41/41 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall` | 26/26 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall-ui` | 61/61 |

本机日志：`artifacts/arch-corrections/A/`。不改 ADR、锁文件、解决方案、共享构建配置或任何测试。

#### 修正 B：集合值相等与值类型修正

Domain 新增 `EquatableArray<T>`，只读包装 ImmutableArray、default 在读取/枚举/元素相等/哈希中视为空，复制可变输入。所有契约集合与用例返回集合使用该类型；LanguageTag 按 Value 比较，default 等于空字符串且哈希一致；探测旗标存在性必须显式传入；ErrorCodes 为 static 常量类。旧转换的显式初始化校验仍保留，原转换测试不变。仅为用户指定的 static ErrorCodes 同步豁免 static 类记录检查，集合白名单在修正 C 收紧。

计数 71 → 82，仅新增 Domain 7（集合 6、语言 1）、Application 4（spec/result 内容相等、default 空集合、旗标必须显式）；原 71 个用例全部保留，Core 26 和 Architecture 37 不变。受控 13/13、656/656。提交 [899dec5](https://github.com/Danyuyuyuyu/mux-deck/commit/899dec5b1e39b0311aa3f3afc95aebb66b59067d)，[Desktop CI 已通过](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38384423798)。

| 实际命令（desktop/） | 结果 |
|---|---|
| `dotnet restore MuxDeck.sln --locked-mode` | 10/10 成功 |
| `dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false` | 0 警告 / 0 错误 |
| `dotnet test MuxDeck.sln --no-build -c Release` | 82/82（Domain 15、Application 4、Core 26、Architecture 37），失败/跳过 0 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release` | 28/28 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-policy` | 57/57 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-round1` | 89/89 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-ui-round1` | 176/176 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --workbench-ui` | 38/38 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --input-feedback` | 24/24 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --media-drop` | 25/25 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --environment-ui` | 23/23 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --components` | 31/31 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-ui` | 37/37 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-all` | 41/41 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall` | 26/26 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall-ui` | 61/61 |

本机日志：`artifacts/arch-corrections/B/`。共享文件仅骨架与本任务验证记录；sln、锁文件、props、ADR 均不变。

### 2a 实际验证（2026-10-13）

输入：在 `v2/avalonia-rewrite` 执行 `git pull --rebase origin v2/avalonia-rewrite`，确认最新基线含 `9a1d4e3`；沿用已接受的 [ADR-0001](../adr/0001-layered-architecture.md)。输出：Domain `TrackPolicy`、`LanguageRules`、`OutputNaming` 及原位置调用，新增规则单元测试与副作用架构检查。失败行为：构建、架构或任何既有回归失败均不推送；推送后 Desktop CI 不通过即修复或回退。原输入拒绝、QC 失败、取消和不覆盖落位语义不变。

规则均直接迁入 Domain，原位置仅保留调用；文件路径拆分和 IO 事实仍在调用方，文件名合法性校验仍由旧服务负责。SC/TC 默认仅在输入组合变化时调用，同槽替换、纳入切换不重算显式旗标；源 A/V 保留、源字幕与附件移除、外加纳入可编辑保持原样。语言推断保持 IETF 优先、标签边界、优先顺序与 und 的原大小写语义；匹配仅在 IETF 为 null 时回退。输出预览把空白视为自动名，服务只把 null 视为自动名，保留两者原有差异。工具参数、QC 判定和诊断白名单未改。

每条规则有效输入与关键反例的既有 Smoke 对应：

| Domain 测试 | 新增用例 | 既有场景与关键反例 |
|---|---|---|
| `TrackPolicyTests` 默认轨 | 5 | Smoke / `--single-mux-round1`：SC、TC-only、SC+TC；无输入不选择默认轨，旧空草稿初始 SC 标记保留 |
| `TrackPolicyTests` 源策略与纳入 | 11 | `--single-mux-round1` 源 A/V 保留与源字幕移除；`--single-mux-ui-round1` 只读源纳入列、外加可切换；源字幕不能因外加纳入值为 true 而保留，外加禁用不被源保留覆盖 |
| `LanguageRulesTests` 推断 | 26 | `--media-drop` 未指定语言从有边界的文件名标签推断、未知回退 und；已声明字段优先，无边界子串不推断，未声明 IETF 不回退传统字段 |
| `LanguageRulesTests` 匹配 | 14 | Smoke 公共封装 QC，以及 `--media-drop-real` 篡改新增音轨语言阻止发布；别名与大小写匹配、IETF 不匹配时禁止传统字段掩盖、zh-Hans / zh-Hant 不混用（这里只复用元数据事实，未重放真实套件） |
| `OutputNamingTests` | 13 | `--single-mux-round1` 中文空格大写 MKV 默认名、输出名编辑与 Smoke 成品命名；自定义名不覆写、空白服务名留给原校验拒绝、未知视频与空基名区分 |
| `DomainRuleBoundaryTests` | 3 | 逐个确认三个真实规则在 Domain 程序集，禁用文件/目录/进程/网络/Console/Environment/Avalonia 依赖，以及当前时间与 ID 生成 |

以下命令均在 `desktop/` 实际执行：

| 实际命令 | 结果 |
|---|---|
| `dotnet restore MuxDeck.sln --locked-mode` | 10/10 项目成功 |
| `dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false` | 10/10 项目，0 警告 / 0 错误 |
| `dotnet test MuxDeck.sln --no-build -c Release` | 208/208（Domain 84、Application 4、Core 26、Architecture 94），失败/跳过 0 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release` | 28/28 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-policy` | 57/57 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-round1` | 89/89 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-ui-round1` | 176/176 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --workbench-ui` | 38/38 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --input-feedback` | 24/24 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --media-drop` | 25/25 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --environment-ui` | 23/23 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --components` | 31/31 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-ui` | 37/37 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-all` | 41/41 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall` | 26/26 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall-ui` | 61/61 |

136 → 208，只新增 72 项：Domain 69（默认轨 5、纳入与源保留 11、语言推断 26、匹配 14、命名 13），架构 3；原 136 项全部保留，没有删改、跳过或放宽现有测试。受控 13/13 套件、656/656，与基准一致。本机日志：`artifacts/arch-corrections/2a/`。

共享文件只更新 `docs/agents/skeleton.md` 中本次迁移现状；`MuxDeck.sln`、锁文件、`Directory.Build.props` 不变，未改 ADR 或步骤计划。未重放真实媒体工具链、原生选择器、Explorer、高 DPI 或播放器；真实工具链基准仍需在 3b 前按上方要求重跑。实现提交 [`34c4c18`](https://github.com/Danyuyuyuyu/mux-deck/commit/34c4c1827169270c715a955fe027a0bd6622509d) 已在本地完整验证后执行 `git pull --rebase origin v2/avalonia-rewrite` 并推送。[Desktop CI](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38390147903) 已完成，conclusion 为 success，head_sha 与实现提交一致；locked restore、build、单元与架构测试、Smoke 及全部 12 个带参数受控回归步骤均通过，失败产物上传按预期跳过。随后仅补充实现提交与 CI 链接的文档提交，按工作流 paths 排除规则不重复触发构建。

### 2b 实施与验证（2026-10-13）

输入：最新 `v2/avalonia-rewrite`（含 2a 验收记录 `481fb4b`）及现有固定槽位/自动导入接纳实现。输出：Domain `InputRules` 集中类型识别、字体扩展名、完整路径事实校验、数量与去重判定；旧位置只收集路径规范化、存在性及媒体探测事实并调用规则。失败行为：保持原拒绝码、文案、优先顺序及全批次不修改草稿；准备和提交均重新核验，迟到、编辑锁和确认逻辑暂留 Desktop。验收场景：大小写扩展名、非本地/相对/设备/数据流路径、数量、重复与 SC/TC 整组交换、混合自动导入、多音轨同容器和单字体目录；全部既有受控套件计数保持基准。不改 ADR、步骤计划、工具参数、QC 或诊断白名单。


按已接受 [ADR-0001](../adr/0001-layered-architecture.md) 实施，不修订 ADR。原 `MediaFileTypes` 和 `IsFont` 仅保留扩展名提取与 Domain 调用；路径比较也只在边界规范化后调用 Domain。固定槽位与自动入口的原支持范围、设备路径处理差异和大小写语义均保留；准备/提交仍重新采集存在性事实，保持原拒绝优先顺序、文案、确认、迟到保护和草稿更新时机。不存在文件或目录仍由原边界拒绝，不把 IO 带入 Domain。

新增用例注明既有 Smoke 场景，均有有效输入和关键反例：

| Domain 测试 | 新增用例 | 既有场景与关键反例 |
|---|---|---|
| `InputTypeRulesTests` 扩展名/固定槽位 | 46 | `--media-drop` 混合类型与未知类型拒绝；`--single-mux-round1` 大写 MKV/ASS；覆盖原 30 种扩展名、大小写、未知后缀、备份后缀与固定槽位不接受 MOV/SRT |
| `InputTypeRulesTests` 候选类型/字体归组 | 12 | 文件/目录不可混用、非本地优先拒绝、字体文件归位父目录；保留自动入口原未知 kind 语义，不额外修正拒绝策略 |
| `InputTypeRulesTests` 探测类型 | 18 | `--media-drop` ASS/SRT、真实多音轨 ID、整批错误类型拒绝；无轨、多字幕轨、CodecId 与描述冲突、扩展名与实际视频轨不符 |
| `InputPathRulesTests` 路径 | 15 | `--single-mux-round1` 完整中文/空格路径、相对/无效路径拒绝，`--media-drop` 本地输入；UNC、空/null、设备路径入口差异、无效字符事实、数据流标记 |
| `InputPathRulesTests` 数量/槽位 | 17 | 空/多视频、多字体目录、1–2 ASS、同槽重复分配拒绝，自动批次最多一个源视频；非法槽位与过量反例 |
| `InputPathRulesTests` 比较/去重/字体/替换 | 26 | 同路径大小写接纳无害、同批重复拒绝、最终槽位集合与整组交换、替换确认；单字体目录、同容器不同音轨可导入、同轨或已加入字幕不可重复 |
| `InputRuleBoundaryTests` | 1 | 非空扫描真实 `InputRules`，禁止文件/目录/进程/网络/Console/Environment/Avalonia 以及时间/ID 副作用 API |

以下命令均在 `desktop/` 实际执行：

| 实际命令 | 结果 |
|---|---|
| `dotnet restore MuxDeck.sln --locked-mode` | 10/10 项目成功 |
| `dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false` | 10/10 项目，0 警告 / 0 错误 |
| `dotnet test MuxDeck.sln --no-build -c Release` | 343/343（Domain 218、Application 4、Core 26、Architecture 95），失败/跳过 0 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release` | 28/28 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-policy` | 57/57 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-round1` | 89/89 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-ui-round1` | 176/176 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --workbench-ui` | 38/38 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --input-feedback` | 24/24 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --media-drop` | 25/25 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --environment-ui` | 23/23 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --components` | 31/31 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-ui` | 37/37 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-all` | 41/41 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall` | 26/26 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall-ui` | 61/61 |

208 → 343，仅新增 135 项：Domain 134（类型规则 76、路径/数量/去重规则 58）、架构 1；原 208 项全部保留，没有修改、跳过、删除或放宽任何既有测试。受控 13/13 套件、656/656；接纳 89/89、自动导入 25/25 与基准一致。本机日志：`artifacts/arch-corrections/2b/`。

共享文件仅 `docs/agents/skeleton.md` 更新本步迁移现状与计数；`MuxDeck.sln`、`Directory.Build.props`、全部锁文件、ADR、步骤计划及 `diagnostics.md` 均不变。没有迁移草稿会话或修改媒体参数/QC；本轮未重放真实媒体工具链或原生选择器、Explorer、高 DPI、播放器，真实基准仍需在 3b 前重跑。实现提交 [`90fff6c`](https://github.com/Danyuyuyuyu/mux-deck/commit/90fff6c47d8a3fcef6c509fa81ec91f80425b865) 已在本地完整验证后执行 `git pull --rebase origin v2/avalonia-rewrite` 并推送。[Desktop CI](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38393673757) 已完成，workflow 与 desktop job 的 conclusion 均为 success，head_sha 与实现提交一致；locked restore、build、单元与架构测试、Smoke 及全部 12 个带参数受控回归步骤均通过，失败产物上传按预期跳过。随后仅补充实现提交与 CI 链接的文档提交，按工作流 paths 排除规则不重复触发构建。

#### 2b 修正：合并输入位置枚举

输入为 `2789ee3`，输出为 Contracts `InputItem.Kind` 直接采用 Domain `InputLocationKind`，删除重复的 `InputItemKind`。三个原有取值保留同样语义，新增契约测试覆盖 Domain 四个取值、类型归属、相等/不等和旧枚举已移除；旧运行调用方未接入该契约，行为不变。失败行为：构建、架构或既有回归失败即不推送；本修正 CI 通过后才开始 2c。按已接受 ADR-0001 实施，不修改 ADR。

实际验证（`desktop/`）：locked restore 10/10，严格 Release 构建 0 警告 / 0 错误，`dotnet test` 348/348（Domain 218、Application 9、Core 26、Architecture 95），失败/跳过 0。343 → 348，仅新增契约测试 5；原 343 保留。受控 13/13、656/656，命令及计数如下，本机日志 `artifacts/arch-corrections/2b-enum/`：

| 实际命令 | 结果 |
|---|---|
| `dotnet restore MuxDeck.sln --locked-mode` | 10/10 |
| `dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false` | 0 警告 / 0 错误 |
| `dotnet test MuxDeck.sln --no-build -c Release` | 348/348 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release` | 28/28 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-policy` | 57/57 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-round1` | 89/89 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-ui-round1` | 176/176 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --workbench-ui` | 38/38 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --input-feedback` | 24/24 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --media-drop` | 25/25 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --environment-ui` | 23/23 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --components` | 31/31 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-ui` | 37/37 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-all` | 41/41 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall` | 26/26 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall-ui` | 61/61 |

枚举修正提交 [10255a2](https://github.com/Danyuyuyuyu/mux-deck/commit/10255a236c6826825da9bb4be9abba55fb31e1ef) 的 [Desktop CI](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38397219246) 已完成 success，核对 head_sha 一致，CI 通过后才开始 2c；未重放真实工具链或原生 UI。共享文件仅 skeleton.md，sln、锁文件、props、ADR 不变。

### 2c 实施与验证（2026-10-13）

输入：枚举合并提交 `10255a2` 的 [Desktop CI](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38397219246) 已完成 success。输出：Domain `DiagnosticPolicy`、`OutputQcRules`、`AssDocument`，原 Core 位置只负责事实转换、IO 与调用。失败行为：诊断白名单、QC 拒绝与原错误文案保持不变；Domain 规则失败在旧边界映射为原 InvalidDataException，不新增架构例外。验收覆盖精确颜色告警/未知诊断、源/新增轨道元数据、时长、ASS 样式/覆盖/码点/动画拒绝、仅 CRLF/LF 归一与全文比较（Dialogue 文本/时序/Style/字体/顺序/末尾空格反例）。真实 ttx 与文件/进程操作不进 Domain，字体元数据读取及兼容证明保留原实现，随第 3 步处理；不改 `diagnostics.md`、工具参数或 ADR。

规则与既有回归对应：

| 新增测试 | 计数 | 对应场景与反例 |
|---|---|---|
| `DiagnosticPolicyTests` | 23 | `--warning-policy` 唯一颜色白名单五种标签、未知 warning、危险后缀/多逻辑行、原始错误、未知/无法解析级别、ANSI、普通 INF/DBG 与文本 |
| `AssDocumentTests` | 12 | Smoke / `--warning-policy` 必要节、字体/粗斜体覆盖与 reset、Unicode/ASS 转义、绘图忽略、动画拒绝、非标准字重、未知样式/格式字段、mapping 归一与歧义拒绝 |
| `OutputQcRulesTests` | 41 | Smoke / `--warning-policy` 已知源字段严格匹配与缺失字段保留未知、新增字幕/音频逐字段拒绝、真实 Track ID、时长容差与未知时长、失败前检查保留；同工具字幕全文只归一 CRLF/LF，Dialogue 文本/时序/Style/字体/顺序/末尾空格变化均拒绝 |
| `DiagnosticAndQcBoundaryTests` | 3 | 非空扫描三个真实 Domain 规则，禁止 IO/进程/网络/Environment/Console/Avalonia、Core/Contracts 与时间/ID 依赖 |

最终实现返回不可变 QC 结果，避免 C# yield 状态机自动调用 Environment.CurrentManagedThreadId；未修改、跳过或增加例外来绕过护栏。ASS 解析结果为不可变使用/映射集合，旧字体校验边界转换为原字典；ttx 读取、候选字体/字形验证、原生/合成粗体证明、参考生成和文件提取保持原实现。已知规则失败的错误文案与此前通过的检查项均保留。

以下命令均在 `desktop/` 实际执行：

| 实际命令 | 结果 |
|---|---|
| `dotnet restore MuxDeck.sln --locked-mode` | 10/10 项目成功 |
| `dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false` | 10/10 项目，0 警告 / 0 错误 |
| `dotnet test MuxDeck.sln --no-build -c Release` | 427/427（Domain 294、Application 9、Core 26、Architecture 98），失败/跳过 0 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release` | 28/28 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-policy` | 57/57 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-round1` | 89/89 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-ui-round1` | 176/176 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --workbench-ui` | 38/38 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --input-feedback` | 24/24 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --media-drop` | 25/25 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --environment-ui` | 23/23 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --components` | 31/31 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-ui` | 37/37 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-all` | 41/41 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall` | 26/26 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall-ui` | 61/61 |

348 → 427，仅新增 79 项：Domain 76、架构 3；原 348 项全部保留，既有测试零修改。13/13 受控套件、656/656，与基准一致；告警策略明确重跑 57/57，白名单未变。日志：`artifacts/arch-corrections/2c/`，ASS/诊断/元数据拒绝通过公共旧服务回归核验。

共享文件仅 `docs/agents/skeleton.md` 同步迁移现状、契约枚举与测试计数；本任务记录枚举修正及 2c 实测结果。未修改 sln、锁文件、props、ADR、步骤计划或 `diagnostics.md`。按已接受 ADR-0001 实施，工具参数与 QC 规则语义未改。未重放真实媒体工具链、原生选择器、Explorer、高 DPI 或播放器，真实工具链基准仍需在 3b 前重跑。实现提交 [`90fea6b`](https://github.com/Danyuyuyuyu/mux-deck/commit/90fea6b02e421105cd3c7bdb45c69c4c69d1df8e) 已在本地完整验证后执行 `git pull --rebase origin v2/avalonia-rewrite` 并推送。[Desktop CI](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38400687784) 已完成，workflow 与 desktop job 的 conclusion 均为 success，head_sha 与实现提交一致；locked restore、build、单元/架构测试、Smoke 及全部 12 个带参数受控回归均通过，包括 warning-policy；失败产物上传按预期跳过。随后只补充提交与 CI 链接的文档提交，按 paths 排除规则不重复触发构建。

### 3a 实施与验证（2026-10-13）

输入：ab01d98 的 Desktop CI 已通过，原有轨道与 QC 事实重复。输出：Application 九个业务能力端口及不可变输入输出、Domain 统一 MediaTrackInfo（TrackKind、LanguageTag、TrackFlags、TrackId），Contracts 探测结果与 QC 共用该类型，删除 TrackQcFacts。旧 Core 边界只转换轨道事实并调用 Domain，原工具调用与流水线不迁移。失败行为：原 QC 拒绝、检查文案、未知字段与语言比较保持一致，端口取消令牌与工作区异步释放约束明示；构建或既有回归失败不推送，CI 通过后完成本步。验收：端口全部归 Application、无外层依赖，转换覆盖原始字符串、未知类型、null/空 IETF、元数据存在性与集合快照，全部原有测试保留。按已接受 ADR-0001 实施，不修改 ADR；3b 前仍需重跑真实工具链基准。

实际命令均在 `desktop/` 执行，日志为 `artifacts/arch-corrections/3a/`：

| 实际命令 | 结果 |
|---|---|
| `dotnet restore MuxDeck.sln --locked-mode` | 10/10 |
| `dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false` | 0 警告 / 0 错误 |
| `dotnet test MuxDeck.sln --no-build -c Release` | 485/485（Domain 298、Application 10、Core 37、Architecture 140）；失败/跳过 0 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release` | 28/28 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-policy` | 57/57 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-round1` | 89/89 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-ui-round1` | 176/176 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --workbench-ui` | 38/38 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --input-feedback` | 24/24 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --media-drop` | 25/25 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --environment-ui` | 23/23 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --components` | 31/31 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-ui` | 37/37 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-all` | 41/41 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall` | 26/26 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall-ui` | 61/61 |

456 → 485，仅新增 29 项：Domain 4（值相等、IETF 缺失/空区别、传统语言严格比较、未知类型）、Application 1（探测与 QC 共用事实）、Core 11（原标签/未知类型、字段存在性、null/空 IETF、集合快照）、Architecture 13（九个端口归属、异步资源生命周期、不可变数据与契约边界、唯一事实类型、可取消操作与注入时钟）。原 456 项全部保留，原 OutputQcRulesTests 的 41 项仅同步值类型与旗标表达，测试方法及 Fact/Theory/InlineData 声明逐项对比一致，拒绝断言未放宽。受控 13/13、656/656 等于基准，包括 warning-policy 57/57。

共享文件仅 `docs/agents/skeleton.md`，同步端口、契约归属与迁移现状；任务文档更新 1a 修正 CI 与 3a 实测结果。sln、props、全部锁文件、ADR、步骤计划及 diagnostics.md 不变。原 Core 仅在 QC 事实边界换用统一转换，工具调用、参数与旧流水线保持不变。未重放真实工具链或原生选择器、Explorer、高 DPI 与播放器；本步未实现端口适配或假端口流水线，真实工具链基准仍须在 3b 前重跑。实现提交 [da0f334](https://github.com/Danyuyuyuyu/mux-deck/commit/da0f3342f2b50bccfdcf31f19212ee3bb63e9f91) 已在本地完整验证后执行 `git pull --rebase origin v2/avalonia-rewrite` 并推送。[Desktop CI](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38406784353) 完成 success，workflow 与 desktop job 均成功，head_sha 已核对一致；locked restore、build、单元/架构测试、Smoke 及全部 12 个带参数受控回归（含 warning-policy）通过，失败产物上传按预期跳过。随后仅补充结果表、提交与 CI 链接的文档提交，按工作流 paths 排除规则不重复触发构建。

#### 3a 修正：端口失败约定与中间文件引用

输入：9a45de2 的 CI 已通过。输出：Ports/Failures 的统一 ToolFailureException(Code, Message, Detail)、ErrorCodes 的退出/解析/Fatal 三类集中错误码及 Ports.WorkFile。失败行为：工具退出码异常、输出解析失败、Fatal 诊断抛 ToolFailureException，取消抛 OperationCanceledException；构造异常拒绝不属于 ErrorCodes 的码，保留原始 Detail。所有端口及调用成员 XML 注释明确该约定；中间文件只用 WorkFile，只有最终发布返回 OutputArtifact。既有 record 数据护栏保持不变，失败异常按 Exception 专门验证，另新增覆盖整个 Ports 子命名空间的形状检查，不修改或豁免 Contracts 不可变规则。验收：错误码/原文、所有端口注释、WorkFile 归属与最终产物边界、全部原有测试与受控回归。ADR、步骤计划、诊断白名单、旧 Core 和真实调用行为不变。实际验证：520 → 549，仅新增 29 项（Application 12：集中码/原文/Detail、未知码拒绝、工作文件值语义；Architecture 17：异常归属/只读载荷、Ports 子命名空间形状、中间/最终文件边界、全部十个端口与工作区接口 XML、泛型签名反例）。原 520 项全部保留，既有 Ports / Contracts / QC 测试文件零修改；日志 `artifacts/arch-corrections/3a-ports/`。受控 13/13、656/656，warning-policy 57/57；实现提交 [9171c30](https://github.com/Danyuyuyuyu/mux-deck/commit/9171c307571bf188b2fd78f78b533898ff712b1b) 本地全部验证后 pull --rebase 并推送；[Desktop CI](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38412696046) 完成 success，workflow 与 desktop job 全部通过，head_sha 已核对一致。确认通过后开始真实基准重跑。

| 实际命令（desktop/） | 结果 |
|---|---|
| `dotnet restore MuxDeck.sln --locked-mode` | 10/10 |
| `dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false` | 0 警告 / 0 错误 |
| `dotnet test MuxDeck.sln --no-build -c Release` | 549/549（Domain 298、Application 22、Core 37、Architecture 192）；失败/跳过 0 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release` | 28/28 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-policy` | 57/57 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-round1` | 89/89 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-ui-round1` | 176/176 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --workbench-ui` | 38/38 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --input-feedback` | 24/24 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --media-drop` | 25/25 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --environment-ui` | 23/23 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --components` | 31/31 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-ui` | 37/37 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-all` | 41/41 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall` | 26/26 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall-ui` | 61/61 |

共享文件仅 skeleton.md，同步端口失败、取消和工作文件约定；sln、props、锁文件、ADR、步骤计划和 diagnostics.md 不变。错误码仅向 ErrorCodes 添加三项，原常量不变；旧 Core 及真实调用方零修改，行为未接入新端口。下一项真实基准必须待本项 CI 通过才运行，任一真实结果与原计数不符立即停止，不开始 3b。



### 真实工具链基准重跑（2026-10-13，3b 前）

输入为端口修正 `9171c30` 的生产代码；[Desktop CI](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38412696046) 完成 success，head_sha 已核对一致。首次三项在该提交执行，续跑基线为纯文档提交 `f956e36`；已核对 `9171c30..f956e36` 的 src、tests、sln、props 没有变化，沿用同一 Release 编译产物。用户明确授权按历史入口完成电影，再顺序联网安装与固定包缓存卸载。输出为六项“仅本地”的命令、计数、日期、工具版本及本机报告，均写入上方对照表。

六项全部通过：真实集成 **121/121**、多轨导入 **37/37**、真实 UI **39/39**、完整电影 **9/9**、联网安装 **15/15**、固定包卸载 **17/17**，共 **6/6 套件、238/238 检查**，退出码与失败数均为 0。电影原生报告另确认 `SyntheticBoldExpected=true`、`Status=Succeeded`，原始/副本 ASS、字体和视频哈希检查通过。任何一项不符即停止的规则不变；未删除、跳过、放宽或修改测试，不重定义任何基准。

#### 电影入口文档修正与补充记录

原对照表将 9 项基准配成 `--warning-real <task.log>`，是**文档错误、不是回归**。历史 [9 项报告](../../artifacts/warning-policy/real-e223f0011b774942bf40123b7b1c76a3/report.json) 的 `SyntheticBoldExpected=true`，对应 `--warning-synthetic-bold-real <task.log>`。该入口在普通重放基础上额外检查“真实 Regular-only 字体 fixture 验证合成粗体提示”。本次按用户指定的同一请求日志执行历史入口，[原生报告](../../artifacts/warning-policy/real-99bfc72a8373477dacf51a8e00524b70/report.json) **9/9、失败 0、SyntheticBoldExpected=true**，命令已修正到对照表，9 项基准不变。

首次按原表运行的 `--warning-real artifacts/runs/20261009-115919-18aeaf51f58d43e5a4b935b9ce8b8f5b/task.log` 为 [8/8、失败 0](../../artifacts/warning-policy/real-2a100db1e6e6472cacd2ec5f22286dd7/report.json)，`SyntheticBoldExpected=false`，保留为补充记录，不替代合成粗体 9 项基准。首次因此暂停、只读对比并报告；用户明确确认历史入口后才续跑。电影控制台不打印总数，首次临时计数脚本无法解析 stdout；两份原生 JSON 的 8 与 9 项检查列表已分别核对，记录脚本现读取原生报告，不修改 harness。

#### 安装、卸载与版本证据

`--component-install-all-real` 真实联网下载并核验固定包、启动工具、重检测、验证移动目录和只装依赖路径，使用新安装的三组件及独立字体后端完成真实 ASS 子集化、封装和 QC，原生报告 **15/15、失败 0**。报告副本：[安装报告](../../artifacts/arch-corrections/real-baseline/component-install-all-real-report.json)；临时原目录为 `C:/Users/ZhenXun/AppData/Local/Temp/mux-deck-install-d4de6aa627a543cf9ba676f43c9a4f4b/`，未写入用户真实工具目录。

卸载固定缓存为 `artifacts/arch-corrections/real-baseline/packages`，七个文件按当前 `ComponentReleases` 固定地址下载，大小和 SHA-256 **7/7** 核对一致；包括 mkv.zip、ffmpeg.zip、afs.zip、assfonts.zip、python.zip、fonttools.whl、pip.whl。生产安装器在卸载套件中再次核验，执行四组件真实安装/启动/卸载、系统回退、外部 AFS 主程序保留、独立依赖卸载重装及复用，原生报告 **17/17、失败 0**。证据：[包清单](../../artifacts/arch-corrections/real-baseline/package-manifest-verified.json)、[卸载报告](../../artifacts/arch-corrections/real-baseline/component-uninstall-real-report.json)；临时原目录为 `C:/Users/ZhenXun/AppData/Local/Temp/mux-deck-uninstall-bc4f366ec52b432e93c0a8924183e6a4/`。操作只限本次验收独占目录，系统工具与外部 AFS 文件哈希保留检查通过。

电影与前三项使用系统 MKVToolNix 100.0、PATH FFmpeg 8.1.1-full_build、仓库 AFS 2.1.1+abeaf2a431ab95572379e8501a06b09389f5173d、ttx/fontTools 4.61.1、Python 3.12.10；[本机工具版本](../../artifacts/arch-corrections/real-baseline/tool-versions.json) 与集成报告实际 ToolPaths 一致。安装与卸载使用受管 MKVToolNix 101.0、FFmpeg 8.1.1-essentials_build、AFS 2.1.1（固定发布包 v2.2.0 的内部版本）、fontTools 4.61.1、Python 3.12.10；卸载还使用 assfonts **0.7.3**，系统回退仍为 MKVToolNix 100.0。证据：[安装后版本查询](../../artifacts/arch-corrections/real-baseline/installed-tool-versions.json)、[固定包 assfonts 实际版本](../../artifacts/arch-corrections/real-baseline/assfonts-version.json)。两个工具来源的版本分别记录，不以发布包名替代实际 AFS 返回值。

六项命令与日期汇总、stdout、原生报告路径保存在 `artifacts/arch-corrections/real-baseline/`；源输入只读，输出、下载和暂存隔离。本轮真实 UI 为已有 headless 控件与真实 Core/工具流程，不替代原生选择器、Explorer、DPI 或播放器验收。本提交仅修订任务文档及其对照表，不改 ADR、步骤计划、骨架、生产代码、测试、sln、props、锁文件或诊断白名单；按已接受 ADR-0001 实施。按用户要求，六项全部通过后只做一笔纯文档提交、pull --rebase 并推送；工作流明确排除纯文档，不触发新的 Desktop CI。**3b 未开始，提交后停止等待用户确认。**

#### 3a 修正：端口失败约定补全

输入：dbe9f56，六项真实基准已完成。输出：失败/取消 XML 只保留在外部操作方法；类型与纯属性不重复说明。新增目标已存在与工作区 IO 失败集中码；ToolFailureException 增加保留原异常的四参构造，原三参调用保持兼容。失败行为：未知码拒绝、原文与 InnerException 保留，取消仍抛 OperationCanceledException。验收：原有全部测试保留，端口 XML 测试只按用户授权调整检查范围，其他归属/只读/工作文件/契约断言不放宽；严格构建、全部单元/架构及 13 套受控回归通过后才提交，CI 通过后才开始 3b。

实际验证（desktop/）：549 → 554，仅新增 Application 5（新集中码自动覆盖 2、原异常/堆栈/空异常兼容/未知码补充 3），原 549 项保留；Architecture 192 不变，原 XML 十组用例按用户授权限定到操作方法，类型/属性要求无异常说明；其他断言不放宽。受控 13/13、656/656。日志 `artifacts/arch-corrections/3a-failure-complete/`，实现提交 [7a7a585](https://github.com/Danyuyuyuyu/mux-deck/commit/7a7a585b2a7cbb2b4a8884ee5491b58e1a03b0a3) 已在本地完整验证后 pull --rebase 并推送；[Desktop CI](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38422029153) 完成 success，head_sha 核对一致，CI 通过后才开始 3b。

| 实际命令 | 结果 |
|---|---|
| `dotnet restore MuxDeck.sln --locked-mode` | 10/10 |
| `dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false` | 0 警告 / 0 错误 |
| `dotnet test MuxDeck.sln --no-build -c Release` | 554/554（Domain 298、Application 27、Core 37、Architecture 192）；失败/跳过 0 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release` | 28/28 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-policy` | 57/57 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-round1` | 89/89 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-ui-round1` | 176/176 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --workbench-ui` | 38/38 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --input-feedback` | 24/24 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --media-drop` | 25/25 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --environment-ui` | 23/23 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --components` | 31/31 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-ui` | 37/37 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-all` | 41/41 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall` | 26/26 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall-ui` | 61/61 |

实际命令与计数见上表，实现提交与通过的 Desktop CI 见本节开头记录；按已接受 ADR-0001 实施，不修改 ADR。

### 3b 实施与验证

输入：7a7a585 的 Desktop CI 已通过，六项真实工具链基准均通过；用户批准的三项调整与 Desktop 直接依赖清单已先写入 3b 计划。输出：Infrastructure 工具/进程适配、独占工作区与不覆盖落位、单向过渡 Core → Infrastructure、新 Infrastructure 集成测试与锁文件。失败行为：适配边界统一 ToolFailureException，取消仍抛 OperationCanceledException；旧 Core 边界保留原文案、诊断和 QC 行为。Smoke 只改 using/命名空间引用。验收：每个适配的参数、输出、退出码、取消、文件生命周期反例；原进程树/全部受控回归保留；本地真实 integration 121/121、media-drop-real 37/37 必须与基准一致；失败不推送，CI 通过后才完成 3b。本步不实施 3c 流水线或 4a 装配，不修改 ADR、诊断白名单或其他步骤计划。

实际验证（desktop/）：2026-10-13 23:58 至 2026-10-14 00:01（Asia/Shanghai；artifacts run-id 使用 UTC 日期）。554 → 601，仅新增 Infrastructure 45、架构 2（新增测试项目的直接引用与全部端口的 Infrastructure 实现），原 554 项全部保留，失败/跳过 0。受控 13/13、656/656；Smoke 的 .cs diff 只有命名空间 using，未改逻辑、断言或计数。ProcessRunner/WindowsProcessJob 原文件除命名空间外保持，原进程树回归通过。新项目只直接引用 Infrastructure；NuGet restore 成功，新项目锁文件及过渡引用锁图均已提交。日志 `artifacts/arch-corrections/3b/`，真实重放与初始基准严格一致。

| 实际命令 | 结果 |
|---|---|
| `dotnet restore MuxDeck.sln --locked-mode` | 11/11 |
| `dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false` | 0 警告 / 0 错误 |
| `dotnet test MuxDeck.sln --no-build -c Release` | 601/601（Domain 298、Application 27、Core 37、Architecture 194、Infrastructure 45）；失败/跳过 0 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release` | 28/28 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-policy` | 57/57 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-round1` | 89/89 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --single-mux-ui-round1` | 176/176 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --workbench-ui` | 38/38 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --input-feedback` | 24/24 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --media-drop` | 25/25 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --environment-ui` | 23/23 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --components` | 31/31 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-ui` | 37/37 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-all` | 41/41 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall` | 26/26 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall-ui` | 61/61 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --integration` | 121/121，失败 0 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --media-drop-real` | 37/37，失败 0 |

真实工具版本：MKVToolNix 100.0 / FFmpeg 8.1.1 / AFS 2.1.1 / fontTools 4.61.1 / Python 3.12.10，与重构前基准相同。[integration 报告](../../artifacts/phase1/20261013-155956-4ebf5982d07a422cbf9eab2f789006ac/report.json)与[media-drop-real 报告](../../artifacts/automatic-drop/real-bd1ab50607484ac6b38d508b9401c2ad/report.json)保留在本机，分别 121/121、37/37，失败均为 0。按已接受的 ADR-0001 和本节明确授权的三项调整实施，不修改 ADR。实现提交 [a2b76d9](https://github.com/Danyuyuyuyu/mux-deck/commit/a2b76d9588646265bfab68d0521928c8f881f94c) 已在本地全部验证后 pull --rebase 并推送；[Desktop CI](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38427342392) 完成 success，head_sha 与实现提交一致，全部必需检查通过。2026-10-14（Asia/Shanghai）补录本节与结果表，3b 已完成；本步到此结束，后续 3c/4a 尚未实施。

## 发现的旧缺陷

（已修复：修正 A；提交 c636e91，Desktop CI 38383347658 已通过。以下保留发现背景。）`Desktop/App.axaml.cs` 将未限定的 `Application` 用作 Avalonia 基类；当目标程序集首次包含 `MuxDeck.Application` 命名空间的业务类型时，编译器会优先解析同名命名空间并报 CS0118。首个反例因此未计为架构护栏验收，改用独立禁止命名空间后两条护栏均确认失败。1b 只定义 `MuxDeck.Contracts`，没有触发该冲突；该缺陷已由修正 A 的 c636e91 修复，并经临时同名类型与完整 CI 验证。
