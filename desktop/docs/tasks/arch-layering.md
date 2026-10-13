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

满足[开发流程](../agents/migration.md#架构分层重构)中的完成条件，并在下方逐步骤记录：提交链接、实际验证命令与计数、真实工具链对照结论、未覆盖项。

## 实施结果

| PR | 状态 | 验证 | 备注 |
|---|---|---|---|
| [1a · PR #3](https://github.com/Danyuyuyuyu/mux-deck/pull/3) | 已合并（`c4f932e`） | 锁定 restore：9/9 项目；Release build：0 警告 / 0 错误；dotnet test：32/32（架构规则 27 + 扫描反例 5），失败/跳过 0；Smoke：28/28 | 三个空生产项目、三个 xUnit 测试项目、过渡引用、NetArchTest 护栏、CI test 步骤与全部新增项目锁文件；按已接受 ADR-0001 实施，未移动代码、定义契约或修改行为。Domain/Application 业务测试 0；未重放真实工具链或原生 UI。 |
| 1b | 未开始 | | |
| 2a | 未开始 | | |
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

## 发现的旧缺陷

（重构中发现的旧版缺陷记录于此，单独提交修复。）
