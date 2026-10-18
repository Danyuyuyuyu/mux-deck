# 代码骨架

本文规定 v2 的代码职责、依赖和契约。路径均相对于 `desktop/`；功能顺序见 [开发流程](migration.md)，验证要求见 [验证文档](verification.md)。分层决策的背景与取舍见 [ADR-0001](../adr/0001-layered-architecture.md)，规则以本文为准。

## 项目与依赖

目标结构为四个生产项目：

```text
src/MuxDeck.Domain/          纯业务规则与值类型：无文件、进程、时间或 UI 副作用
src/MuxDeck.Application/     用例编排、草稿会话、跨功能操作协调、端口接口；Contracts/ 承载对外契约
src/MuxDeck.Infrastructure/  端口实现：外部工具适配、进程与 Job、工具定位、文件落位、下载、持久化
src/MuxDeck.Desktop/         Avalonia 视图、ViewModel、平台交互；Program 与 Composition/ 为唯一装配入口
tests/                      按层划分的自动化验证与验收入口
```

```text
Domain ← Application ← Infrastructure
              ↑               ↑
           Desktop ───────────┘（仅 Program 与 Composition/）
```

- Domain 不引用任何项目或第三方包，禁用文件、进程、网络和 Console；Environment 按成员禁止 `GetEnvironmentVariable`、`GetEnvironmentVariables`、`SetEnvironmentVariable`、`ExpandEnvironmentVariables`、`MachineName`、`UserName`、`UserDomainName`、`CurrentDirectory`（读/写）、`GetFolderPath`、`GetCommandLineArgs`、`CommandLine`、`Exit`、`FailFast`、`ProcessId`、`ProcessPath`，由成员级 IL 护栏检查。经用户明确授权，放行 `CurrentManagedThreadId`、`NewLine`、`ProcessorCount` 等只读成员及 yield 状态机；`TickCount` / `TickCount64` 仍由下方时间护栏禁止。
- Application 只引用 Domain，不引用 Avalonia、Infrastructure 或具体工具；Application / Contracts 禁用 System.Diagnostics.Process、System.Net；经用户明确授权，System.IO 改为成员级白名单，仅放行 Path.Combine、Join、GetFileName、GetFileNameWithoutExtension、GetExtension、GetDirectoryName、ChangeExtension、HasExtension、DirectorySeparatorChar、AltDirectorySeparatorChar。Path 未列出的成员（含 GetFullPath、GetTempPath、GetTempFileName、GetRandomFileName）以及 System.IO 其他所有类型（含 File、Directory、FileInfo、DirectoryInfo、Stream 及其派生类、InvalidDataException）仍禁用；签名、局部变量、调用和 lambda/async 状态机均检查。经用户批准，Application/Contracts 的 IL 护栏同时拒绝包含 `System.IO.` 的字符串字面量（含编译器生成的嵌套类型）。Domain 的 System.IO 整命名空间禁令不变。
- Infrastructure 引用 Application（实现其端口）与 Domain，不引用 Desktop 或 Avalonia。
- Desktop 引用 Application 与 Infrastructure；`ViewModels/` 与 `Views/` 只使用 `MuxDeck.Contracts` 命名空间、Domain 值类型和用例接口，不构造 Infrastructure 类型。只有 `Program.cs` 与 `Composition/` 可使用 Infrastructure 命名空间。
- Domain / Application / Contracts 禁止直接读取当前时间或生成 ID（DateTime / DateTimeOffset 当前值、TimeProvider.System、Guid.NewGuid、Stopwatch、Environment.TickCount / TickCount64），lambda / async 状态机同样受检查。
- 测试项目只引用其验证的生产项目。上述规则由架构测试固定，违反即测试失败。

1a 已建立 Domain、Application、Infrastructure 三个生产项目；1b 已在 Domain/Application 定义值类型与契约，3b 已迁出 Infrastructure 工具/进程/文件适配，旧流程与调用方仍位于 Core 与 Desktop，按[架构分层重构](../tasks/arch-layering.md)逐步搬迁。过渡期的实际项目引用如下（目标依赖仍以上述规则为准）：

| 项目 | 过渡期实际直接项目引用 |
|---|---|
| Domain | 无 |
| Application | Domain |
| Infrastructure | Application、Domain |
| Core | Domain、Application、Infrastructure（3b 用户批准过渡，第 5 步移除） |
| Desktop | Core、Application、Infrastructure（4a-1 接入，仅 Program / Composition 使用） |

Core → Infrastructure 为 3b 用户批准的单向过渡，到第 5 步移除；Infrastructure 不引用 Core 的护栏不变。4a-1 已接入 Desktop → Infrastructure，只有 Program / Composition 使用；Desktop → Core 保留到第 5 步。StoragePaths、ToolLocator 与组件实现已在 4a-2 迁入 Infrastructure；4a-3 已将导入识别与校验迁入 Application，Core 只保留旧入口兼容。上述过渡引用不豁免目标命名空间规则：Core 内先改为 `MuxDeck.Domain`、`MuxDeck.Contracts`、`MuxDeck.Application`、`MuxDeck.Infrastructure` 的类型与对应新项目一起接受架构测试，搬迁时只移动文件。每完成一步，在同一改动中更新本节与下方“迁移前现状”。

测试按层划分：

| 项目 | 内容 |
|---|---|
| `tests/MuxDeck.Domain.Tests` | 纯规则单元测试，覆盖有效输入与关键反例 |
| `tests/MuxDeck.Application.Tests` | 假端口下的流程、失败、取消、清理与不覆盖落位 |
| `tests/MuxDeck.Core.Tests` | 过渡期旧模型与新契约转换，第 5 步随旧转换处理 |
| `tests/MuxDeck.Infrastructure.IntegrationTests` | 真实工具、进程、编码与进程树 |
| `tests/MuxDeck.Architecture.Tests` | 项目引用与命名空间依赖规则 |
| `tests/MuxDeck.Smoke` | 保留为验收入口与 headless UI 检查 |

1a 选定并固定测试依赖：`xunit.v3` **3.2.2**、`xunit.runner.visualstudio` **3.1.4**、`Microsoft.NET.Test.Sdk` **17.14.1**；架构测试使用 `NetArchTest.Rules` **1.3.2**。所有新增项目提交 `packages.lock.json`，继续使用 `dotnet restore MuxDeck.sln --locked-mode`。

Domain.Tests 只直接引用 Domain，Application.Tests 只直接引用 Application；Architecture.Tests 验证全部生产项目（过渡期包含 Core），Smoke 仍只直接引用 Desktop；Infrastructure.IntegrationTests 只直接引用 Infrastructure。1b 初始新增 Domain 值类型测试 8 项、Core 旧模型转换测试 26 项；修正 B 后 Domain 为 15 项、Application 契约值相等测试 4 项，修正 C 后架构测试 91 项；2a 后 Domain 84 / 架构 94，2b 后 Domain 218 / 架构 95；输入位置枚举合并后 Application 9；2c 后 Domain 294 / 架构 98；Environment 经授权修正后架构 127，原有 427 项全部保留，新增 29 项成员护栏。3a 后 Domain 298 / Application 10 / Core 37 / 架构 140，共 485 项，仅新增 29，原 456 项保留。上述为 3a 历史计数；3c 已新增 27 项假端口流程用例，3d 新增旧请求日志回归 1 项。Core.Tests 只直接引用 Core，不放宽既有分层测试依赖。架构测试检查 restore 后的实际直接项目引用、Domain 包依赖、程序集与命名空间依赖、Views/ViewModels 的契约与接口边界，以及 Infrastructure 只能由 Program/Composition 使用的装配边界（含源码路径）。字段、泛型、构造调用和异步方法反例验证 NetArchTest 的扫描能力。

## 契约

`MuxDeck.Contracts` 是 Desktop（及以后其他入口）与 Application 的交接格式：任务规格、进度、结果、诊断和用例接口。

