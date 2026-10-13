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

- 整体移动：`ProcessRunner`、`WindowsProcessJob`、`ToolLocator`、`StoragePaths`、`IMuxToolRunner`/`MuxToolRunner`、`AfsDiagnosticCollector`、`FontToolsLocator`。
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

| 套件 | 命令参数 | 基准 | 运行位置 |
|---|---|---|---|
| Smoke | （无参数） | 28/28 | CI + 本地 |
| 分层与架构测试 | `dotnet test` | 32/32（1a） | CI + 本地 |
| 告警策略 | `--warning-policy` | 57/57 | CI + 本地 |
| 单个封装接纳 | `--single-mux-round1` | 89/89 | CI + 本地 |
| 单个封装 UI | `--single-mux-ui-round1` | 176/176 | CI + 本地 |
| 工作台 | `--workbench-ui` | 38/38 | CI + 本地 |
| 输入反馈 | `--input-feedback` | 24/24 | CI + 本地 |
| 自动导入（受控） | `--media-drop` | 25/25 | CI + 本地 |
| 环境 UI | `--environment-ui` | 23/23 | CI + 本地 |
| 组件检测（受控） | `--components` | 31/31 | CI + 本地 |
| 组件安装 UI | `--component-install-ui` | 37/37 | CI + 本地 |
| 组件安装（受控） | `--component-install-all` | 41/41 | CI + 本地 |
| 组件卸载（受控） | `--component-uninstall` | 26/26 | CI + 本地 |
| 组件卸载 UI | `--component-uninstall-ui` | 61/61 | CI + 本地 |
| 真实工具链 | `--integration` | 121/121 | 仅本地 |
| 真实多轨导入 | `--media-drop-real` | 37/37 | 仅本地 |
| 真实 UI | `--ui-integration <fixture>` | 39/39 | 仅本地 |
| 完整电影重放 | `--warning-real <task.log>` | 9/9 | 仅本地 |
| 真实组件安装 | `--component-install-all-real` | 15/15 | 仅本地，需联网 |
| 真实卸载循环 | `--component-uninstall-real <缓存目录>` | 17/17 | 仅本地 |

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
| 2a | 本地验证完成，待推送及 CI | locked restore 10/10；Release 0 警告 / 0 错误；dotnet test 208/208；受控 13/13、656/656 | TrackPolicy、LanguageRules、OutputNaming 已迁入 Domain；新增 Domain 69、架构 3，原 136 全部保留；详见 2a 实际验证。 |
| 2b | 未开始 | | |
| 2c | 未开始 | | |
| 3a | 未开始 | | |
| 3b | 未开始 | | |
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

共享文件只更新 `docs/agents/skeleton.md` 中本次迁移现状；`MuxDeck.sln`、锁文件、`Directory.Build.props` 不变，未改 ADR 或步骤计划。未重放真实媒体工具链、原生选择器、Explorer、高 DPI 或播放器；真实工具链基准仍需在 3b 前按上方要求重跑。本地验证完成，推送后补入实现提交与 Desktop CI 链接。

## 发现的旧缺陷

（已修复：修正 A；提交 c636e91，Desktop CI 38383347658 已通过。以下保留发现背景。）`Desktop/App.axaml.cs` 将未限定的 `Application` 用作 Avalonia 基类；当目标程序集首次包含 `MuxDeck.Application` 命名空间的业务类型时，编译器会优先解析同名命名空间并报 CS0118。首个反例因此未计为架构护栏验收，改用独立禁止命名空间后两条护栏均确认失败。1b 只定义 `MuxDeck.Contracts`，没有触发该冲突；该缺陷已由修正 A 的 c636e91 修复，并经临时同名类型与完整 CI 验证。
