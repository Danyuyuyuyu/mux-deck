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

- **用户已批准的 3c/3d 调整**（本次先改计划再实施）：
  1. 新用例不持有可变配置，工具与目录配置通过 MuxJobSpec.Tools 随任务快照传入。SingleMuxService.UpdateWorkspace 作为旧入口外壳保留到 4a，仅影响下一次任务；“删除 UpdateWorkspace”的完成标准移到 4a。
  2. 3c、3d 分别形成本地提交；完整真实对照完成后只推送临时审查分支 review/3c-3d。用户确认后才 pull --rebase、推送到 v2/avalonia-rewrite、核对 CI，最后删除临时分支（用户已批准）。
  3. 真实基准起点使用 3b 前重跑记录 dbe9f56，取代 3f42228；完整电影入口为 --warning-synthetic-bold-real <task.log>。逐项对照另在 e005923 独立工作树与新代码上以同机、同输入、同工具运行，方法见下方 3d。

- `SingleMuxService.RunAsync`（迁移前约 260 行）改写为 `RunSingleMuxUseCase`：检查输入 → 探测源 → 子集化 → 生成封装计划 → 封装临时成品 → 成品校验 → 不覆盖落位，阶段间用 record 传递。
- 工具与工作目录配置通过 MuxJobSpec.Tools 随任务快照传入；新用例不持有可变配置，旧 UpdateWorkspace 外壳按用户批准保留到 4a。
- QC 输出结构化 `CheckResult`；失败输出 `JobFailure`（含阶段与错误码）。
- Application 测试：假端口覆盖各阶段失败、取消、清理、目标已存在与落位期间新出现目标。

3c/3d 验收采用下方经用户批准的工具不确定性规则；生产规则、测试和架构护栏不变。

### 3d 旧入口（小）

`SingleMuxService` 只保留旧模型转换并调用新用例；Desktop 与 Smoke 不改。

3c/3d 本地验证与真实逐项对照（**用户已批准**）：

- 先跑 locked restore、Release 0 警告 / 0 错误、dotnet test 全部通过及 13 个受控套件；3c、3d 各自本地提交，先不推主分支；最终对照完成后只推 review/3c-3d，等待用户确认。
- 3d 完成后跑六项仅本地真实套件：--integration（121）、--media-drop-real（37）、--ui-integration（39）、--warning-synthetic-bold-real artifacts/runs/20261009-115919-18aeaf51f58d43e5a4b935b9ce8b8f5b/task.log（9，SyntheticBoldExpected=true）、--component-install-all-real（15，需联网）、--component-uninstall-real <固定发布包缓存目录>（17）。计数对照 dbe9f56 的重跑基准，不再用 3f42228；--warning-real 的 8 项仅保留历史补充记录。
- 在 e005923 独立工作树与新代码上，以同一台机器、相同输入与工具，各重跑 integration、media-drop-real、ui-integration 和完整电影。逐成品提取并比较轨道类型/编码/语言/名称/default/forced、附件身份与 SHA-256、ASS 全文、音频 fullraw 哈希、检查项与告警。
- **用户已批准的工具不确定性对照规则**（先用 e005923 的 integration-single-sc 连跑两遍证实来源）：ASS 使用每个文件自己的 `; Font Subset:` 映射，把随机名还原为原字体名后全文逐字比较，其他文字/空格/换行/顺序保留。字体附件按原字体（含 face/文件名与顺序）配对，所有表逐表比较哈希；只还原 name 表中的随机名称，并排除 head.modified / head.checkSumAdjustment，其他 head 字节保留。附件数量、顺序、MIME 与去随机部分的文件名逐项比较。轨道信息、音频 fullraw、检查项、告警和进度保持严格比较，不做处理。脚本必须用真实字体字形缺失、Dialogue 正文改动、字体表改动以及各规则的反例证明拒绝能力；授权范围之外的差异继续停止报告。

- 不比较整个 MKV 文件哈希（日期与 UID 每次变化）。同一输入的逐项报告记录“一致 / 差异”，路径写入实施结果；对照脚本提交至 desktop/scripts/，不进解决方案或 CI。
- 开发中自己代码引起的构建或测试失败直接修复；只有最终基准逐项对照出现差异、修复必须改测试/基准/计划、需要放宽架构护栏才停止报告（用户已批准）。完成后将两笔提交推至 review/3c-3d 审查，主分支仍等待用户确认；确认后 pull --rebase、推送、确认 Desktop CI 并删除临时分支。

### 3e 操作协调与组件（中）

- 实现 `IOperationCoordinator`（内存租约），替换 `ComponentOperations.Gate`。
- 组件检测/安装/卸载经 `IManageComponents`；`ComponentVerifier`、`ComponentInstaller`、`ComponentUninstaller`、`ComponentDirectories`、`ComponentReleases` 移入 Infrastructure；`AssfontsInstaller` 兼容入口随第 5 步评估移除。
- 验证：组件受控、安装 UI、卸载、卸载 UI、环境 UI 回归；固定缓存真实卸载循环 `--component-uninstall-real`。

## 第 4 步：拆分 Desktop

### 4a 集中装配（中）