- 命名空间固定为 `MuxDeck.Contracts`，文件位于 `src/MuxDeck.Application/Contracts/`。出现第二个调用方时拆为独立项目，命名空间不变。
- 只依赖 `System.*` 与 `MuxDeck.Domain`。`TrackKind`、`LanguageTag`、`TrackFlags` 等值类型归 Domain，由契约引用。
- 只含不可变 record、枚举和用例接口（错误码常量允许 static class）；不含读写文件、调用服务或依赖当前时间的逻辑。
- 类型全部 `public`，不以 `InternalsVisibleTo` 访问内部成员，不暴露 Application 内部类型（如任务临时目录）。
- 结果结构化：成败用枚举表达；检查项由 `CheckResult(Code, Status)` 及强类型事实字段表达；失败由 `JobFailure(Code, Stage, Message, Detail)` 及失败对象字段表达。界面不解析文案或日志判断成败。
- 工具诊断码以[工具诊断分类](diagnostics.md)为唯一来源；任务错误码集中定义在 `Contracts/ErrorCodes.cs`。
- 任务阶段 `JobStage` 只含单任务阶段；组件检测、安装、卸载使用组件契约自己的阶段。
- CheckResult 直接携带 Sequence、Count、TrackId、Name、Language / LanguageIetf、TrackFlags 与 ExpectedHash / ActualHash；JobFailure 以 Sequence / Path 表达失败对象，Detail 仅保留原始原因，禁止用 JSON 字符串编码事实。JobProgress 的 Sequence / Total 表达子集化序号/总数，Diagnostics 为 EquatableArray<Diagnostic>，不保留 JSON Detail。旧结果向新契约转换的无法重建事实的原文只存 LegacyText。
- 每种轨道 QC 检查使用独立的 QC_RETAINED_VIDEO、QC_RETAINED_AUDIO、QC_SUBTITLE_TRACK、QC_ADDED_AUDIO_TRACK；对应失败码为 MUX_VIDEO_COUNT_MISMATCH、MUX_AUDIO_COUNT_MISMATCH、MUX_VIDEO_METADATA_MISMATCH、MUX_AUDIO_METADATA_MISMATCH、MUX_DURATION_MISMATCH、MUX_SUBTITLE_COUNT_MISMATCH、MUX_SUBTITLE_METADATA_MISMATCH、MUX_ADDED_AUDIO_METADATA_MISMATCH。用例自己的输入、计划与 QC 拒绝由 Application 内部 MuxRuleFailureException 表达，输出 JobFailure.Message=null；端口失败保留 ToolFailureException 原始 Code / Message / Detail。
- 改变契约属于接口变更，在同一改动中更新调用方、本节与契约相关测试。

目标契约与迁移前对应：

| 契约 | 作用 | 迁移前 |
|---|---|---|
| `MuxJobSpec`、`ExternalTrackSpec`、`SourceTrackSelection`、`FontSpec`、`OutputSpec` | 单任务不可变规格 | `MuxRequest`、`SubtitleInput`、`AudioInput` |
| `JobProgress(Stage, StepCode, Percent, Message)` | 阶段进度 | `MuxProgress` |
| `MuxJobResult`、`CheckResult`、`JobFailure`、`Diagnostic`、`OutputArtifact`、`LogRef` | 结构化结果 | `MuxResult`（`Checks` 文本、`Error` 字符串）、`MuxWarning` |
| `MediaProbeResult`、Domain `MediaTrackInfo` 与附件类型 | 共用 Domain 轨道事实的只读探测结果 | `MediaProbe`、`MediaTrack`（`Type` 为字符串）、`MediaAttachment` |
| `InputItem`、`ImportPlan` | 导入准备与提交 | `InputCandidate`、`InputAcceptancePlan`、`MediaImportPlan` |
| `IRunSingleMux`、`IProbeMedia`、`IPrepareImport`、`IManageComponents` | 用例接口 | 直接调用 `SingleMuxService`、`ComponentInstaller` 等 |
| `OperationKind`、`IOperationLease` | 跨功能占用与互斥 | `ComponentOperations.Gate` 与 `MainWindowViewModel` 联动 |

### 1b 契约与旧模型过渡

- LanguageTag 的相等与哈希按 Value 比较，default 与空字符串相等；MediaTrackInfo 的 HasDefaultFlag / HasForcedFlag 必须显式传入，不再默认猜测元数据存在。ErrorCodes 为 static 常量类，现有常量和调用写法不变。
- 输入契约 `InputItem.Kind` 直接使用 Domain 的 `InputLocationKind`（LocalFile / LocalDirectory / NonLocal / Unknown）；不在 Contracts 中重复定义输入位置枚举。Desktop 的旧候选类型暂由边界映射，随草稿迁移处理。
- Domain 已定义 `TrackKind`、`LanguageTag`、`TrackFlags`、`TrackId`。语言值保留原字符串（含空值和传统/IETF 标签），1b 不引入规范化或匹配规则。
- `Contracts/` 已定义单任务规格/结果/进度、媒体探测、导入计划、组件操作及协调器接口。集合统一使用 Domain 的 `EquatableArray<T>`（只读包装 ImmutableArray，按元素比较相等与哈希，default 视为空），仅保存数据；导入会话由调用方提供 ID 与修订号，不暴露 ViewModel 或 Application 内部对象。组件阶段独立于 `JobStage`；3e 已实现 Application 内存协调器与组件用例，过渡装配及组件外部操作暂留 Core，到 4a 搬迁。
- `MuxJobSpec.Tools` 显式携带工具、日志、缓存及独立 fonttools 后端配置；Core 的 `Muxing/LegacyMuxContractConverter.cs` 提供 `ToSpec(MuxRequest, Workspace/ToolConfiguration)`、`ToLegacyRequest(MuxJobSpec)` 与 `ToResult(MuxResult)`。3d 的旧 SingleMuxService 已通过转换调用新用例；ToResult 仍用于显式转换历史旧结果，保持原字段含义。3a 新增 ToTrackInfo / ToProbeResult 纯转换，旧 QC 边界已调用 ToTrackInfo。
- 旧请求的字幕与音频各自保留顺序，新规格先列字幕、再列音频；空输出名与 null 分别原样往返。旧模型无法表达的源轨选择、跨类型交错顺序、字幕来源轨号或无来源轨号的音频，转换回旧请求时明确拒绝，不丢弃字段。
- 旧检查文本使用唯一过渡码 `ErrorCodes.LegacyCheck`（`MUX_LEGACY_CHECK`），原文完整保存在 `CheckResult.LegacyText`，状态为 `NotEvaluated`，不根据文案判定成败；旧失败码为 `MUX_LEGACY_FAILURE`，取消码为 `MUX_CANCELLED`。旧结果未记录失败阶段，暂为 `JobStage.Unknown`。旧入口调用新用例时使用真实阶段与结构化信息；上述过渡映射仅用于显式 ToResult 历史转换。
- 旧 warnings 的 code、message、出现次数及顺序原样转换；成品、日志路径及 nullable 失败消息保留。工具诊断白名单仍只由 `diagnostics.md` 维护。契约的 public、归属、依赖、不可变数据与无友元程序集规则由架构测试检查；除 string 外公开集合只能是 EquatableArray<T>，拒绝其他集合及可写 setter，static 常量类豁免数据形状检查；依赖与可见性两条护栏已通过本地故意违反确认。

## 职责与落位

