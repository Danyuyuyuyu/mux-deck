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

- Domain 不引用任何项目或第三方包。
- Application 只引用 Domain，不引用 Avalonia、Infrastructure 或具体工具。
- Infrastructure 引用 Application（实现其端口）与 Domain，不引用 Desktop。
- Desktop 引用 Application 与 Infrastructure；`ViewModels/` 与 `Views/` 只使用 `MuxDeck.Contracts` 命名空间、Domain 值类型和用例接口，不构造 Infrastructure 类型。只有 `Program.cs` 与 `Composition/` 可使用 Infrastructure 命名空间。
- 测试项目只引用其验证的生产项目。上述规则由架构测试固定，违反即测试失败。

1a 已建立 Domain、Application、Infrastructure 三个生产项目；1b 已在 Domain/Application 定义值类型与契约，Infrastructure 仍为空，旧流程与调用方仍位于 Core 与 Desktop，按[架构分层重构](../tasks/arch-layering.md)逐步搬迁。过渡期的实际项目引用如下（目标依赖仍以上述规则为准）：

| 项目 | 1a 直接项目引用 |
|---|---|
| Domain | 无 |
| Application | Domain |
| Infrastructure | Application、Domain |
| Core | Domain、Application |
| Desktop | Core、Application |

Desktop → Core 保留到既有入口迁移完成；Desktop → Infrastructure 在集中装配接入时建立。上述过渡引用不豁免目标命名空间规则：Core 内先改为 `MuxDeck.Domain`、`MuxDeck.Contracts`、`MuxDeck.Application`、`MuxDeck.Infrastructure` 的类型与对应新项目一起接受架构测试，搬迁时只移动文件。每完成一步，在同一改动中更新本节与下方“迁移前现状”。

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

Domain.Tests 只直接引用 Domain，Application.Tests 只直接引用 Application；Architecture.Tests 验证全部生产项目（过渡期包含 Core），Smoke 仍只直接引用 Desktop。1b 新增 Domain 值类型测试 8 项、Core 旧模型转换测试 26 项；Application 流程尚未迁移，当前流程用例 0。Core.Tests 只直接引用 Core，不放宽既有分层测试依赖。架构测试检查 restore 后的实际直接项目引用、Domain 包依赖、程序集与命名空间依赖、Views/ViewModels 的契约与接口边界，以及 Infrastructure 只能由 Program/Composition 使用的装配边界（含源码路径）。字段、泛型、构造调用和异步方法反例验证 NetArchTest 的扫描能力。

## 契约

`MuxDeck.Contracts` 是 Desktop（及以后其他入口）与 Application 的交接格式：任务规格、进度、结果、诊断和用例接口。

- 命名空间固定为 `MuxDeck.Contracts`，文件位于 `src/MuxDeck.Application/Contracts/`。出现第二个调用方时拆为独立项目，命名空间不变。
- 只依赖 `System.*` 与 `MuxDeck.Domain`。`TrackKind`、`LanguageTag`、`TrackFlags` 等值类型归 Domain，由契约引用。
- 只含不可变 record、枚举和用例接口；不含读写文件、调用服务或依赖当前时间的逻辑。
- 类型全部 `public`，不以 `InternalsVisibleTo` 访问内部成员，不暴露 Application 内部类型（如任务临时目录）。
- 结果结构化：成败用枚举表达；检查项为 `CheckResult(Code, Status, Subject, Expected, Actual)`；失败为 `JobFailure(Code, Stage, Message, Detail)`。界面不解析文案或日志判断成败。
- 工具诊断码以[工具诊断分类](diagnostics.md)为唯一来源；任务错误码集中定义在 `Contracts/ErrorCodes.cs`。
- 任务阶段 `JobStage` 只含单任务阶段；组件检测、安装、卸载使用组件契约自己的阶段。
- 改变契约属于接口变更，在同一改动中更新调用方、本节与契约相关测试。

目标契约与迁移前对应：