- **用户已批准**：此步删除 SingleMuxService.UpdateWorkspace 外壳；3c/3d 新用例已只消费 MuxJobSpec.Tools 快照，4a 替换旧装配后不再需要可变入口。
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
| **3c** | 七个阶段各有 Application 假端口测试，覆盖成功、本阶段失败（`JobFailure.Stage` 与 `Code` 正确）、取消、临时目录无残留、目标已存在、落位期间新出现目标；新用例无可变配置，并有“运行中修改配置不影响已提交任务”的测试；旧 UpdateWorkspace 按用户批准留到 4a；3c 只本地提交，3d 后执行全部仅本地真实套件与[逐项对照](#3d-旧入口小)，报告写入结果表；**两笔一起推送前经用户确认** |
| **3d** | SingleMuxService 只含旧模型转换与用例调用（用户批准 UpdateWorkspace 外壳留到 4a）；Desktop 与 Smoke 零改动；全部受控与六项真实套件等于 dbe9f56 基准；e005923 与新代码四项逐产物对照一致；只本地提交，报告与两个哈希交用户确认后一起推送 |
| **3e** | `ComponentOperations.Gate` 删除，所有互斥经 `IOperationCoordinator`；协调器测试覆盖同类/异类互斥矩阵、异常与取消后的租约释放、`ShutdownAsync` 等待全部租约；组件与环境相关套件等于基准；本地 `--component-uninstall-real` 17/17 |
| **4a** | 用户已批准从 3c 移入的 UpdateWorkspace 外壳删除；App.Workspace、App.Storage 删除；新增架构测试：ViewModels/Views 不引用 Infrastructure，装配只在 `Program`/`Composition/`；`--root` 便携模式与默认存储路径行为不变（有测试或记录手动验证）；全部 UI 套件等于基准 |
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
| [3b · `a2b76d9`](https://github.com/Danyuyuyuyu/mux-deck/commit/a2b76d9588646265bfab68d0521928c8f881f94c) + [修正 · `d99781b`](https://github.com/Danyuyuyuyu/mux-deck/commit/d99781b5cb44ac4c2279d90225dd6412ba7d92b7) | 已完成，已推送，[修正 Desktop CI 通过](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38494664905) | locked restore 11/11；Release 0 警告 / 0 错误；test 613/613；受控 13/13、656/656；真实 integration 121/121、media-drop-real 37/37 | 依用户批准保留 Desktop 直接依赖；Core → Infrastructure 过渡；初始新增 47，本次新增 12，原 601 保留。 |
| [3c · 7c9a477](https://github.com/Danyuyuyuyu/mux-deck/commit/7c9a477410768aa1758551116193fac19e8172de) + [修正 · a3dbea9](https://github.com/Danyuyuyuyu/mux-deck/commit/a3dbea92f867a739aae4490e249ace1a493300d3) | 用户已确认，保留三提交推送主分支；[Desktop CI 通过](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38530034209)，head_sha=a3dbea9 | 初验 675；结构化修正 test 717/717；restore 11/11、Release 0/0；受控 656/656；真实 238/238 | 原 676 保留，新增 41；[15/15、150/150 对照报告](../../artifacts/arch-corrections/3c-structured-final/summary-all15.json)；远端审查分支已删除。 |
| [3d · 0f1927b](https://github.com/Danyuyuyuyu/mux-deck/commit/0f1927b5b374eee9198ff9c22e3e047c27dad448) | 用户已确认并推送主分支；[Desktop CI 通过](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38530034209)，head_sha=a3dbea9 | 初验 676；结构化修正 test 717/717；restore 11/11、Release 0/0；受控 656/656；真实 238/238；脚本反例 22/22 | [15 项逐项对照报告](../../artifacts/arch-corrections/3c-structured-final/summary-all15.json)全部一致；旧/旧来源复核 10/10，原始报告保留。 |
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

#### 3b 修正：适配修正

输入：ddf6987 / a2b76d9 已完成 3b，用户要求独立提交本修正。输出：ToolCallContext 工具日志采用旧 TaskLog 的尽力写入行为；集中新增 MUX_TOOL_TIMEOUT / MUX_TOOL_NOT_FOUND，分别用于超时和工具缺失，Message 保持；Core ToolLocator.Find 复用 ToolPathLookup.Find，来源显示不变；ToSpec(Workspace) 复用 Workspace 隐式转换。失败行为：仅跳过日志写入失败，工具退出、输出解析、Fatal 与真实取消继续按端口约定传播。验收边界为用户指定的工具调用与定位端口、旧 Core 显示与转换入口；补公开端口回归，保留所有现有用例，错误码预期仅同步本次明确要求的精确值，其余断言不放宽。完整本地受控 13 套件和真实 integration 121/121、media-drop-real 37/37 与基准一致后，pull --rebase、推送并确认 CI。按已接受 ADR-0001 实施，不修改 ADR，不开始 3c。

实际验证（2026-10-14，Asia/Shanghai；命令在 desktop/ 执行）：601 → 613，原 601 用例保留，新增 12（Infrastructure 日志失败及 Fatal 保留 7、Core 来源显示 3、Application 新码构造白名单 2）。既有超时与工具缺失用例仅把泛化错误码同步为本次明确要求的精确值，新增完整 Message 相等断言，原 InnerException / message 内容断言保留；未删除、跳过或放宽任何现有用例。架构 194 不变，Smoke 与 ADR 无修改。受控 13/13、656/656，两项真实对照严格相等。TDD 证据：日志 7 项先失败后通过，超时 1 项和工具缺失 1 项分别先失败后通过；来源与旧转换在收敛前后均通过。日志位于 `artifacts/arch-corrections/3b-adapter-fix/`。

| 实际命令 | 结果 |
|---|---|
| `dotnet restore MuxDeck.sln --locked-mode` | 11/11 |
| `dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false` | 0 警告 / 0 错误 |
| `dotnet test MuxDeck.sln --no-build -c Release` | 613/613（Domain 298、Application 29、Core 40、Architecture 194、Infrastructure 52）；失败/跳过 0 |
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
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --integration` | 121/121 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --media-drop-real` | 37/37 |

真实 [integration 报告](../../artifacts/phase1/20261014-011113-5f2f6effc5eb4bf2a3b3ecf0a8aaf7a8/report.json)与 [media-drop-real 报告](../../artifacts/automatic-drop/real-4b4aeedca72f4f21bcfce2bd3c730341/report.json)分别 121/121、37/37，失败 0；工具版本继续为 MKVToolNix 100.0、FFmpeg 8.1.1、AFS 2.1.1、fontTools 4.61.1、Python 3.12.10。实现提交 [d99781b](https://github.com/Danyuyuyuyu/mux-deck/commit/d99781b5cb44ac4c2279d90225dd6412ba7d92b7) 使用指定标题 `refactor(desktop): [3b] 适配修正`，全部本地验证后 pull --rebase 并推送；[Desktop CI](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38494664905) 完成 success，head_sha 核对一致，全部必需检查通过。仅补录结果表与修正记录，不开始 3c。

## 发现的旧缺陷

（已修复：修正 A；提交 c636e91，Desktop CI 38383347658 已通过。以下保留发现背景。）`Desktop/App.axaml.cs` 将未限定的 `Application` 用作 Avalonia 基类；当目标程序集首次包含 `MuxDeck.Application` 命名空间的业务类型时，编译器会优先解析同名命名空间并报 CS0118。首个反例因此未计为架构护栏验收，改用独立禁止命名空间后两条护栏均确认失败。1b 只定义 `MuxDeck.Contracts`，没有触发该冲突；该缺陷已由修正 A 的 c636e91 修复，并经临时同名类型与完整 CI 验证。

### 3c 本地验证（2026-10-14）

用户批准调整：新用例仅持有端口，配置通过 MuxJobSpec.Tools 的任务快照传入；旧 UpdateWorkspace 外壳留至 4a。本步与 3d 分别本地提交，完整真实对照后等用户确认再一起推送。依据已接受 ADR-0001，未修改 ADR。

输入为不可变规格，输出为结构化检查、诊断和实际阶段失败；取消独立返回，成功/失败/取消/异常等待工作区异步释放。不覆盖发布是提交点。新增四个业务端口均有 Infrastructure 实现，不使用通用文件系统接口。

实际命令（desktop/）：
- `dotnet restore MuxDeck.sln --locked-mode`：11/11。
- `dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false`：0 警告 / 0 错误。
- `dotnet test MuxDeck.sln --no-build -c Release`：675/675，失败/跳过 0；Domain 298 / Application 80 / Core 40 / Architecture 198 / Infrastructure 59。原 613 保留，仅新增 62（集中码 24、用例假端口 27、端口文件事实 7、端口 XML 4）。
- `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release [-- <入口>]`：无参数 28、--warning-policy 57、--single-mux-round1 89、--single-mux-ui-round1 176、--workbench-ui 38、--input-feedback 24、--media-drop 25、--environment-ui 23、--components 31、--component-install-ui 37、--component-install-all 41、--component-uninstall 26、--component-uninstall-ui 61；13/13 套件、656/656，与 dbe9f56 基准一致。

证据：`artifacts/arch-corrections/3c/`。新补测试后再跑严格构建与全部单元测试。Desktop、Smoke、现有行为测试零改动；端口 XML 测试只新增参数。真实对照待 3d，不标已验收；未推送、未运行 CI。

### 3d 暂停记录（2026-10-14）

3d 开发中发现日志转换回归，已修复，见 ①

### 3d 续跑与逐成品差异（2026-10-14）

用户确认继续并批准修复上一条日志转换差异，限定生产修复范围为 LegacyTaskWorkspaceFactory（必要时才加 EquatableArray converter），不改测试/基准；全部通过后才本地提交 3d，推送前先汇总。本次只修复 Factory 的日志字段投影，直接生成旧 MuxRequest 日志，不反序列化 MuxJobSpec、不增加 converter。使用既有转换方法，未改变 Domain、Application、Desktop、Smoke、测试、ADR、解决方案或锁文件。请求日志额外作 16 组 UTF-8 字节核对，覆盖 null/空输出名、中文与转义、多字幕/音频，16/16 与旧序列化格式一致；该核对 console 位于忽略的 artifacts，不修改测试项目。

通用验证实际命令（desktop/）：
- `dotnet restore MuxDeck.sln --locked-mode`：11/11。
- `dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false`：0 警告 / 0 错误。
- 修复后先跑 `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-policy`：57/57，失败 0。
- `dotnet test MuxDeck.sln --no-build -c Release`：675/675（Domain 298 / Application 80 / Core 40 / Architecture 198 / Infrastructure 59），失败/跳过 0，与 3c 一致，原用例全部保留。
- `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release [-- <入口>]`：无参数 28、--warning-policy 57、--single-mux-round1 89、--single-mux-ui-round1 176、--workbench-ui 38、--input-feedback 24、--media-drop 25、--environment-ui 23、--components 31、--component-install-ui 37、--component-install-all 41、--component-uninstall 26、--component-uninstall-ui 61；13/13 套件，656/656。

六项真实验证依次运行，计数全部等于 dbe9f56 基准：

| 真实参数（均为 dotnet run --project tests/MuxDeck.Smoke --no-build -c Release --） | 实际计数 | 本机证据 |
|---|---|---|
| --integration | 121/121，失败 0 | [报告](../../artifacts/phase1/20261014-030154-374c0ec0158e40ba9cf2afcbd1887456/report.json) |
| --media-drop-real | 37/37，失败 0 | [报告](../../artifacts/automatic-drop/real-2c4609983ec64da29b2fe4117f9c16f1/report.json) |
| --ui-integration artifacts/phase1/20261014-030154-374c0ec0158e40ba9cf2afcbd1887456 artifacts/arch-corrections/3d-resume/ui.png | 39/39，失败 0 | [报告](../../artifacts/single-mux-round1-b/real-4189e38529574e4695f462b612dcc6e3/report.json) |
| --warning-synthetic-bold-real artifacts/runs/20261009-115919-18aeaf51f58d43e5a4b935b9ce8b8f5b/task.log | 9/9，失败 0，SyntheticBoldExpected=true | [报告](../../artifacts/warning-policy/real-6c4d9e5607a1486b820e10cfebd6a885/report.json) |
| --component-install-all-real | 15/15，失败 0，实际联网下载 | [报告副本](../../artifacts/arch-corrections/3d-resume/component-install-all-real-report.json) |
| --component-uninstall-real artifacts/arch-corrections/real-baseline/packages | 17/17，失败 0 | [报告副本](../../artifacts/arch-corrections/3d-resume/component-uninstall-real-report.json) |

日期为 2026-10-14（Asia/Shanghai）。既有工具链路径与版本保持：MKVToolNix 100.0 / FFmpeg 8.1.1-full / AFS 2.1.1 / fontTools 4.61.1 / Python 3.12.10；新安装包的输出为 MKV 101.0 / FFmpeg 8.1.1-essentials / AFS 2.1.1，固定卸载包包含 assfonts 0.7.3。逐任务实际工具路径和二进制 SHA-256 另保存在对照 host 结果。

e005923 独立工作树 locked restore 11/11、严格 Release 0 警告 / 0 错误。共享工具目录只读复用；该树实际运行 --integration、--media-drop-real、--ui-integration（通过 junction 复用上述新 integration 输入）、--warning-synthetic-bold-real（同一绝对历史 task.log），分别 121/121、37/37、39/39、9/9，失败 0，电影 SyntheticBoldExpected=true。本机工作树与 native 路径见[四项摘要副本](../../artifacts/arch-corrections/3d-resume/e005923-integration-summary.json)及同目录的 e005923-media-drop-real/ui-integration/movie-summary.json。

逐成品对照采用两个独立 console host，分别引用 e005923 与当前 Core；从四套件成功成品日志整理 [15 个原请求](../../artifacts/arch-corrections/3d-resume/comparison-cases.json)，将同一输入文件、同一工具根目录交给两个公开旧入口重新运行，只改变各自输出目录。每个输入及工具二进制计算 SHA-256，以核对确为同输入/同工具，输入运行前后哈希保持。host 不进解决方案或 CI，不修改 Smoke 或测试；脚本位于 `scripts/Run-ArchComparison.ps1` 与 `scripts/Compare-ArchArtifacts.ps1`，后者提取附件、ASS 全文与音频 fullraw，保留实际名称与字节差异，不计算整 MKV 哈希。

实际对照命令（desktop/）：
```powershell
./scripts/Run-ArchComparison.ps1 -BaselineRoot 'C:\Users\ZhenXun\.codex\worktrees\arch-3c3d-baseline\mux-deck' -CurrentRoot 'E:\Video\mux-deck' -CaseManifest 'artifacts/arch-corrections/3d-resume/comparison-cases.json' -EvidenceDirectory 'artifacts/arch-corrections/3d-resume/paired' -Mkvmerge 'C:\Program Files\MKVToolNix\mkvmerge.exe' -Mkvextract 'C:\Program Files\MKVToolNix\mkvextract.exe'
```

首项 `integration / single-sc` **出现差异，立即停止**：轨道类型/编码/语言/名称/default/forced、音频 fullraw、检查项、告警、进度、输入与工具哈希七组一致；附件身份/SHA-256、ASS 全文两组不一致。示例：旧 `001-arial.0.H7X2U6I4.ttf`、新 `001-arial.0.GDYTFKZV.ttf`，附件 SHA-256 分别为 `FFA879DD4C940E6715E2B5B3BC6EBCDDC05EE5A984A562C3DB9118007C173B38` / `C420C02AEAA493249E68B92DFED124F83631AF06AC73844A60902F358B782933`。Microsoft YaHei token 为 MFG8BO2C / O3OGH0U2；ASS 的 Font Subset 映射、Style 及 Dialogue 的 fn 引用亦不同。现象指向 AFS 每次生成的字体名称，尚未用基准对基准复核，不能据此宣称只是随机性或改为一致。没有对名称、字体字节或 ASS 全文做归一化。

| 套件 | 旧/新套件计数 | 逐成品结论 |
|---|---|---|
| integration | 121/121 / 121/121 | 差异：首个 single-sc 的附件及 ASS；本套其余 4 个成品未继续 |
| media-drop-real | 37/37 / 37/37 | 未完成：按首项差异停止，6 个共享输入成品未运行 |
| ui-integration | 39/39 / 39/39 | 未完成：3 个共享输入成品未运行 |
| 完整电影 | 9/9 / 9/9，标志均 true | 未完成：1 个共享输入成品未运行 |

证据：[逐项差异报告](../../artifacts/arch-corrections/3d-resume/paired/integration-single-sc/comparison.json)、[停止摘要](../../artifacts/arch-corrections/3d-resume/paired/summary.json)、同目录 baseline/current-result.json 与提取文件。1/15 已检查、14 项未验收。按用户停止规则未继续跑剩余对照、未修复其他代码、未改测试或基准。3d 保持未提交/未推送；仅有 3c 本地提交 `7c9a477410768aa1758551116193fac19e8172de`，未 pull --rebase、未运行 CI，等待用户确认下一步。

### 3d ① 请求日志改用旧快照（2026-10-14）

用户批准继续 3d，并限定最终交付只推临时分支 review/3c-3d；主分支等待确认。开发中的自身构建/测试失败直接修复，最终逐项差异、必须改测试/基准/计划、须放宽架构规则时才停下。

日志工作区不解析 SPEC 或 MuxJobSpec，只提供原始日志边界；SingleMuxService 在首次回调前固定旧请求集合，在输入校验完成事实 INPUT_VALIDATED 后、目标存在性检查前，用它持有的旧 MuxRequest 路径投影直接序列化“请求 …”。默认序列化格式、字段顺序及写入时机沿用旧版；用例自己的 SPEC 日志保留原样，未给 EquatableArray 添加 JSON 反序列化支持。

新增公开旧入口测试 LegacyRequestLogTests：先因 SPEC 消失失败，再修复后 1/1 通过；请求行（排除动态时间戳）与旧默认序列化字符串逐字一致，验证中文/引号转义、路径规范化、写入顺序、目标冲突和调用方集合修改隔离。原 675 全部保留，只新增 Core 1；Desktop、Smoke、ADR 与既有测试均未修改。旧流水线辅助删除，唯一七阶段流程在 Application，检查文本与旧进度由转换层生成。

通用命令在 desktop/：
- `dotnet restore MuxDeck.sln --locked-mode`：11/11。
- `dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false`：0 警告 / 0 错误。
- `dotnet test MuxDeck.sln --no-build -c Release`：676/676，失败/跳过 0，Domain 298 / Application 80 / Core 41 / Architecture 198 / Infrastructure 59。
- `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release [-- <入口>]`：无参数 28、warning-policy 57、single-mux-round1 89、single-mux-ui-round1 176、workbench-ui 38、input-feedback 24、media-drop 25、environment-ui 23、components 31、component-install-ui 37、component-install-all 41、component-uninstall 26、component-uninstall-ui 61；13/13、656/656，与 dbe9f56 基准一致。

日志：`artifacts/arch-corrections/3d-request-log/`；真实套件正在续跑，最终逐成品对照尚未完成。本节取代此前续跑时对 SPEC 反向投影的实现描述；历史证据保留。

本轮六项真实命令均为 `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- <参数>`（desktop/，2026-10-14 Asia/Shanghai），全部失败 0：

| 参数 | 计数 | 证据 |
|---|---|---|
| --integration | 121/121 | [报告](../../artifacts/phase1/20261014-040759-5a03b2a1311741279fa7105cca80b0b6/report.json) |
| --media-drop-real | 37/37 | [报告](../../artifacts/automatic-drop/real-9d9fe9ce65d54d169dffd6d54541bfec/report.json) |
| --ui-integration artifacts/phase1/20261014-040759-5a03b2a1311741279fa7105cca80b0b6 artifacts/arch-corrections/3d-request-log/ui.png | 39/39 | [报告](../../artifacts/single-mux-round1-b/real-5c52172666c443bb85f7d2a9ae01c7c5/report.json) |
| --warning-synthetic-bold-real artifacts/runs/20261009-115919-18aeaf51f58d43e5a4b935b9ce8b8f5b/task.log | 9/9；SyntheticBoldExpected=true | [报告](../../artifacts/warning-policy/real-91eef78266ea49549891d320369e35da/report.json) |
| --component-install-all-real | 15/15，实际联网 | [报告副本](../../artifacts/arch-corrections/3d-request-log/component-install-all-real-report.json) |
| --component-uninstall-real artifacts/arch-corrections/real-baseline/packages | 17/17 | [报告副本](../../artifacts/arch-corrections/3d-request-log/component-uninstall-real-report.json) |

合计 6/6、238/238，与 dbe9f56 基准一致。工具版本：MKVToolNix 100.0、FFmpeg 8.1.1-full、AFS 2.1.1、fontTools 4.61.1、Python 3.12.10；联网安装验证使用 MKV 101.0、FFmpeg 8.1.1-essentials、AFS 2.1.1，固定卸载缓存含 assfonts 0.7.3。真实命令输出和计数摘要位于 `artifacts/arch-corrections/real-baseline/3d-request-log-*`。

3d 代码与通用/六项真实验证完成，本地提交；最终 e005923 逐成品对照待运行，暂未推任何分支。比较采用同一输入请求与工具二进制，通过两份独立 Core host 重跑，严格脚本 `scripts/Run-ArchComparison.ps1` 和 `scripts/Compare-ArchArtifacts.ps1` 提交；不引用或采用归一化草稿。基准工作树已核对为 e0059231aef2d8c5fc8a936e91c83378c1a1dcd9，其先前四项 121/37/39/9 的报告保留在 3d-resume/；最终重新配对执行两个公开旧入口。

### 3d ① 最终逐成品对照：差异停止（2026-10-14）

实际命令（desktop/）：
```powershell
./scripts/Run-ArchComparison.ps1 -BaselineRoot 'C:/Users/ZhenXun/.codex/worktrees/arch-3c3d-baseline/mux-deck' -CurrentRoot 'E:/Video/mux-deck' -CaseManifest 'artifacts/arch-corrections/3d-resume/comparison-cases.json' -EvidenceDirectory 'artifacts/arch-corrections/3d-request-log/paired' -Mkvmerge 'C:/Program Files/MKVToolNix/mkvmerge.exe' -Mkvextract 'C:/Program Files/MKVToolNix/mkvextract.exe'
```

两份独立 Core host 均实际构建通过（各 0 警告 / 0 错误），随后使用同一原请求、输入文件与工具各生成一份成品。首项 integration / single-sc 最终比较有差异，脚本立即退出，不继续其余成品：

- 一致：逐轨类型/编码/语言/名称/default/forced（含 codec_id、尺寸/声道/采样率）、音频 fullraw SHA-256、检查项与顺序、告警与次数、旧进度阶段/文字、输入哈希、工具二进制哈希；两侧输入运行前后哈希保持。
- 差异：附件身份和 SHA-256、ASS 提取全文。示例旧附件 `001-arial.0.YZHZCGEL.ttf` / `2166C3189BE532F71A42410C437DEA6AD42A055D42BD658D382A914CCCEABB77`，新附件 `001-arial.0.F3GH93A8.ttf` / `27A583030A34B3CA426B0335D17F10BBC5135DDEDD27B0ADFBD597CB6D6C1EC9`；对应 ASS 中的字体映射与引用亦不同。未做字体名、字体字节或 ASS 全文归一化，不计算整个 MKV 哈希；原因未验收，不据此认定只是随机性。

| 套件 | 本轮新代码计数（基准相同） | 最终逐成品结论 |
|---|---|---|
| integration | 121/121 | 差异：single-sc 附件与 ASS；本套其余 4 项未继续 |
| media-drop-real | 37/37 | 未验收：按首项差异停止，6 项未运行 |
| ui-integration | 39/39 | 未验收：3 项未运行 |
| 完整电影 | 9/9；SyntheticBoldExpected=true | 未验收：1 项未运行 |

[逐项报告](../../artifacts/arch-corrections/3d-request-log/paired/integration-single-sc/comparison.json)、[停止摘要](../../artifacts/arch-corrections/3d-request-log/paired/summary.json)。1/15 已对照、14 项未验收。按用户补充停止规则停止，不改测试/基准/计划或放宽架构护栏；仅补录证据到未推送的 3d 本地提交。3c / 3d 均已本地提交，主分支和 review/3c-3d 均未推送，未 pull --rebase、未运行 CI，等待用户决定该差异的处理。

### 3c/3d 工具不确定性来源与授权对照（2026-10-14）

用户批准本地对照脚本处理已证实的 AssFontSubset 不确定性，不改变生产 QC、现有 .NET/Smoke 测试、基准、架构护栏或 ADR。

① 来源证据：使用 e005923 独立工作树对应的 Core host，读取同一 integration-single-sc 原请求，对相同输入与工具连续运行两遍；两次公开旧入口均成功，输入前后 SHA-256 保持。命令为：
```powershell
dotnet run --project artifacts/arch-corrections/3d-request-log/paired/host-baseline/host.csproj --no-build -c Release -- <同一 integration-single-sc 请求 JSON> E:/Video/mux-deck <first-output> <first-result.json>
dotnet run --project artifacts/arch-corrections/3d-request-log/paired/host-baseline/host.csproj --no-build -c Release -- <同一 integration-single-sc 请求 JSON> E:/Video/mux-deck <second-output> <second-result.json>
```
旧、旧原始对照仍有附件身份、SHA-256、ASS 全文差异，其余七组一致。四份字体只有 name/head 表不同；name 变化仅为随机子集名称（name ID 1/3/4/6 等语言记录），head 差异全部在字节 8–11（checkSumAdjustment）或 28–35（modified），所有其他表原始字节一致。ASS 差异只在映射、Style.Fontname 和 fn 引用；使用各自映射还原后全文一致。依用户判定条件，来源为工具不确定性，不是本次流水线迁移引入的差异。[原始对照](../../artifacts/arch-corrections/3d-afs-proof/raw-comparison.json)、[逐字体定位](../../artifacts/arch-corrections/3d-afs-proof/source-proof.json)。

② 实现：Compare-ArchArtifacts.ps1 先保留 comparison.json.raw.json 和提取文件，再调用 Normalize-AfsEvidence.py。ASS 仅还原映射标识、Style.Fontname 和覆盖标签 fn，保留映射行及顺序；不统一换行/BOM/空白或替换普通对白。字体依原字体、文件名/face 与序号配对，name 所有记录与元数据保留、只换回原名，head 只排除 modified/checkSumAdjustment，其他每个表用原始字节 SHA-256 比较。附件数量/顺序/ID/MIME/大小严格保留，去掉文件名随机段；字体整体哈希由逐表验证替代，原始整体哈希保留在原报告。非字体附件仍严格比整体哈希。轨道、音频 fullraw、检查项、告警、进度、输入及工具哈希沿用原脚本严格结果，不处理。未知格式或超出授权的差异失败退出。脚本只用于本地验收，不进解决方案与 CI。比较解析器使用本机 Python 3.12.10 / fontTools 4.63.0；产物生成的原工具链及二进制哈希不变（原 fontTools 后端 4.61.1）。

③ 同一对照脚本用新规则重新比较①两份旧产物：完全一致，全部十组通过。[旧/旧新规则报告](../../artifacts/arch-corrections/3d-afs-proof/approved-v2/comparison.json)及同目录原始报告保存。

反例命令：
```powershell
python -X utf8 ./scripts/Test-AfsEvidence.py --source artifacts/arch-corrections/3d-afs-proof/raw-comparison.json -v
```
22/22，通过/失败/跳过 = 22/0/0。使用真实提取的 Arial 子集删除正文 English 实际使用的 U+0045（E）及其字形，确认 cmap 与 glyphOrder 都不再包含该字形，必须拒绝；修改真实 OS/2 表内容必须拒绝。还覆盖 Dialogue 正文/时序/普通文本里的随机字串、空白/换行、映射原名/重复行、无关 name 记录、head.created、附件数量/顺序/MIME/非随机文件名、原始附件哈希完整性，以及既有严格轨道/fullraw/检查/告警/进度失败结果。既有 .NET 676 项与受控 656 项不变；[反例输出](../../artifacts/arch-corrections/3d-afs-proof/counter-tests-final.txt)。

④ 全部 15 项新旧对照已完成，采用同一请求、输入与工具，分别调用 e005923 和本地 3d Core。命令：
```powershell
./scripts/Run-ArchComparison.ps1 -BaselineRoot 'C:/Users/ZhenXun/.codex/worktrees/arch-3c3d-baseline/mux-deck' -CurrentRoot 'E:/Video/mux-deck' -CaseManifest 'artifacts/arch-corrections/3d-resume/comparison-cases.json' -EvidenceDirectory 'artifacts/arch-corrections/3d-afs-approved-paired' -Mkvmerge 'C:/Program Files/MKVToolNix/mkvmerge.exe' -Mkvextract 'C:/Program Files/MKVToolNix/mkvextract.exe'
```

最终 15/15 项全部一致，每项十组比较均通过（旧代码重复运行 10/10；反例 22/22）。本次只修改本地对照脚本和任务文档，没有修改生产代码、原测试、基准、架构护栏或 ADR。

| 套件 | 成品输入 | 逐项结果 |
|---|---|---|
| integration | single-sc | [一致](../../artifacts/arch-corrections/3d-afs-approved-paired/integration-single-sc/comparison.json) |
| integration | dual-sc-tc | [一致](../../artifacts/arch-corrections/3d-afs-approved-paired/integration-dual-sc-tc/comparison.json) |
| integration | tc-only | [一致](../../artifacts/arch-corrections/3d-afs-approved-paired/integration-tc-only/comparison.json) |
| integration | mp4-to-mkv | [一致](../../artifacts/arch-corrections/3d-afs-approved-paired/integration-mp4-to-mkv/comparison.json) |
| integration | cancel-at-completed | [一致](../../artifacts/arch-corrections/3d-afs-approved-paired/integration-cancel-at-completed/comparison.json) |
| media-drop-real | srt-only | [一致](../../artifacts/arch-corrections/3d-afs-approved-paired/media-drop-real-srt-only/comparison.json) |
| media-drop-real | audio-only | [一致](../../artifacts/arch-corrections/3d-afs-approved-paired/media-drop-real-audio-only/comparison.json) |
| media-drop-real | multi-audio | [一致](../../artifacts/arch-corrections/3d-afs-approved-paired/media-drop-real-multi-audio/comparison.json) |
| media-drop-real | multi-srt | [一致](../../artifacts/arch-corrections/3d-afs-approved-paired/media-drop-real-multi-srt/comparison.json) |
| media-drop-real | multi-ass | [一致](../../artifacts/arch-corrections/3d-afs-approved-paired/media-drop-real-multi-ass/comparison.json) |
| media-drop-real | mixed | [一致](../../artifacts/arch-corrections/3d-afs-approved-paired/media-drop-real-mixed/comparison.json) |
| ui-integration | single-ass | [一致](../../artifacts/arch-corrections/3d-afs-approved-paired/ui-integration-single-ass/comparison.json) |
| ui-integration | sc-tc | [一致](../../artifacts/arch-corrections/3d-afs-approved-paired/ui-integration-sc-tc/comparison.json) |
| ui-integration | tc-only | [一致](../../artifacts/arch-corrections/3d-afs-approved-paired/ui-integration-tc-only/comparison.json) |
| movie | complete | [一致](../../artifacts/arch-corrections/3d-afs-approved-movie-retry/movie-complete/comparison.json) |

[15 项总摘要](../../artifacts/arch-corrections/3d-afs-proof/summary-all15.json)。首轮完整电影的旧入口在 NoOverwritePublisher.CopyToAsync 遇到 I/O 设备错误，源输入哈希保持，未生成可比较成品；[首次失败结果](../../artifacts/arch-corrections/3d-afs-approved-paired/movie-complete/baseline-result.json)和 task.log 保留。只在新目录重复电影输入，旧、新入口均成功，随后全部十组比较一致；其他 14 项未重跑或覆盖。电影重试的实际命令与前述命令相同，仅 CaseManifest 为 `artifacts/arch-corrections/3d-afs-proof/movie-retry-cases.json`、EvidenceDirectory 为 `artifacts/arch-corrections/3d-afs-approved-movie-retry`。

全部验收通过后，把本次脚本与文档并入尚未推送的 3d 提交，继续保持 3c / 3d 两个提交；仅发布 [review/3c-3d](https://github.com/Danyuyuyuyu/mux-deck/tree/review/3c-3d) 供审查，不向 v2/avalonia-rewrite 推送、不 pull --rebase。主分支仍等用户确认；确认后按已批准顺序 pull --rebase、推送主分支、确认 Desktop CI、删除临时分支。


### 3c 结构化结果修正遗留问题

用户指定：ToolCallContext.SubtitleIndex 和端口上的 index 参数本次保持原样，第 5 步统一处理索引的业务表达与过渡兼容；不在本次修正中更改它们的数值、含义或调用行为。


### 3c 结构化结果修正实际验证（2026-10-14）

按已接受 ADR-0001 及用户本次要求，追加 `refactor(desktop): [3c] 结构化结果修正`，仅发布 review/3c-3d 供审查，主分支等待用户确认。

- CheckResult / JobFailure / JobProgress 用强类型字段表达序号、数量、轨道 ID、名称、传统/IETF 语言、旗标、SHA-256、失败路径和诊断列表，不再编码 JSON 事实；删除 ReadTrackCheck 与转换层全部 JsonSerializer.Deserialize。每类轨道 QC 检查和拒绝使用独立码。
- 用例自己的输入、计划和 QC 拒绝使用内部 MuxRuleFailureException，结果 Message=null；端口失败保留原 Code / Message / Detail。IToolReadiness 的字体后端缺失使用 MUX_FONT_BACKEND_NOT_FOUND，旧文案由此码选择，不匹配工具名称后缀。
- 用例诊断只合并 DiagnosticProgress 回调，返回诊断仅为直接端口调用者的快照；字体提示在产生处报告，删去事后倒扣/补偿。测试证明失败前提示保留且回调/快照不会重复累计。文件端口 XML 写明实际文件/字幕结构失败，XML 检查同步区分文件与工具边界；依赖、不可变数据、唯一失败类型和取消断言保持。
- 原 676 项保留，新增 41：Application 用例 14 + 集中错误码 9，Core 转换 13，Infrastructure 就绪检查 5。历史公开 TrackQcFailed 常量及原验证仍保留，新用例只生成各类独立码。Desktop、Smoke、诊断白名单、基准、对照脚本与 ADR 均无改动。SubtitleIndex / index 遗留处理见上一节，第 5 步执行。

实际命令（工作目录 desktop）：
```powershell
dotnet restore MuxDeck.sln --locked-mode
dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test MuxDeck.sln --no-build -c Release
```
restore 11 个项目成功；Release 0 警告 / 0 错误；test 717/717、失败/跳过 0（Domain 298、Application 103、Core 54、Architecture 198、Infrastructure 64）。[通用验证输出](../../artifacts/arch-corrections/3c-structured-release/tests.txt)、[计数摘要](../../artifacts/arch-corrections/3c-structured-final/validation-summary.json)。最后仅校准文件端口 XML 的实际异常描述，补跑 `dotnet test tests/MuxDeck.Architecture.Tests --no-build -c Release --verbosity quiet`，198/198；随后再次执行上述严格 Release 构建与全量测试，仍为 0 警告 / 0 错误、717/717，见 [最终构建](../../artifacts/arch-corrections/3c-structured-final/build-final.txt)、[最终全量测试](../../artifacts/arch-corrections/3c-structured-final/tests-final.txt)。

受控实际命令使用 `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release`，有参数时在末尾加 `-- <参数>`：

| 参数 | 通过/总计 |
|---|---|
| 无参数 | 28/28 |
| `--warning-policy` | 57/57 |
| `--single-mux-round1` | 89/89 |
| `--single-mux-ui-round1` | 176/176 |
| `--workbench-ui` | 38/38 |
| `--input-feedback` | 24/24 |
| `--media-drop` | 25/25 |
| `--environment-ui` | 23/23 |
| `--components` | 31/31 |
| `--component-install-ui` | 37/37 |
| `--component-install-all` | 41/41 |
| `--component-uninstall` | 26/26 |
| `--component-uninstall-ui` | 61/61 |

13/13 套件、656/656，全部等于基准；各输出位于 `artifacts/arch-corrections/3c-structured-release/`。

六项真实实际命令：

| 命令 | 通过/总计 |
|---|---|
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --integration` | 121/121 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --media-drop-real` | 37/37 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --ui-integration E:\Video\mux-deck\desktop\artifacts\phase1\20261014-062839-8b4de1376c264f21ae2eab006662d653 artifacts/arch-corrections/3c-structured-final/ui.png` | 39/39 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --warning-synthetic-bold-real artifacts/runs/20261009-115919-18aeaf51f58d43e5a4b935b9ce8b8f5b/task.log` | 9/9 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-install-all-real` | 15/15 |
| `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --component-uninstall-real artifacts/arch-corrections/real-baseline/packages` | 17/17 |

真实合计 238/238、失败 0，电影 SyntheticBoldExpected=true；工具 MKVToolNix 100.0、FFmpeg 8.1.1-full_build、AFS 2.1.1、fontTools 4.61.1、Python 3.12.10，[实际版本](../../artifacts/arch-corrections/3c-structured-final/tool-versions.json)。安装包 MKV 101.0 / FFmpeg 8.1.1-essentials / AFS 2.1.1；卸载固定缓存 `artifacts/arch-corrections/real-baseline/packages`。各计数记录为 `artifacts/arch-corrections/real-baseline/3c-structured-final-*-summary.json`；[安装报告](../../artifacts/arch-corrections/3c-structured-final/component-install-all-real-report.json)、[卸载报告](../../artifacts/arch-corrections/3c-structured-final/component-uninstall-real-report.json)。

新旧对照实际命令：
```powershell
./scripts/Run-ArchComparison.ps1 -BaselineRoot 'C:/Users/ZhenXun/.codex/worktrees/arch-3c3d-baseline/mux-deck' -CurrentRoot 'E:/Video/mux-deck' -CaseManifest 'artifacts/arch-corrections/3d-resume/comparison-cases.json' -EvidenceDirectory 'artifacts/arch-corrections/3c-structured-paired' -Mkvmerge 'C:/Program Files/MKVToolNix/mkvmerge.exe' -Mkvextract 'C:/Program Files/MKVToolNix/mkvextract.exe'
```
14 项已完整通过；电影首轮旧入口成功，但执行会话中断，新入口没有结果/对照报告，任务日志停在封装写入阶段。原输出和日志保留，不将中断认定为差异；只在新目录以相同脚本、输入与工具重跑电影。重跑命令参数仅将 CaseManifest 换为 `artifacts/arch-corrections/3c-structured-final/movie-retry-cases.json`、EvidenceDirectory 换为 `artifacts/arch-corrections/3c-structured-movie-retry`。未更改脚本或基准。

最终 15/15 项、150/150 组一致；沿用已批准的 AFS 不确定性处理，其余轨道、音频 fullraw、检查项、告警、进度、输入与工具仍严格比较，不比较整个 MKV 哈希。

| 套件 | 成品输入 | 结果 |
|---|---|---|
| integration | single-sc | [一致](../../artifacts/arch-corrections/3c-structured-paired/integration-single-sc/comparison.json) |
| integration | dual-sc-tc | [一致](../../artifacts/arch-corrections/3c-structured-paired/integration-dual-sc-tc/comparison.json) |
| integration | tc-only | [一致](../../artifacts/arch-corrections/3c-structured-paired/integration-tc-only/comparison.json) |
| integration | mp4-to-mkv | [一致](../../artifacts/arch-corrections/3c-structured-paired/integration-mp4-to-mkv/comparison.json) |
| integration | cancel-at-completed | [一致](../../artifacts/arch-corrections/3c-structured-paired/integration-cancel-at-completed/comparison.json) |
| media-drop-real | srt-only | [一致](../../artifacts/arch-corrections/3c-structured-paired/media-drop-real-srt-only/comparison.json) |
| media-drop-real | audio-only | [一致](../../artifacts/arch-corrections/3c-structured-paired/media-drop-real-audio-only/comparison.json) |
| media-drop-real | multi-audio | [一致](../../artifacts/arch-corrections/3c-structured-paired/media-drop-real-multi-audio/comparison.json) |
| media-drop-real | multi-srt | [一致](../../artifacts/arch-corrections/3c-structured-paired/media-drop-real-multi-srt/comparison.json) |
| media-drop-real | multi-ass | [一致](../../artifacts/arch-corrections/3c-structured-paired/media-drop-real-multi-ass/comparison.json) |
| media-drop-real | mixed | [一致](../../artifacts/arch-corrections/3c-structured-paired/media-drop-real-mixed/comparison.json) |
| ui-integration | single-ass | [一致](../../artifacts/arch-corrections/3c-structured-paired/ui-integration-single-ass/comparison.json) |
| ui-integration | sc-tc | [一致](../../artifacts/arch-corrections/3c-structured-paired/ui-integration-sc-tc/comparison.json) |
| ui-integration | tc-only | [一致](../../artifacts/arch-corrections/3c-structured-paired/ui-integration-tc-only/comparison.json) |
| movie | complete | [一致](../../artifacts/arch-corrections/3c-structured-movie-retry/movie-complete/comparison.json) |

[15 项总报告](../../artifacts/arch-corrections/3c-structured-final/summary-all15.json)。脚本反例命令 `python -X utf8 ./scripts/Test-AfsEvidence.py --source artifacts/arch-corrections/3d-afs-proof/raw-comparison.json -v`，22/22、失败/跳过 0，[输出](../../artifacts/arch-corrections/3c-structured-final/comparison-counter-tests.txt)。审查分支 Desktop CI 的实际结果见交付回报；主分支推送继续等待用户确认。


### 3c/3d 用户确认后的主分支推送

用户已确认：`git switch v2/avalonia-rewrite` → `git merge --ff-only review/3c-3d` → `git pull --rebase origin v2/avalonia-rewrite` → `git push origin HEAD:refs/heads/v2/avalonia-rewrite`。此次拉取时远端为 5ba3146，rebase 无需改写；按顺序保留 7c9a477、0f1927b、a3dbea9 三个提交，未压缩。[主分支 Desktop CI](https://github.com/Danyuyuyuyu/mux-deck/actions/runs/38530034209) completed/success，head_sha 为 a3dbea92f867a739aae4490e249ace1a493300d3，与远端完全一致；随后执行 `git push origin --delete review/3c-3d`，远端分支已删除。已补录上方 3c/3d 结果表，既有计数与报告路径不变。


### 3c 移除 Domain 旧检查文案实际验证

单独提交 `refactor(desktop): [3c] 移除 Domain 旧检查文案`：所有 CheckTracks 调用方都在测试中，已改为 CheckFacts。原有成功、关键字段篡改、传统/IETF 语言、未知类型、时长容差和失败前检查保留案例不变；断言由文案子串/非空，改为精确的检查/失败种类、轨道类型、序号、数量、完整轨道字段和顺序。删除 CheckTracks、LegacyTrackQcText、无人调用的 ErrorCodes.TrackQcFailed。旧文案仅保留 Core 转换层；CheckFacts 及其后全部正式规则正文与 a3dbea9 逐字一致，Application、Infrastructure、Core 转换、Desktop 和 Smoke 流程未改。

已删除旧码的原有效码数据案例改为 `MUX_TRACK_QC_FAILED` 必须拒绝的案例，未删除测试方法，总数保持 717。Domain 298、Application 103、Core 54、Architecture 198、Infrastructure 64；失败/跳过 0。

实际验证（desktop 工作目录）：
```powershell
dotnet restore MuxDeck.sln --locked-mode
dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test MuxDeck.sln --no-build -c Release
```
restore 11 个项目成功，Release 0 警告 / 0 错误，test 717/717。[构建](../../artifacts/arch-corrections/3c-domain-text-validation/build.txt)、[全量测试](../../artifacts/arch-corrections/3c-domain-text-validation/tests.txt)。

受控入口均为 `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release`，有参数时追加 `-- <参数>`：

| 参数 | 通过/总计 |
|---|---|
| 无参数 | 28/28 |
| --warning-policy | 57/57 |
| --single-mux-round1 | 89/89 |
| --single-mux-ui-round1 | 176/176 |
| --workbench-ui | 38/38 |
| --input-feedback | 24/24 |
| --media-drop | 25/25 |
| --environment-ui | 23/23 |
| --components | 31/31 |
| --component-install-ui | 37/37 |
| --component-install-all | 41/41 |
| --component-uninstall | 26/26 |
| --component-uninstall-ui | 61/61 |

13/13 套件、656/656，全部等于基准，输出保留在 `artifacts/arch-corrections/3c-domain-text-validation/`。按用户指定，正式代码路径未改，本次不重跑真实套件；沿用此前真实 238/238、逐成品 15/15 一致的报告，不将历史真实计数记录为本次执行。推送后的 Desktop CI 结果见交付回报；3e 未开始。