| 职责 | 归属 | 对外表达 |
|---|---|---|
| 轨道策略、默认/强制规则、语言标签、命名模板、输入类型识别、接纳校验、QC 判定、诊断分类 | Domain | 纯函数或值类型，输入与结果明确，无副作用 |
| 草稿会话 `MuxDraft`：草稿版本、准备→提交、运行时锁定、生成 `MuxJobSpec` | Application | 单个与批量共用，规则调用 Domain |
| 单任务流水线 | Application 单任务用例 | 一个 `IRunSingleMux.RunAsync` 入口，接收规格、进度与取消 |
| 批量队列编排 | Application 批量用例 | 逐项调用单任务入口，管理顺序与逐项结果 |
| 环境检测、组件安装与卸载编排 | Application 组件用例 | 组件契约表达状态、阶段与结果 |
| 跨功能互斥与关闭等待 | Application `IOperationCoordinator` | 租约表达占用；关闭时等待全部租约释放 |
| 外部工具参数构建、输出解析、产物与错误转换 | Infrastructure 工具适配 | 实现 `IMediaProber`、`IFontSubsetter`、`IMuxer`、`IMediaExtractor` 等端口 |
| 进程、Job Object、工具定位、临时目录、不覆盖落位、下载、业务设置持久化 | Infrastructure 基础能力 | 屏蔽技术细节，不决定业务成败 |
| 页面状态投影、命令、选中行、面板开关、列宽、焦点、通知 | Desktop ViewModels | 属性和命令；不写业务规则、不拼工具参数 |
| 布局、原生选择器、剪贴板、打开路径、拖放 | Desktop Views 与平台服务 | 视图行为，经小接口（如 `ISingleMuxInteractions`）可替换 |
| 对象创建、依赖注入、窗口接线 | Desktop `Program` 与 `Composition/` | 唯一装配入口；不使用静态全局服务 |

### 端口

- 只在真实外部边界建立端口：外部工具、进程、文件落位、下载、持久化、时间与 ID。名称表达业务能力（如 `IFontSubsetter`），不建通用仓储、事件总线、中介者或万能任务接口。
- 端口接口定义在 Application，实现位于 Infrastructure，测试替身位于测试项目。
- Application/Ports/Failures 的 ToolFailureException(Code, Message, Detail) 是端口统一失败类型：工具退出码异常、输出解析失败或出现 Fatal 级诊断都抛该类型；取消抛 OperationCanceledException。XML 失败与取消约定只写在执行外部工具或文件操作的方法上，纯文件方法明确描述路径规范化、复制、目录创建或读写失败（MUX_WORKSPACE_IO_FAILED），不声称调用工具；接口类型与只读属性不重复声明。当前失败码集中在 Contracts.ErrorCodes（MUX_TOOL_TIMEOUT、MUX_TOOL_NOT_FOUND、MUX_TOOL_EXIT_FAILED、MUX_TOOL_OUTPUT_INVALID、MUX_TOOL_FATAL_DIAGNOSTIC、MUX_TARGET_ALREADY_EXISTS、MUX_WORKSPACE_IO_FAILED），构造拒绝未知码，并支持保留 InnerException；不另立诊断白名单，Detail 保留原始原因。
- ToolCallContext 工具调用中的命令与 stdout/stderr 日志采用尽力写入：日志写入失败跳过，不改变工具结果，也不跳过 Fatal 分类；直接调用 ITaskWorkspace.WriteLogAsync 仍按文件端口约定抛失败，实际工具取消仍传播 OperationCanceledException。超时使用 MUX_TOOL_TIMEOUT，ToolCatalog 定位缺失使用 MUX_TOOL_NOT_FOUND，Message 保留原文案。
- IMuxInputReader 收集输入/目标文件事实与字幕原文；IMuxEvidenceReader 管理 SRT 副本和提取证据，读取大小、全文和 SHA-256；IMediaReferenceBuilder 建立同工具往返参考。IToolReadiness 对缺少 pyftsubset 或 ttx 能力使用 MUX_FONT_BACKEND_NOT_FOUND，转换层按此码选择旧文案，不匹配消息末尾。
- 用例诊断仅从 ToolCallContext.DiagnosticProgress 合并；端口在诊断产生时报告新增次数，即使之后失败也保留。FontSubsetResult / MediaMuxResult 中的 Diagnostics 是直接端口调用者的快照，用例不重复合并它们；字形提示在产生处报告，不做事后计数倒扣。实时与最终工具流复核仍由收集器去重，诊断白名单不变。
- 中间文件使用 Application/Ports 的 WorkFile(Path)：修正字幕、子集字体、提取目标与临时封装产物都不使用 OutputArtifact。IArtifactPublisher 接受 WorkFile，只有不覆盖发布成功才返回 Contracts.OutputArtifact，作为交给界面的最终成品。失败异常按 Exception 专门检查，所有端口数据继续为不可变 record/EquatableArray；既有 Contracts 与 Ports 数据护栏保持不变。
- `IMuxToolRunner` 已随 3b 迁入 Infrastructure；Core 过渡引用与原有 Smoke 注入仍需 public 可见性，后续入口迁移完成后成为 Infrastructure 内部依赖。`--warning-policy` 等依赖它的回归改为替换工具端口后再收紧其可见性。

### 单任务流水线

```text
检查输入 → 探测源 → 子集化 → 生成封装计划 → 封装临时成品 → 成品校验 → 不覆盖落位
 Domain   IMediaProber IFontSubsetter Domain   IMuxer     IOutputVerifier IArtifactPublisher
```

每阶段输入输出为 record，阶段转换与进度由用例负责，工具适配只解析工具自身进度。临时目录、任务日志与清理由 `TaskWorkspace`（`IAsyncDisposable`）负责，成功、失败、取消与异常路径都经过同一清理。

### 迁移前现状