| 契约 | 作用 | 迁移前 |
|---|---|---|
| `MuxJobSpec`、`ExternalTrackSpec`、`SourceTrackSelection`、`FontSpec`、`OutputSpec` | 单任务不可变规格 | `MuxRequest`、`SubtitleInput`、`AudioInput` |
| `JobProgress(Stage, StepCode, Percent, Message)` | 阶段进度 | `MuxProgress` |
| `MuxJobResult`、`CheckResult`、`JobFailure`、`Diagnostic`、`OutputArtifact`、`LogRef` | 结构化结果 | `MuxResult`（`Checks` 文本、`Error` 字符串）、`MuxWarning` |
| `MediaProbeResult` 及轨道、附件类型 | 只读探测结果 | `MediaProbe`、`MediaTrack`（`Type` 为字符串）、`MediaAttachment` |
| `InputItem`、`ImportPlan` | 导入准备与提交 | `InputCandidate`、`InputAcceptancePlan`、`MediaImportPlan` |
| `IRunSingleMux`、`IProbeMedia`、`IPrepareImport`、`IManageComponents` | 用例接口 | 直接调用 `SingleMuxService`、`ComponentInstaller` 等 |
| `OperationKind`、`IOperationLease` | 跨功能占用与互斥 | `ComponentOperations.Gate` 与 `MainWindowViewModel` 联动 |

### 1b 契约与旧模型过渡

- Domain 已定义 `TrackKind`、`LanguageTag`、`TrackFlags`、`TrackId`。语言值保留原字符串（含空值和传统/IETF 标签），1b 不引入规范化或匹配规则。
- `Contracts/` 已定义单任务规格/结果/进度、媒体探测、导入计划、组件操作及协调器接口。集合统一使用 `ImmutableArray<T>`，仅保存数据；导入会话由调用方提供 ID 与修订号，不暴露 ViewModel 或 Application 内部对象。组件阶段独立于 `JobStage`，协调器目前只有契约，没有实现。
- `MuxJobSpec.Tools` 显式携带工具、日志、缓存及独立 fonttools 后端配置；Core 的 `Muxing/LegacyMuxContractConverter.cs` 提供 `ToSpec(MuxRequest, Workspace/ToolConfiguration)`、`ToLegacyRequest(MuxJobSpec)` 与 `ToResult(MuxResult)`。旧入口与调用方尚未接入这些转换，保持原行为。
- 旧请求的字幕与音频各自保留顺序，新规格先列字幕、再列音频；空输出名与 null 分别原样往返。旧模型无法表达的源轨选择、跨类型交错顺序、字幕来源轨号或无来源轨号的音频，转换回旧请求时明确拒绝，不丢弃字段。
- 旧检查文本使用唯一过渡码 `ErrorCodes.LegacyCheck`（`MUX_LEGACY_CHECK`），原文完整保存在 `CheckResult.Actual`，状态为 `NotEvaluated`，不根据文案判定成败；旧失败码为 `MUX_LEGACY_FAILURE`，取消码为 `MUX_CANCELLED`。旧结果未记录失败阶段，暂为 `JobStage.Unknown`。上述过渡检查/失败映射在第 3 步替换为真实结构化信息。
- 旧 warnings 的 code、message、出现次数及顺序原样转换；成品、日志路径及 nullable 失败消息保留。工具诊断白名单仍只由 `diagnostics.md` 维护。契约的 public、归属、依赖、不可变数据与无友元程序集规则由架构测试检查；依赖与可见性两条护栏已通过本地故意违反确认。

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
- 现有 `IMuxToolRunner` 是进程边界，迁移后成为 Infrastructure 内部依赖。`--warning-policy` 等依赖它的回归改为替换工具端口后再收紧其可见性。

### 单任务流水线

```text
检查输入 → 探测源 → 子集化 → 生成封装计划 → 封装临时成品 → 成品校验 → 不覆盖落位
 Domain   IMediaProber IFontSubsetter Domain   IMuxer     IOutputVerifier IArtifactPublisher
```

每阶段输入输出为 record，阶段转换与进度由用例负责，工具适配只解析工具自身进度。临时目录、任务日志与清理由 `TaskWorkspace`（`IAsyncDisposable`）负责，成功、失败、取消与异常路径都经过同一清理。

### 迁移前现状

以下两节记录迁移前的文件落位与 Desktop 行为约定。行为约定（接纳、草稿修订、Overlay、键盘、结果展示等）在迁移后继续有效；文件归属随[架构分层重构](migration.md#架构分层重构)改写，搬迁哪一部分就在同一改动中更新对应条目。

#### 阶段一单任务职责

阶段一的业务编排与媒体工具代码位于 `src/MuxDeck.Core/Muxing/`。下表路径相对于 `src/MuxDeck.Core/`；进程技术适配位于 Core 根目录，不拆出额外生产项目：

| 职责 | 相对 Core 的文件 | 约束 |
|---|---|---|
| 旧请求、结果、状态、进度、轨道探测模型；1b 兼容转换 | `Muxing/MuxModels.cs`、`Muxing/LegacyMuxContractConverter.cs`（新增） | 以不可变业务数据表达；字幕输入保留路径、名字、语言和 default/forced 选择 |
| 单任务整流程、AFS/mkvmerge/mkvextract 参数与 JSON 探测 | `Muxing/SingleMuxService.cs`、`Muxing/ExternalMedia.cs` | 一个 `RunAsync` 入口拥有任务日志/临时目录、进度、取消与清理；固定 PyFontTools；按检查、探测、子集化、封装、QC、不覆盖落位排序 |
| AFS 诊断分类与结果 warning | `Muxing/AfsDiagnosticClassifier.cs`、`Muxing/MuxModels.cs`、`Muxing/SingleMuxService.cs` | 分类白名单、未知诊断与可继续告警行为以[工具诊断分类](diagnostics.md)为准；流程返回结构化 warning |
| ASS 字形使用解析及子集字体校验 | `Muxing/AssGlyphValidator.cs` | 读取 UTF-8 ASS 样式与覆盖字体，按对白统计实际码点，再与 ttx 导出的字体 `name`/`cmap` 表核对 |
| 可替换的外部进程边界 | `Muxing/IMuxToolRunner.cs` | `IMuxToolRunner` 只包住外部进程执行；默认 `MuxToolRunner` 复用 `ProcessRunner`，测试注入保持在这个具体边界 |
| QC 判定 | `Muxing/SingleMuxService.cs` 私有探测/QC 流程和 `Muxing/AssGlyphValidator.cs` | 对比源视频与音轨，逐轨验证新增 ASS 名称/语言/default/forced；验证附件身份、大小、类型、提取 SHA-256 和 ASS 内容；不能仅按语言找到任意轨即通过。ASS 以同一 MKVToolNix 将每条修正 ASS 单独封装、提取为参考，与真实成品提取全文比较；仅统一 CRLF/LF，Dialogue 文本、时序、Style、字体、事件顺序和文本边缘空格均须一致 |
| 文件、进程与日志支持 | `ProcessRunner.cs`、`WindowsProcessJob.cs` 与 `Muxing/SingleMuxService.cs` | 运行级临时目录和完整任务日志；已归属 Job 的工具进程用取消/父退出收尾；`Process.Start()` 到 Assign Job 存在未覆盖竞态，不能承诺任意 launcher 的后代全终止；目标用同目录临时文件及不覆盖 rename 落位 |

新增的多轨与自动导入范围以 [自动拖放任务](../tasks/automatic-media-drop.md) 为准：`SubtitleText` 统一 ASS/SRT 的 UTF-8 与结构校验，ASS 继续使用 `AssGlyphValidator`；外加音频包含真实输入 Track ID，`ExternalMedia` 构建选择参数并核验编码、声道、采样率与元数据。字幕采用同工具往返全文参考，音频采用同工具参考的 `--fullraw`（含 CodecPrivate）SHA-256，任一不符阻止发布。只在 A/V 输入时长均已声明时检查总时长，不以字幕尾时间替代未知容器数据。

接口合同与第一阶段具体验收案例见 [阶段一任务](../tasks/phase-1-single-mux.md)。Core 已确认的探测类型为 `MediaTrack`、`MediaAttachment`、`MediaProbe`；公开的单任务签名按任务文档约定，真实文件和额外依赖以实现为准。

#### 阶段一 Desktop 单任务约定

最新自动入口由 `ViewModels/MediaImport.cs` 和 `Views/SingleMuxView.MediaInput.cs` 承担：工作区文件格式优先，混合批次准备后一次提交；文件名仅在语言未声明时提供提示。字幕/音频追加到共享 `externalOrder`，显示 ID 连续编号，GUID 只作内部身份；一份音频容器可以投影多条真实音轨。字幕/音频更换、移除与编辑守卫共同维护草稿版本。ASS 才要求字体目录和字体后端；SRT/音频可单独封装。`MuxRequest` 快照复制字幕与音频集合，开始/取消/关闭锁定不变，关闭等待还包含媒体识别进程。以下 SC/TC API 保留其旧草稿契约和回归，不再限制新拖放/添加入口。

最新输入反馈见[窗口输入反馈任务](../tasks/window-input-feedback.md)：`Views/SingleMuxView.DropFeedback.cs` 在窗口宿主统一处理拖入，包含左侧导航，只有单个封装可接纳；`MainWindow` 的透明遮罩覆盖整块客户区且退出命中测试。子控件 Leave/Enter 通过 70ms 合并，真正窗口外离开、Drop、Esc、切页及脱离清理；详情面板坐标遮挡、忙碌和所有可见 owned dialog 均拒绝。原生对话框的 enabled 状态不能只凭托管 `IsEnabled` 推断。

`ViewModels/InputFeedback.cs` 拥有明确类型的单条通知与临时拖入投影；`ReportInteractionMessage` 保留原文本合同，同时发布类型，不用文案推断成功。`Views/InteractionNotice` 在右上角显示当前反馈，属性栏打开时向左避让；成功 5 秒、普通 4 秒隐藏，忙碌保持到结束，警告/错误等关闭或替换。可见重复不重启计时，旧修订计时不清新消息，视图脱离解除订阅和计时。取消接纳不遗留忙碌；底部 Action Bar 仍只显示持续的输入/任务状态。

`ViewModels/MainWindowViewModel.cs` 保留四个页面状态（单个、批量、字幕工具、环境检测），并公开 `Single` 和 `Environment`；`MainWindow.axaml` 显示单个、批量、环境检测导航。字幕工具仍为未实现占位且没有可见导航入口。保留 `VideoPath`、`ScSubtitle`、`TcSubtitle`、`OutputDirectory` 转发属性。单个封装由 `Views/SingleMuxView.axaml` 承载，绑定根为 `SingleMuxViewModel`；旧 `VideoInput` 文本框已由视频摘要和 `PickVideoButton` 新入口替换，Smoke 用实际按钮键盘事件、接纳及 `VideoName` 编译绑定验证，不设置隐藏兼容控件。

环境规则位于 Core `Components/`：`ComponentModels` 分开定义检测状态、安装阶段与能力；`ComponentVerifier` 复用 `ToolLocator` 和 `ProcessRunner`，验证启动、版本与必要依赖；`ComponentReleases` 维护固定版本、包布局、体积和哈希；`ComponentInstaller` 拥有四组件下载、校验、暂存验证、不覆盖提交、安装日志和取消清理，`AssfontsInstaller` 仅保留首轮兼容入口并共用并发守卫。`FontToolsLocator` 由检测和封装共用，优先管理目录内的独立后端，其次保留旧系统查找；安装暂存验证通过 `Workspace.FontToolsDirectoryOverride` 强制检查候选依赖。Desktop `EnvironmentViewModel` 保留稳定条目、检测修订与安装任务；`EnvironmentComponentViewModel` 投影状态点、行内动作和详情。`StoragePaths` 与 `StorageSettingsStore` 负责路径布局和持久化；MainWindow 仅接线操作互斥、目录选择、官方链接与关闭等待。实际封装参数和 QC 仍由原单任务流程决定，当前范围见[补齐组件安装](../tasks/component-installation-complete.md)。

组件卸载以当前配置目录为管理边界：手动复制的组件也计为 Mux Deck 来源，不使用安装归属记录。Core `ComponentDirectories` 只生成固定直接子目录的计划并检查边界/链接，`ComponentUninstaller` 重新验证后逐项删除并记录日志，与安装器共用 `ComponentOperations.Gate`。AFS 的随包 Python 随主目录删除，独立 `fonttools` 保留并单独管理；重装可复用已验证的独立后端。Desktop 保存待确认计划、卸载任务和错误；MainWindow 结合单任务、导入与真实工具探测忙碌态禁止删除，关闭等待收尾。完整边界与证据见[组件卸载任务](../tasks/component-uninstall.md)。

单任务交互由 `ViewModels/SingleMuxViewModel.cs` 持有；它构造 `MuxRequest` 快照并调用 `SingleMuxService`，包含 `StartAsync`、`CancelAndWaitAsync`、`RunTask`、`IsRunning`、`CanEdit`、`Tracks`、`SelectedTrack`、`Log`、`Result` 与 `StatusText`。`TrackRowViewModel` 表示内封来源轨道与外加字幕的展示行；SC/TC 外加字幕各自显式维护名称、语言、default 和 forced，输入组合变更时只应用一次 SC/TC 默认规则。`MainWindowViewModel` 继续负责导航和工具环境摘要。UI 不承担 QC，也不构造 AFS 或 mkvmerge 参数。

第一轮任务 A 的新增状态和接纳接口位于同一 ViewModel 及 `ViewModels/InputAcceptance.cs`，不放入 Core：

- 摘要由 `VideoFileName`、`VideoFullPath`、`VideoDimensions`、`VideoCodec`、`VideoDurationNanoseconds`、`MediaSummary` 与 `ProbeState`/`ProbeText` 表达；未知字段为空，不猜容器。`SourceTracks` 和 `ExternalTracks` 是只读可观察分组；`SourceAttachmentCount`/`SourceAttachments` 用于附件数量和详情。旧 `Tracks` 仍含源轨、附件与外加字幕，以兼容当前视图。
- 固定 `ScTrack`/`TcTrack` 的 `IsIncluded` 独立于路径、名称及旗标。禁用仍可查看；`SetSubtitleIncluded`、`RemoveSubtitle` 按槽位操作，提交只消费启用 ASS。同槽替换和启用切换保留名称/default/forced；添加或移除改变槽位组合时沿用 SC/TC 默认规则。两个槽位只订阅一次元数据变化，并在运行、取消清理和关闭等待时用模型守卫拒绝路径、元数据及启用变更。
- 视图从系统文件数据构造 `InputCandidate`（本地文件、本地文件夹；URL/文本标为 `NonLocal`），先调用 `PrepareVideoInput`、`PrepareSubtitleInputs`、`PrepareFontsDirectory` 或 `PrepareOutputDirectory`。字幕分配用 `SubtitleAssignment(SubtitleSlot, InputCandidate)` 明确 SC/TC。准备阶段只校验并返回只读 `InputAcceptancePlan`，不修改配置。视频或已有字幕槽位的异路径替换通过 `RequiresReplacementConfirmation` 要求视图明示确认；取消直接丢弃计划。
- 确认后 `CommitInput(plan, replacementsConfirmed)` 再核验 `CanEdit`、草稿修订及全部候选的类型/存在性/唯一性，一次提交。只接纳完整 Windows 路径；按规范化绝对路径和大小写比较，不识别硬链接/符号链接身份。批量重复按操作完成后的 SC/TC 集合比较，整组交换合法，同槽同路径无害。结果的 `Error`/`Message` 给出拒绝原因；失败仅更新 `InteractionMessage`，草稿和 Core 结果均保留。选择器、复制或平台动作异常可用 `ReportInteractionMessage` 表达。
- `DraftRevision` 对参数、启用和外加元数据变化计数；一次接纳批次增加一次，同值赋值、导航、选择和展示不增加。`ResultRevision`、`IsResultHistorical`、`ResultLabel` 区分当前草稿与上次 Core 结果；配置变化保留 `Result`、诊断和日志，再次运行期间也保留上次结果至新结果返回。主区使用 `State`/`StateSummary`，进度使用 `ProgressStage`/`ProgressMessage` 与既有不定进度；详细 `Checks`/`Warnings`/原始 `Error` 从 `Result` 读取。`CanStart` 只表示提交条件齐全，最终验证仍归 Core。

单个封装最新结构见 [工作台重构任务](../tasks/single-mux-workbench.md)；三张旧稿保留为历史布局参考：

- `MainWindow` 保留 172 宽左侧栏与底部工作区路径条，给单任务视图提供整块客户区宿主；`SingleMuxView` 持有封装/字体附件/可选开发日志页签与全高轨道属性栏，不再接收重复页面标题。封装页主区自上而下为视频摘要（约 72 高，两行投影）、轨道与内容（约 312，清单内部滚动）、输出设置（约 143，字体/输出目录并排、字体辅助说明及输出文件名）。主区不整体滚动：默认列宽下普通 3 源轨 + SC/TC 在 1040×760 下无需滚动，更多轨道只在清单内部滚动。固定状态区（约 76 高）不随内容滚动；底部结果面板止于其上缘，全高轨道属性栏会覆盖其右侧操作，关闭栏后恢复可达。
- 主表列为“无文字纳入列、ID、类型、语言、名称、默认、强制、编码”，共享 `TrackRowTemplate` 和 `TrackColumnLayout`，`TrackColumnsGrid` 为每个实例投影独立列定义；表头拖动/键盘调整由 `TrackColumnHeader` 承担，表格内部允许水平滚动并同步表头，源/外加以分组标题区分，删除逐行详情/更换/移除按钮，整行主要区域是行选择按钮。首列包含状态是按来源策略生成的展示投影（`TrackRowViewModel.InclusionBox`）：源视频/音频勾选只读、源字幕不勾选只读、外加字幕可切换，不直接绑定默认为 true 的 `IsIncluded`；只读源行仍可选中。行选择按钮的可访问名称由 `TrackRowViewModel.AccessibleName` 明确给出，不能退化为控件类型文本。
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

外加列表按首次添加顺序展示，`DisplayId` 为从 0 连续编号的展示投影，内部 `Id` 仍为 SC/TC；替换不改顺序，移除后重排，再添加位于末尾。空名称不填默认占位。`LanguageDisplay` 只转换展示名称，源行保留传统/BCP 47 两种原始代码与编码详情；传入 Core 的语言和旗标不变。Action Bar 只统计已知缺项，“已准备好”表示基本输入齐全，不代表预检完成。

`Preferences/UserPreferencesStore.cs` 是 v2 界面偏好文件边界，生产 `App` 注入用户 LocalApplicationData/MuxDeck/v2/preferences.json；测试注入 artifacts 位置。`TrackColumnLayout` 独立于草稿，版本化加载、范围限制、损坏回退，异步串行原子保存；关闭窗口等最后一次保存，保存失败只影响偏好，不改变任务结果或阻止已安全停止的窗口关闭。

上述为职责落位规则，按实际任务创建文件和子目录；不预建空模块或按每个动作拆分项目。

## 单任务接口

单任务入口封装子集化、封装、校验与成品落位的整条流程。界面和批量共用这一入口，参数组装与业务规则只有一份实现。目标入口为 `IRunSingleMux.RunAsync(MuxJobSpec, IProgress<JobProgress>?, CancellationToken)`，返回 `MuxJobResult`；迁移完成前为 `SingleMuxService.RunAsync(MuxRequest, …)`。

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