以下两节记录迁移前的文件落位与 Desktop 行为约定。行为约定（接纳、草稿修订、Overlay、键盘、结果展示等）在迁移后继续有效；文件归属随[架构分层重构](migration.md#架构分层重构)改写，搬迁哪一部分就在同一改动中更新对应条目。

#### 2a 轨道与命名已迁入 Domain

- `src/MuxDeck.Domain/TrackPolicy.cs` 是 SC/TC 默认轨、源 A/V 保留、来源纳入投影与导入默认旗标的唯一规则位置。调用方提供槽位存在、轨道类型和外加纳入选择；旧空草稿仍先显示 SC 默认，实际组合变化后按槽位重算。4b-2 后编辑守卫、修改时机与草稿修订由 Application `MuxDraft` 持有，Desktop 只投影。新增 `ImportedSubtitleIsDefault`（仅当前没有任何字幕行时默认）与 `ImportedAudioIsDefault`（源声明默认且为默认）供导入提交复用。
- `LanguageRules.cs` 负责基于不含扩展名的文件名推断语言，以及旧封装 QC 的传统/IETF 标签匹配。传统/IETF 原字符串保留，IETF 仅为 null 时才允许匹配回退；语言推断保留旧标签边界、优先顺序和大小写处理。
- `OutputNaming.cs` 负责默认后缀、自定义名称与默认名拼接。`ResolvePreview` 在无文件名且无源视频时返回 null；“源文件名”占位与 `源文件名.muxed.mkv` 的界面补全由 Desktop 用 Domain `DefaultFileName` 完成，Domain 不含中文文案。Desktop 预览将空白视为自动命名；旧服务仅将 null 视为自动命名，空白仍由原校验拒绝。路径拆分、文件存在性、合法文件名校验和落位均留在调用边界。
- 原位置只调用上述 Domain 规则；规则不使用文件、目录、进程、当前时间或 ID 生成 API。Domain 单元测试注明对应的既有 Smoke 场景，架构测试逐个检查三个规则类型并保留全命名空间护栏。

#### 2b 输入识别与接纳已迁入 Domain

- `src/MuxDeck.Domain/InputRules.cs` 是媒体/字体扩展名、固定槽位类型与数量、候选来源类型、完整路径事实校验、规范化路径比较、去重及单字体目录的唯一规则位置；自动导入中的源视频数量、字幕/音频探测类型和已加入轨道身份也由该规则判定。
- `MuxDeck.Core.Muxing.MediaFileTypes` 与旧 `SingleMuxService.IsFont` 仅提取扩展名并调用 Domain，保留现有调用方。Core 按运行平台决定路径大小写策略，Desktop 仍按 Windows 大小写无关策略比较。
- `Infrastructure/SystemPathFacts.cs` 同时提供批量事实（同步拖入反馈与 `PrepareImportUseCase`）和固定槽位事实（`CollectForFixedSlot` / `ISlotPathFacts`）。固定槽位按旧 `ValidateCandidate` 先 `GetFullPath` 再裁剪末尾分隔符，存在性、扩展名与非法字符基于裁剪路径；原始 `Path` 仍用于完整路径与设备前缀判定。`ISlotPathFacts` 的同步 `Collect` 带可选取消令牌以满足端口护栏，实现先检查取消再收集；`MuxDraft` 同步入口不额外传令牌。`MediaImport.cs` 只负责 ViewModel 状态投影。规范化键先在边界收集，再交给 Domain 比较；Domain 不使用 `System.IO`，不查询文件或目录。
- 固定槽位仍只接纳 MKV/MP4 与 ASS，自动导入保留原有广泛视频/音频/ASS/SRT/字体入口。两者的设备路径与规范化细节差异保持原行为，不统一或扩大允许范围。
- 4b-2 已将固定槽位准备/提交移入 Application `MuxDraft`（同步与异步共用同一校验核心），编辑锁与修订号由草稿持有；Desktop 通过 Core 过渡门面投影。SC/TC 去重比较最终槽位集合；同音频容器按真实 Track ID 区分，字幕按文件身份去重。Domain 规则测试与副作用架构检查覆盖这些边界。

#### 2c 诊断、QC 与 ASS 解析已迁入 Domain

- `DiagnosticPolicy.cs` 维护唯一 AFS 物理行分类实现，ANSI、精确颜色白名单、未知级别/诊断与原 code/message 保持不变。Infrastructure `AfsDiagnosticClassifier` 只转接数据形状；有界分块重组、流复核和 warning 合并由同层收集器完成。
- `OutputQcRules.cs` 负责保留源 A/V 的已知元数据、新增字幕/音频规格、真实音轨 ID 及已声明 A/V 时长比较；输入为 `OutputQcFacts.cs` 的不可变事实。CheckFacts 返回 TrackQcReport，保留按原顺序通过的检查事实和失败种类/轨道类型/序号；Domain 不生成旧检查文案，文案仅由 Core 转换层生成。
- 字幕全文比较也由 `OutputQcRules` 负责，只归一 CRLF/LF；同工具 transport 参考生成、提取和文件读取仍在旧边界。Dialogue 文本、时序、Style、字体、事件顺序与末尾空格不能丢弃或放宽。
- `AssDocument.cs` 负责必要节、样式、字体/粗斜体覆盖、绘图忽略、Unicode 码点与 AFS mapping 解析，返回不可变使用/映射集合；动画字体切换、未知样式、非标准字重等原拒绝保持不变。UTF-8/BOM 文件读取、ttx 读取及字体候选/字形/固有粗体证明已随 3b 迁入 Infrastructure，纯规则仍由 Domain 维护。
- Domain 的解析拒绝使用无 IO 依赖的 `MediaRuleException`，旧边界转换为原 `InvalidDataException` 文案；不把文件、进程、时间、有副作用的 Environment 成员或具体工具类型带入 Domain。原 `diagnostics.md` 白名单未修改。

#### 阶段一单任务职责

3b 后，旧编排和兼容模型仍在 Core，进程与工具适配位于 Infrastructure。下表路径相对于 src/：

| 职责 | 文件 | 约束 |
|---|---|---|
| 旧请求、结果、状态、进度、探测模型与兼容转换 | Core/Muxing/MuxModels.cs、LegacyMuxContractConverter.cs | 旧入口转换模型，保持 Desktop 调用行为；3d/5 处理兼容入口 |
| 旧入口与唯一单任务用例 | Core/Muxing/SingleMuxService.cs、LegacyMuxContractConverter.cs；Application/RunSingleMuxUseCase.cs | 旧入口只接线和转换，ExternalMedia.cs 的流程辅助已删除；七阶段流程仅在 Application。检查文案、参考往返、哈希和提交后取消保持 |
| AFS 诊断与字体文件校验 | Infrastructure/Muxing/AfsDiagnosticClassifier.cs、AfsDiagnosticCollector.cs、AssGlyphValidator.cs | 分类调用 Domain DiagnosticPolicy；ASS 规则调用 Domain AssDocument；保留诊断计数、实时输出与字体来源证明 |
| 可替换进程边界 | Infrastructure/Muxing/IMuxToolRunner.cs；Infrastructure/ProcessRunner.cs、WindowsProcessJob.cs | 进程与 Job 整体搬迁；旧受控回归继续注入同一 runner，不更改测试逻辑；Start 到 Assign Job 的已知竞态仍未覆盖 |
| 导入识别与校验 | Application/PrepareImportUseCase.cs、Domain/InputRules.cs、Domain/SubtitleRules.cs、Domain/AssDocument.cs | 用例编排路径事实、只读探测、字幕读取、ASS 用法和计划；ASS 用法失败保留旧 `AssGlyphValidator.ReadUsage` 文案，失败路径暂存 `OperationFailure.Detail` |
| 探测、子集化、封装、提取、字体表 | Infrastructure/Muxing/MkvmergeProber.cs、AfsSubsetter.cs、MkvmergeMuxer.cs、MkvextractExtractor.cs、TtxFontInspector.cs、ToolExecutor.cs | 实现 Application 端口；列表参数、双流完整日志、UTF-8、超时和取消；失败统一 ToolFailureException，取消保留 OperationCanceledException |
| 工作区、日志与不覆盖发布 | Infrastructure/FileTaskWorkspace.cs、NoOverwritePublisher.cs | 独占 cache/run-id/work，异步释放等待清理；日志保留；同目录暂存、刷盘、最后取消检查与不覆盖 rename；提交后不取消已完成成品 |
| QC 判定 | Core/Muxing/SingleMuxService.cs 调用 Domain OutputQcRules；Infrastructure/Muxing/AssGlyphValidator.cs 保留字体 IO 核验 | 对比源视频与音轨，逐轨验证新增 ASS 名称/语言/default/forced；验证附件身份、大小、类型、提取 SHA-256 和 ASS 内容；不能仅按语言找到任意轨即通过。ASS 以同一 MKVToolNix 将每条修正 ASS 单独封装、提取为参考，与真实成品提取全文比较；仅统一 CRLF/LF，Dialogue 文本、时序、Style、字体、事件顺序和文本边缘空格均须一致 |
| 工具配置与定位 | Infrastructure/ToolCatalog.cs、ToolPathLookup.cs、ToolLocator.cs、StoragePaths.cs、Components/FontToolsLocator.cs | Infrastructure 不使用 Core；4a-2 已将 StoragePaths、ToolLocator 与组件边界迁入 Infrastructure；Workspace 仍是第 5 步前的 Core 过渡模型 |
| 集中装配与设置存储 | Desktop/Composition/AppComposition.cs；Infrastructure/StorageSettingsStore.cs；Desktop/Preferences/UserPreferencesStore.cs | Program 只调用装配；App 注入窗口工厂，无静态 Workspace / Storage；MainWindow、Environment、SingleMux ViewModel 注入服务依赖；存储索引只保存工作与工具目录，装配转换为旧 StoragePaths；界面偏好留 Desktop |
| 只读媒体探测 | Application/ProbeMediaUseCase.cs；Infrastructure/Muxing/MkvmergeProber.cs | 4a-1 给 `IMediaProber` 新增 `ProbeReadOnlyAsync`，暴露已有的只读重载；IProbeMedia 消费配置快照，探测命令与参数不变；旧 MediaProbe 由转换类完整投影 |

新增的多轨与自动导入范围以 [自动拖放任务](../tasks/automatic-media-drop.md) 为准：`PrepareImportUseCase` 统一编排输入事实、媒体探测、`MuxInputReader` 字幕读取、SRT 时间轴和 ASS 用法校验；ASS 子集化阶段仍由 `AssGlyphValidator` 负责字体 IO 核验。外加音频包含真实输入 Track ID，`ExternalMedia` 构建选择参数；编码、声道、采样率与元数据的 QC 调用 Domain `OutputQcRules`。字幕采用同工具往返全文参考，音频采用同工具参考的 `--fullraw`（含 CodecPrivate）SHA-256，任一不符阻止发布。只在 A/V 输入时长均已声明时检查总时长，不以字幕尾时间替代未知容器数据。

接口合同与第一阶段具体验收案例见 [阶段一任务](../tasks/phase-1-single-mux.md)。Core 已确认的探测类型为 `MediaTrack`、`MediaAttachment`、`MediaProbe`；公开的单任务签名按任务文档约定，真实文件和额外依赖以实现为准。


#### 3a 端口与统一轨道事实已定义

- `src/MuxDeck.Application/Ports/` 定义九个端口，接口均为 public；真实实现已随下方 3b 迁出，流水线接入留待 3c。端口数据为不可变 record，集合为 EquatableArray，不建立通用仓储、事件总线、中介者或万能执行接口。
- 原 Contracts `MediaTrackInfo` 归入 Domain，作为唯一的轨道事实类型，使用 TrackKind / LanguageTag / TrackId / TrackFlags。MediaProbeResult、ImportedTrackSpec 与 Domain MediaQcFacts 共用它；重复的 TrackQcFacts 已删除。保留 RawType、nullable IETF、HasDefaultFlag / HasForcedFlag，传统语言严格按原值比较，IETF 保留原大小写无关规则。
- Core LegacyMuxContractConverter 的 ToTrackInfo / ToProbeResult 只负责模型转换与不可变快照；旧 SingleMuxService 的 QC 边界调用同一转换后交给 OutputQcRules，不保留第二套轨道事实判定。旧工具参数、流水线、进度、失败文案与行为保持不变。
- 所有外部操作接受 CancellationToken；TaskWorkspace 通过 IAsyncDisposable 负责独占工作目录清理与日志生命周期，不向 Contracts 暴露工作区接口；IClock 只声明注入的 UTC 值，不读取当前时间。

| 端口 | 业务边界 |
|---|---|
| IMediaProber | 媒体探测，返回共用 Domain 轨道事实的 MediaProbeResult |
| IFontSubsetter | ASS 字体子集化，返回修正字幕与字体 WorkFile 及诊断 |
| IMuxer | 封装临时 WorkFile，返回工作文件与诊断 |
| IMediaExtractor | 按真实轨号/附件号提取，支持容器默认格式及 fullraw |
| IFontInspector | 读取指定字体 face 的名字、码点、粗斜体和字重事实 |
| IArtifactPublisher | 同目录暂存并不覆盖提交，冲突与竞态失败 |
| ITaskWorkspaceFactory | 创建任务独占工作区，异步释放后清理完成、保留日志 |
| IToolCatalog | 按业务能力定位工具，返回不可变描述 |
| IClock | 提供注入的 UTC 时间 |

#### 3b Infrastructure 适配已迁出

九个业务端口与 ITaskWorkspace 均有 Infrastructure 实现；Core → Infrastructure 为用户批准的过渡引用，第 5 步移除。Infrastructure 始终不引用 Core、Desktop、Avalonia。StoragePaths、ToolLocator、Workspace 及任务文档列出的 Desktop 直接依赖留 Core，4a 替换调用与装配。

tests/MuxDeck.Infrastructure.IntegrationTests 只直接引用 Infrastructure，固定 xUnit v3 3.2.2 / runner 3.1.4 / Test SDK 17.14.1，并提交 packages.lock.json。新增 45 项，使用测试程序集子进程与隔离文件验证六个工具适配的参数、UTF-8 双流、退出码与取消，另验证 JSON/XML 失败、AFS Fatal、超时、Job 后代进程、工具定位、工作区与不覆盖发布。CI 不要求本机媒体工具；真实媒体回归仍由 Smoke 的仅本地入口运行。Architecture 新增两项（直接引用、全部端口实现），原 554 项保留，共 601 项：Domain 298 / Application 27 / Core 37 / Architecture 194 / Infrastructure 45。受控入口与基准计数不变。

3b 适配修正后累计 613 项：Domain 298 / Application 29 / Core 40 / Architecture 194 / Infrastructure 52。原 601 全部保留，新增日志失败与 Fatal 保留 7、来源显示 3、新错误码构造白名单 2；超时与缺失的原用例同步精确码预期并增加完整 Message 断言。修正与 CI 见[3b 实施记录](../tasks/arch-layering.md#3b-修正适配修正)。

#### 阶段一 Desktop 单任务约定

最新自动入口由 Application `PrepareImportUseCase`、`ViewModels/MediaImport.cs` 和 `Views/SingleMuxView.MediaInput.cs` 协作承担：工作区文件格式优先，混合批次准备后一次提交；文件名仅在语言未声明时提供提示，路径拆分后调用 Domain `LanguageRules.InferImported`，不在 ViewModel 内保留推断实现。字幕/音频追加到共享 `externalOrder`，显示 ID 连续编号，GUID 只作内部身份；一份音频容器可以投影多条真实音轨。字幕/音频更换、移除与编辑守卫共同维护草稿版本。ASS 才要求字体目录和字体后端；SRT/音频可单独封装。`MuxRequest` 快照复制字幕与音频集合，开始/取消/关闭锁定不变，关闭等待还包含媒体识别进程。以下 SC/TC API 保留其旧草稿契约和回归，不再限制新拖放/添加入口。

最新输入反馈见[窗口输入反馈任务](../tasks/window-input-feedback.md)：`Views/SingleMuxView.DropFeedback.cs` 在窗口宿主统一处理拖入，包含左侧导航，只有单个封装可接纳；`MainWindow` 的透明遮罩覆盖整块客户区且退出命中测试。子控件 Leave/Enter 通过 70ms 合并，真正窗口外离开、Drop、Esc、切页及脱离清理；详情面板坐标遮挡、忙碌和所有可见 owned dialog 均拒绝。原生对话框的 enabled 状态不能只凭托管 `IsEnabled` 推断。

`ViewModels/InputFeedback.cs` 拥有明确类型的单条通知与临时拖入投影；`ReportInteractionMessage` 保留原文本合同，同时发布类型，不用文案推断成功。`Views/InteractionNotice` 在右上角显示当前反馈，属性栏打开时向左避让；成功 5 秒、普通 4 秒隐藏，忙碌保持到结束，警告/错误等关闭或替换。可见重复不重启计时，旧修订计时不清新消息，视图脱离解除订阅和计时。取消接纳不遗留忙碌；底部 Action Bar 仍只显示持续的输入/任务状态。

`ViewModels/MainWindowViewModel.cs` 保留四个页面状态（单个、批量、字幕工具、环境检测），并公开 `Single` 和 `Environment`；`MainWindow.axaml` 显示单个、批量、环境检测导航。字幕工具仍为未实现占位且没有可见导航入口。保留 `VideoPath`、`ScSubtitle`、`TcSubtitle`、`OutputDirectory` 转发属性。单个封装由 `Views/SingleMuxView.axaml` 承载，绑定根为 `SingleMuxViewModel`；旧 `VideoInput` 文本框已由视频摘要和 `PickVideoButton` 新入口替换，Smoke 用实际按钮键盘事件、接纳及 `VideoName` 编译绑定验证，不设置隐藏兼容控件。

环境规则位于 Infrastructure `Components/`：`ComponentModels` 定义外部实现的检测状态、安装阶段与能力；`ComponentVerifier` 复用 `ToolLocator` 和 `ProcessRunner`，验证启动、版本和必要依赖；`ComponentReleases` 维护固定版本、包布局、体积和哈希；`ComponentInstaller` 拥有四组件下载、校验、暂存验证、不覆盖提交、安装日志和取消清理，`AssfontsInstaller` 仅保留到第 5 步评估。`FontToolsLocator` 由检测和封装共用，优先管理目录内的独立后端，其次保留旧系统查找；安装暂存验证通过 `ComponentConfiguration.FontToolsDirectoryOverride` 强制检查候选依赖。Application `ManageComponentsUseCase` 持有唯一协调器租约；Desktop `EnvironmentViewModel` 只投影契约模型与同步目录查询结果，`EnvironmentComponentViewModel` 投影状态点、行内动作和详情。`StoragePaths` 与 `StorageSettingsStore` 负责路径布局和持久化；MainWindow 仅接线操作互斥、目录选择、官方链接与关闭等待。实际封装参数和 QC 仍由原单任务流程决定，当前范围见[补齐组件安装](../tasks/component-installation-complete.md)。

组件卸载以当前配置目录为管理边界：手动复制的组件也计为 Mux Deck 来源，不使用安装归属记录。Core `ComponentDirectories` 只生成固定直接子目录的计划并检查边界/链接，`ComponentUninstaller` 重新验证后逐项删除并记录日志，与安装器共用 Application `OperationCoordinator` 的租约。AFS 的随包 Python 随主目录删除，独立 `fonttools` 保留并单独管理；重装可复用已验证的独立后端。Desktop 保存待确认计划、卸载任务和错误；MainWindow 结合单任务、导入与真实工具探测忙碌态禁止删除，关闭等待收尾。完整边界与证据见[组件卸载任务](../tasks/component-uninstall.md)。

单任务交互由 `ViewModels/SingleMuxViewModel.cs` 持有；4b-2 后草稿（`MuxDraft`，经 Core 过渡门面 `SingleMuxDraft`，4b-3 删除）是唯一数据源，VM 构造时通过 `SingleMuxService.CreateDraft()` 获取独立实例，不再保存路径字段、修订计数或接纳校验副本。`StartAsync` 用 `draft.ToSpec(tools)` 调用 `SingleMuxService.RunSpecAsync`；VM 仍包含 `CancelAndWaitAsync`、`RunTask`、`IsRunning`、`CanEdit`、`Tracks`、`SelectedTrack`、`Log`、`Result` 与 `StatusText`。`TrackRowViewModel` 是投影：SC/TC 与外加行的对象身份保持不变，行编辑写回草稿，草稿变化只更新已有行；固定槽位行用结构化 `Slot` 标识，源行用 `SourceRetained` 表达保留策略。`MainWindowViewModel` 继续负责导航和工具环境摘要。UI 不承担 QC，也不构造 AFS 或 mkvmerge 参数。

第一轮任务 A 的状态和接纳接口仍位于同一 ViewModel 及 `ViewModels/InputAcceptance.cs`；4b-2 后校验、计划与修订由 Application `MuxDraft` 持有，Desktop 只负责同步收集、投影与中文文案映射（`DraftText` 按入口/操作/错误码/原因集中映射），不解析 `Detail`：

- 摘要由 `VideoFileName`、`VideoFullPath`、`VideoDimensions`、`VideoCodec`、`VideoDurationNanoseconds`、`MediaSummary` 与 `ProbeState`/`ProbeText` 表达；未知字段为空，不猜容器。`SourceTracks` 和 `ExternalTracks` 是只读可观察分组；`SourceAttachmentCount`/`SourceAttachments` 用于附件数量和详情。旧 `Tracks` 仍含源轨、附件与外加字幕，以兼容当前视图。
- 固定 `ScTrack`/`TcTrack` 的 `IsIncluded` 独立于路径、名称及旗标。禁用仍可查看；`SetSubtitleIncluded`、`RemoveSubtitle` 按槽位操作，提交只消费启用 ASS。同槽替换和启用切换保留名称/default/forced；添加或移除改变槽位组合时沿用 SC/TC 默认规则。两个槽位只订阅一次元数据变化，并在运行、取消清理和关闭等待时用模型守卫拒绝路径、元数据及启用变更。
- 视图从系统文件数据构造 `InputCandidate`（本地文件、本地文件夹；URL/文本标为 `NonLocal`），先调用 `PrepareVideoInput`、`PrepareSubtitleInputs`、`PrepareFontsDirectory` 或 `PrepareOutputDirectory`。字幕分配用 `SubtitleAssignment(SubtitleSlot, InputCandidate)` 明确 SC/TC。准备阶段只校验并返回只读 `InputAcceptancePlan`（内部绑定草稿计划），不修改配置。视频或已有字幕槽位的异路径替换通过 `RequiresReplacementConfirmation` 要求视图明示确认；取消直接丢弃计划。
- 确认后 `CommitInput(plan, replacementsConfirmed)` 再核验 `CanEdit`、草稿修订，由草稿重新收集候选与当前路径的固定槽位事实并跑同一校验核心；只接纳完整 Windows 路径，按裁剪后的规范化绝对路径和大小写比较，不识别硬链接/符号链接身份。批量重复按操作完成后的 SC/TC 集合比较，整组交换合法，同槽同路径无害。结果的 `Error`/`Message` 给出拒绝原因；失败仅更新 `InteractionMessage`，草稿和 Core 结果均保留。选择器、复制或平台动作异常可用 `ReportInteractionMessage` 表达。
- `DraftRevision` 由草稿持有：对参数、启用和外加元数据变化计数；一次接纳批次增加一次，同值赋值、导航、选择和展示不增加。`ResultRevision`、`IsResultHistorical`、`ResultLabel` 区分当前草稿与上次 Core 结果；配置变化保留 `Result`、诊断和日志，再次运行期间也保留上次结果至新结果返回。主区使用 `State`/`StateSummary`，进度使用 `ProgressStage`/`ProgressMessage` 与既有不定进度；详细 `Checks`/`Warnings`/原始 `Error` 从 `Result` 读取。`CanStart` 只表示提交条件齐全，最终验证仍归 Core。

单个封装最新结构见 [工作台重构任务](../tasks/single-mux-workbench.md)；三张旧稿保留为历史布局参考：

- `MainWindow` 保留 172 宽左侧栏与底部工作区路径条，给单任务视图提供整块客户区宿主；`SingleMuxView` 持有封装/字体附件/可选开发日志页签与全高轨道属性栏，不再接收重复页面标题。封装页主区自上而下为视频摘要（约 72 高，两行投影）、轨道与内容（约 312，清单内部滚动）、输出设置（约 143，字体/输出目录并排、字体辅助说明及输出文件名）。主区不整体滚动：默认列宽下普通 3 源轨 + SC/TC 在 1040×760 下无需滚动，更多轨道只在清单内部滚动。固定状态区（约 76 高）不随内容滚动；底部结果面板止于其上缘，全高轨道属性栏会覆盖其右侧操作，关闭栏后恢复可达。
- 主表列为“无文字纳入列、ID、类型、语言、名称、默认、强制、编码”，共享 `TrackRowTemplate` 和 `TrackColumnLayout`，`TrackColumnsGrid` 为每个实例投影独立列定义；表头拖动/键盘调整由 `TrackColumnHeader` 承担，表格内部允许水平滚动并同步表头，源/外加以分组标题区分，删除逐行详情/更换/移除按钮，整行主要区域是行选择按钮。首列包含状态由 `TrackRowViewModel.InclusionBox` 调用 Domain `TrackPolicy.IsIncluded` 生成展示投影，源保留策略调用 `TrackPolicy.RetainsSource`：源视频/音频勾选只读、源字幕不勾选只读、外加字幕可切换，不直接绑定默认为 true 的 `IsIncluded`；只读源行仍可选中。行选择按钮的可访问名称由 `TrackRowViewModel.AccessibleName` 明确给出，不能退化为控件类型文本。
- 两个 Overlay 覆盖主内容：右侧轨道属性栏（300 宽）按最新用户红框贴窗口客户区右边，纵向从客户区顶部到底部，无外侧留白与圆角；底部结果详情（300 高）仍止于状态栏上缘。二者暂互斥，打开一个关闭另一个；切换轨道原位更新；`×` 与 Esc 关闭、点击空白不关闭。被遮区域同时退出指针命中测试与 Tab 导航：轨道面板打开时视频摘要区与设置区整体退出，结果面板打开时设置区与被完全盖住的行退出，可见轨道清单保持可用；面板内容溢出时仅面板内部滚动。轨道属性栏只有封装页签选中时可见，切页/页签隐藏但保留模型状态；标题与关闭入口、外加更换/移除操作固定，中部滚动。
- Overlay 键盘规则可自动验证：窗口级 Tab/Shift+Tab 在未被遮挡的可用控件间移动，包含活动面板自身的操作与可见侧栏，不进入被遮的添加/附件/设置操作或全高属性栏下的状态按钮；窗口键盘接线在视图脱离时解除，隐藏封装页或其他页签使用默认导航。关闭后按当前视觉树解析触发入口/当前选中行并落到有效可见焦点（行随清单重建后不沿用旧控件，入口被移除时回退到可用控件）。轨道清单获焦时普通 ↑/↓ 可跨视口滚入下一行并原位更新已打开的面板，Enter/Space 打开对应属性；结果面板打开时仅增加清单内部末尾滚动余量，使末行可滚到露出的区域，不改变主布局。方向导航不改草稿或包含状态，文本编辑与下拉内保持控件自身语义。
- 未选外加字幕替换/移除导致清单重建时，所选源行按来源、类型和轨道标识恢复到当前对象，Inspector 保持源视频路径；更换视频先清空旧选择，不按相同数字标识继承另一视频的轨道。源选择恢复只是展示更新，不增加草稿修订。
- 轨道属性按图布局包含、名称（可编辑）、固定只读语言、默认/强制、来源与媒体信息及完整文件路径；外加轨道显示更换/移除，复用现有确认、接纳、修订与编辑锁，图中的按钮不是新的业务入口。源轨属性按真实能力只读，无真实来源的字段（如帧率）省略而不补示意值。输入路径复制快捷入口已移除：视频菜单仅保留重新探测，字体/输出目录提供选择和打开/清空菜单，属性栏显示完整路径；只读输入框仍支持文本选取与系统复制。目录菜单提供打开和清空，不恢复复制入口。
- 结果详情集中当前结果概览、`Result.Checks` 逐项、`Warnings`、原始 `Error` 与复制/日志/成品操作。`Checks` 仍是文本集合，逐项分区展示，不解析中文或日志关键词拼出不存在的结构化成功证据。成品计数由成功 `Result` 的输出快照经只读 `ProbeAsync` 补齐（`OutputMediaCounts`/`OutputAttachmentSummary`）：绑定成功结果、迟到或被取代的结果不回写，探测失败只影响概览而不改动已成功的 Core 结果，无来源时显示“成品概览尚未取得”。主状态区只给简明摘要和详情入口，不把长原始错误塞进页脚。
- 添加入口为“＋ 添加轨道”菜单：ASS/SRT 字幕与音频均可用，经过自动识别并追加；不再要求用户分配 SC/TC 槽位或限制两条。源附件为轻量行“源附件（数量） / 本次不保留”，零附件时省略；查看入口用对话框给出真实附件清单。
- 工作区使用 Avalonia 12 的 `IDataTransfer`/`DataFormat.File` 与 `IStorageItem.TryGetLocalPath`，有文件格式时忽略伴随文本/URL，纯文本/URL 不转成本地候选；新拖入和添加选择器共用媒体 Prepare→必要的视频替换确认→Commit 流程。`SubtitleAssignmentWindow` 仅保留供旧槽位接口使用，新的多轨入口不显示分配框。`Views/SingleMuxInteractions.cs` 负责系统选择器、视频/轨道替换确认、旧槽位分配、剪贴板和精确路径打开；`ISingleMuxInteractions` 是原生交互的具体测试边界，测试替身不能证明原生选择器、Explorer 拖入或系统剪贴板通过。`MainWindowViewModel` 可注入 `SingleMuxService`，仅用于受控 Core runner 装配。
- `ShowDeveloperLog` 由 Debug 默认开启、Release 默认关闭，可用本次进程环境变量 `MUX_DECK_DEV_LOG=1` 开启，不写持久配置。Release 仍保留原始错误、全部 Checks/Warnings、复制诊断与成品/日志定位，成功附提示在结果详情可发现。主区使用真实 `StateSummary`，历史结果用 `ResultLabel`/`ResultHeaderNote` 标明；原始错误只在详情，不解析日志猜成败。取消清理时命令禁用重复取消，输入继续锁定。
- `DisplayDuration`/`DisplayMediaSummary`/`VideoSummaryLine` 是探测数据的展示投影，不改变 `MediaSummary` 或 Core 模型。同路径 `ProbeVideoAsync` 开始时撤下旧源数据，失败时保持源摘要/轨道/附件为空，外加字幕及设置保留；迟到保护和关闭等待沿用既有实现。
- 验证入口 `--single-mux-round1` 保留任务 A 行为检查；`--single-mux-ui-round1` 使用新布局真实控件与模拟 DragDrop，核验面板开关/互斥/焦点/几何、真实可访问名称与两尺寸多状态截图；`--ui-integration` 使用当前新窗口与自动添加轨道入口，串行执行单 ASS、双 ASS、禁用首条字幕的真实 Core 小 fixture，核验产物、成品概览、冲突重试与输入哈希。报告/截图/隔离素材位于 `artifacts/single-mux-round1-b/<run-id>/`。多轨识别与真实 SRT/音频封装另由 `--media-drop` / `--media-drop-real` 验证，证据见[验证要求](verification.md#自动拖放与多轨封装2026-10-10)。headless 检查不替代原生桌面、DPI 与播放器验收。

外加列表按首次添加顺序展示，`DisplayId` 为从 0 连续编号的展示投影；固定槽位行用结构化 `Slot` 标识，不再按 `Id` 文本判断，内部 `Id` 仍为 SC/TC 以保持对象身份；替换不改顺序，移除后重排，再添加位于末尾。空名称不填默认占位。`LanguageDisplay` 只转换展示名称，源行保留传统/BCP 47 两种原始代码与编码详情；传入 Core 的语言和旗标不变。Action Bar 只统计已知缺项，“已准备好”表示基本输入齐全，不代表预检完成。

`Preferences/UserPreferencesStore.cs` 是 v2 界面偏好文件边界，生产 `App` 注入用户 LocalApplicationData/MuxDeck/v2/preferences.json；测试注入 artifacts 位置。`TrackColumnLayout` 独立于草稿，版本化加载、范围限制、损坏回退，异步串行原子保存；关闭窗口等最后一次保存，保存失败只影响偏好，不改变任务结果或阻止已安全停止的窗口关闭。

上述为职责落位规则，按实际任务创建文件和子目录；不预建空模块或按每个动作拆分项目。

### 3c 单任务用例

`RunSingleMuxUseCase` 只持有端口，按检查输入、探测源、子集化、计划、临时封装、校验、不覆盖落位七阶段传递不可变 record。工具与目录从 `MuxJobSpec.Tools` 取得；4a-1 已删除旧 `UpdateWorkspace` 外壳，ViewModel 向每次运行/探测传入配置快照，旧服务只持有不可变的初始配置和装配依赖。

文件事实由业务端口 `IMuxInputReader` 和 `IMuxEvidenceReader` 提供，分别实现为 Infrastructure 的 `MuxInputReader`、`MuxEvidenceReader`；同工具参考往返通过 `IMediaReferenceBuilder` / `MediaReferenceBuilder`，工具启动前验证通过 `IToolReadiness` / `ToolReadiness`。不建立通用文件系统接口，Application 仅使用允许的 Path 字符串成员。

QC 返回 `TrackQcReport` 的结构化事实，经 `QC_SUBSET_FONTS`、`QC_TRACK_FACTS`、`QC_ATTACHMENTS`、`QC_MEDIA_READABLE`、`QC_AUDIO_PAYLOAD` 生成 `CheckResult`。旧 `OutputQcRules.CheckTracks` 是兼容值转换，共享唯一 `CheckFacts` 规则；旧文本由 `LegacyTrackQcText` 转换。用例不保存旧检查文案。

失败保留实际阶段；端口码原样传递。用例自身的集中码见 `ErrorCodes`：输入/视频/字体目录/输出路径/字幕与音频元数据、源轨缺失、封装计划、轨道 QC、附件数量/身份/数据、字幕参考/全文、音频 payload、源选择与未预期异常。取消返回 Cancelled；工作区所有终态都等待异步释放。只有发布端口成功才产生最终 `OutputArtifact`，提交成功后忽略取消。

### 3d 旧入口转换

`SingleMuxService` 仅固定请求/规格快照、配置快照并调用用例；旧进度、检查顺序、错误文案与告警由 `LegacyMuxContractConverter` 转换。4b-2 新增 `RunSpecAsync(MuxJobSpec, …)`，ViewModel 直接提交草稿规格；`INPUT_VALIDATED` 时仍用 `ToLegacyRequest` 生成旧请求快照写 `请求 …` 行，沿用原 JsonSerializer 默认格式及规范路径/输出名，保证与旧入口逐字一致。静态 `DefaultCollectFacts` / `DefaultPathFacts` 已删除，端口由装配显式注入。用例自己的 `SPEC` 日志保持原样，日志工作区不反向解析规格，EquatableArray 不增加 JSON 反序列化支持。

3d 历史累计测试 676：Domain 298 / Application 80 / Core 41 / Architecture 198 / Infrastructure 59；3c 的 675 项全部保留，新增旧入口 task.log 的请求行逐字、时序与集合隔离检查。该步 Desktop 和 Smoke 不改；UpdateWorkspace 后于 4a-1 删除。

## 单任务接口

单任务入口封装子集化、封装、校验与成品落位的整条流程。界面和批量共用这一入口，参数组装与业务规则只有一份实现。目标入口为 `IRunSingleMux.RunAsync(MuxJobSpec, IProgress<JobProgress>?, CancellationToken)`，返回 `MuxJobResult`；迁移完成前保留 `SingleMuxService.RunAsync(MuxRequest, …)` 兼容入口，4b-2 起 ViewModel 已改用 `RunSpecAsync(MuxJobSpec, …)`。

- 输入是本次任务的不可变快照，包含所有影响结果的路径和选项。工具与工作目录配置随任务快照传入，运行中的任务不受后续配置修改影响，不在服务上保留可变配置（迁移前为 `SingleMuxService.UpdateWorkspace`）。
- 输出包含明确的成功、失败或取消状态，成品位置、结构化检查结果与可定位的失败原因（见[契约](#契约)）。
- 进度包含明确阶段；工具适配模块可解析工具自己的进度，流程模块负责阶段转换。
- 任务拥有独立临时目录、进程生命周期和结果；可通过取消令牌停止执行。
- 需要替换外部调用或做失败注入时，按上文[端口](#端口)规则在具体依赖处建立小接口。

## 工具与文件操作

业务流程由 C# 实现，复用 MKVToolNix、AssFontSubset 等外部可执行工具。工具定位返回真实可用信息，路径存在、可执行性与后端依赖分别核验。

进程参数通过参数列表传递；同时读取 stdout/stderr；编码按工具约定处理。接入长任务时使用文件日志和有界界面缓存，避免无限累积输出。取消应终止并等待已纳入生命周期管理的相关进程退出，再清理本次资源；当前 Windows Job 归属不是 `Process.Start()` 原子操作的一部分，限制见 [开发流程](migration.md)。

新版配置、日志和历史使用专属目录或命名空间。业务相关设置（存储路径、工具目录，以后的预设与队列）的路径布局与读写归 Infrastructure；界面偏好（列宽等）只属于 Desktop，其存储留在 Desktop `Preferences/`，位置见上文。持久化文件带 schema 版本，写入互斥、原子替换、损坏回退。v1 配置与历史作为只读参考；后续导入采用明确转换并保留原文件。共享第三方工具目录可以只读复用。

## UI 与状态

- 视觉资源与通用尺寸归 `Styles/Resources.axaml`，共享控件样式归 `Styles/Controls.axaml`；`App.axaml` 只装配。页面专属轨道、拖入与 Overlay 样式留在 Views。颜色、字号与控件规则见 [浅色视觉规范](../design/visual-language.md)，不在 ViewModel 中添加视觉资源。
- 使用 MVVM 和编译绑定；每个绑定根与数据模板声明数据类型。
- ViewModel 只依赖契约与用例接口，经构造函数注入，不自行创建默认依赖；异步展示进度和结果，耗时处理不阻塞 UI 线程。
- 草稿规则由 `MuxDraft` 与 Domain 承担；ViewModel 只保存界面状态（选中、面板、列宽、焦点、通知）和展示投影。
- 单个封装 ViewModel 按页面区域拆分（视频摘要、轨道清单、轨道属性、输出设置、运行与结果详情），页面 ViewModel 只做组合。
- 单个、批量和后续字幕工具各自拥有状态；切页保留输入，运行任务使用提交时快照。
- 表单参数、进度阶段与完成结果分开表示；以核心结果决定成功，日志用于诊断。
- 跨页面忙碌与互斥只读 `IOperationCoordinator` 的状态，窗口关闭只等待协调器，不在视图代码中组合各页面的忙碌标志。
- 未实现的入口显示真实完成状态。原生窗口保持键盘操作、焦点反馈和内容滚动能力。

## 扩展与规则更新

后续预设、配置和队列等模块按上文分层加入；模块对外暴露所需用例与契约，内部可变状态由自身维护。新增生产项目、拆分 Contracts 或改变依赖方向须先新增或修订 ADR。

改变项目依赖、契约、单任务接口或状态归属时，在同一改动中更新本文、架构测试与对应行为验证。SDK 与包版本直接查项目配置；更改依赖时一起维护锁文件。


3c 小修已删除仅由测试使用的 CheckTracks、Domain 的 LegacyTrackQcText 和无人调用的 TrackQcFailed 旧码。所有相关测试改为 CheckFacts 的种类、轨道类型、序号、数量和完整轨道字段断言，正式用例/转换路径保持；已移除旧码新增拒绝验证，原有效码案例迁移为拒绝案例，总数保持 717。


### 3e 操作协调与组件用例

`OperationCoordinator` 实现 `IOperationCoordinator`，不涉及 IO。兼容矩阵以[3e 检索记录](../tasks/arch-layering.md#现有操作互斥矩阵3e-检索结论)为依据：安装与卸载同类/异类互斥，其他 Core 操作允许并行。租约释放幂等；`ShutdownAsync` 关闭接纳并等待所有租约，取消仅取消等待。`StateChanged` 按变更顺序报告不可变快照，订阅者异常不泄漏租约。Desktop 原有方向性的忙碌态、检测重启与关闭取消接线保持原样，随第 4 步迁移，不把 UI 条件扩成新的全局锁。

`ManageComponentsUseCase` 实现 `IManageComponents`，检测、卸载计划检查使用检测租约，安装/卸载使用互斥租约；成功、失败、异常、取消均释放。每次调用只使用 `ComponentConfiguration` 快照；拒绝仍为“已有组件安装或卸载正在进行。”。集中错误码新增 `MUX_COMPONENT_OPERATION_BUSY`、`MUX_COMPONENT_INSTALL_FAILED`、`MUX_COMPONENT_UNINSTALL_FAILED`。组件检查增加按 ID 检测入口；`ComponentPart.Purpose/Source` 改为枚举，Desktop 映射回原中文用途与来源文字，保证显示不变。目录同步查询只提供显示候选，确认卸载仍走 `PrepareUninstallAsync` 并在执行时复核边界。

4a-2 已删除 Core `ComponentManagement`、`ComponentOperations` 静态桥接以及旧 `*CoreAsync` 回绕和 `legacyExceptions` 开关。组件验证、安装、卸载、目录边界、发布版本、StoragePaths、ToolLocator 均位于 Infrastructure；Application 只通过 `IManageComponents` 与契约模型接入。`AssfontsInstaller` 仍保留到第 5 步评估。Desktop 架构测试要求只有 `MuxDeck.Desktop.Composition` 可引用 Infrastructure；Smoke 替身迁移到 Infrastructure 边界，断言、文案和状态序列保持不变。

3e 新增协调器矩阵及生命周期 35 项、组件用例 17 项、过渡转换与实际卸载边界 5 项，集中错误码自动增加 3 项注册码用例；原 717 项全部保留，总计 777（Domain 298 / Application 158 / Core 59 / 架构 198 / Infrastructure 64）。现有测试和护栏未改动。
