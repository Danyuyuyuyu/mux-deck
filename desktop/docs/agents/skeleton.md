# 代码骨架

本文规定 v2 的代码职责、依赖和接口。路径均相对于 `desktop/`；功能顺序见 [开发流程](migration.md)，验证要求见 [验证文档](verification.md)。

## 项目与依赖

```text
src/MuxDeck.Core/       核心规则、单任务流程、外部工具与文件操作
src/MuxDeck.Desktop/    Avalonia 应用装配、视图与 ViewModel
tests/                 自动化验证与测试素材生成入口
```

允许依赖：`Desktop → Core`，测试项目依赖其验证的生产项目。Core 保持独立于 Avalonia 和 v1 应用代码。项目间依赖保持单向；当前保留两个生产项目，出现真实隔离需求后再拆分。

## 职责与落位

| 职责 | 归属 | 对外表达 |
|---|---|---|
| 输入、任务快照、结果、进度 | Core 的任务类型 | 用业务数据表示，不携带 UI 控件 |
| 命名、轨道策略、校验判定 | Core 的纯规则 | 输入与结果明确，无进程或文件副作用 |
| 单个封装编排 | Core 的单任务模块 | 一个执行入口，接收任务、进度与取消 |
| 子集化、探测、封装工具适配 | Core 的对应工具模块 | 参数构建、协议解析、产物与错误转换 |
| 进程、工具定位、临时文件操作 | Core 的基础能力 | 屏蔽技术细节，不决定业务成败 |
| 批量队列编排 | Core 的批量模块 | 调用单任务入口，管理顺序与逐项结果 |
| 输入、当前页、忙碌态、结果展示 | Desktop 的 ViewModels | 属性和命令，不拼封装命令 |
| 布局、原生文件选择、焦点 | Desktop 的 Views | 视图行为，不执行封装流程 |
| 模块创建、依赖注入、窗口接线 | Desktop 的应用启动处 | 集中装配，避免业务模块自行读取全局 UI 状态 |

### 阶段一单任务职责

阶段一的业务编排与媒体工具代码位于 `src/MuxDeck.Core/Muxing/`。下表路径相对于 `src/MuxDeck.Core/`；进程技术适配位于 Core 根目录，不拆出额外生产项目：

| 职责 | 相对 Core 的文件 | 约束 |
|---|---|---|
| 请求、结果、状态、进度、轨道探测模型 | `Muxing/MuxModels.cs` | 以不可变业务数据表达；字幕输入保留路径、名字、语言和 default/forced 选择 |
| 单任务整流程、AFS/mkvmerge/mkvextract 参数与 JSON 探测 | `Muxing/SingleMuxService.cs` | 一个 `RunAsync` 入口拥有任务日志/临时目录、进度、取消与清理；固定 PyFontTools；按检查、探测、子集化、封装、QC、不覆盖落位排序 |
| AFS 诊断分类与结果 warning | `Muxing/AfsDiagnosticClassifier.cs`、`Muxing/MuxModels.cs`、`Muxing/SingleMuxService.cs` | 分类白名单、未知诊断与可继续告警行为以[工具诊断分类](diagnostics.md)为准；流程返回结构化 warning |
| ASS 字形使用解析及子集字体校验 | `Muxing/AssGlyphValidator.cs` | 读取 UTF-8 ASS 样式与覆盖字体，按对白统计实际码点，再与 ttx 导出的字体 `name`/`cmap` 表核对 |
| 可替换的外部进程边界 | `Muxing/IMuxToolRunner.cs` | `IMuxToolRunner` 只包住外部进程执行；默认 `MuxToolRunner` 复用 `ProcessRunner`，测试注入保持在这个具体边界 |
| QC 判定 | `Muxing/SingleMuxService.cs` 私有探测/QC 流程和 `Muxing/AssGlyphValidator.cs` | 对比源视频与音轨，逐轨验证新增 ASS 名称/语言/default/forced；验证附件身份、大小、类型、提取 SHA-256 和 ASS 内容；不能仅按语言找到任意轨即通过。ASS 以同一 MKVToolNix 将每条修正 ASS 单独封装、提取为参考，与真实成品提取全文比较；仅统一 CRLF/LF，Dialogue 文本、时序、Style、字体、事件顺序和文本边缘空格均须一致 |
| 文件、进程与日志支持 | `ProcessRunner.cs`、`WindowsProcessJob.cs` 与 `Muxing/SingleMuxService.cs` | 运行级临时目录和完整任务日志；已归属 Job 的工具进程用取消/父退出收尾；`Process.Start()` 到 Assign Job 存在未覆盖竞态，不能承诺任意 launcher 的后代全终止；目标用同目录临时文件及不覆盖 rename 落位 |

接口合同与第一阶段具体验收案例见 [阶段一任务](../tasks/phase-1-single-mux.md)。Core 已确认的探测类型为 `MediaTrack`、`MediaAttachment`、`MediaProbe`；公开的单任务签名按任务文档约定，真实文件和额外依赖以实现为准。

### 阶段一 Desktop 单任务约定

`ViewModels/MainWindowViewModel.cs` 保留四个页面状态（单个、批量、字幕工具、环境检测）和工具检测，并公开 `Single`；当前 `MainWindow.axaml` 只显示三个导航入口：单个、批量、环境检测。字幕工具仍是未实现占位状态，但当前没有可见导航入口。为 Smoke 兼容保留 `VideoPath`、`ScSubtitle`、`TcSubtitle`、`OutputDirectory` 转发属性；视频输入控件名为 `VideoInput`，字段通过编译绑定展示。控件及字段名是 Smoke 冒烟检查的兼容面。

单任务交互由 `ViewModels/SingleMuxViewModel.cs` 持有；它构造 `MuxRequest` 快照并调用 `SingleMuxService`，包含 `StartAsync`、`CancelAndWaitAsync`、`RunTask`、`IsRunning`、`CanEdit`、`Tracks`、`SelectedTrack`、`Log`、`Result` 与 `StatusText`。`TrackRowViewModel` 表示内封来源轨道与外加字幕的展示行；SC/TC 外加字幕各自显式维护名称、语言、default 和 forced，输入组合变更时只应用一次 SC/TC 默认规则。`MainWindowViewModel` 继续负责导航和工具环境摘要。UI 不承担 QC，也不构造 AFS 或 mkvmerge 参数。

上述为职责落位规则，按实际任务创建文件和子目录；不预建空模块或按每个动作拆分项目。

## 单任务接口

单任务入口封装子集化、封装、校验与成品落位的整条流程。界面和批量共用这一入口，参数组装与业务规则只有一份实现。

- 输入是本次任务的不可变快照，包含所有影响结果的路径和选项。
- 输出包含明确的成功、失败或取消状态，成品位置、校验结果与可定位的失败原因。
- 进度包含明确阶段；工具适配模块可解析工具自己的进度，流程模块负责阶段转换。
- 任务拥有独立临时目录、进程生命周期和结果；可通过取消令牌停止执行。
- 需要替换外部调用或做失败注入时，在具体依赖处建立小接口；不用通用仓储、事件总线或万能任务接口代替业务表达。

## 工具与文件操作

业务流程由 C# 实现，复用 MKVToolNix、AssFontSubset 等外部可执行工具。工具定位返回真实可用信息，路径存在、可执行性与后端依赖分别核验。

进程参数通过参数列表传递；同时读取 stdout/stderr；编码按工具约定处理。接入长任务时使用文件日志和有界界面缓存，避免无限累积输出。取消应终止并等待已纳入生命周期管理的相关进程退出，再清理本次资源；当前 Windows Job 归属不是 `Process.Start()` 原子操作的一部分，限制见 [开发流程](migration.md)。

新版配置、日志和历史使用专属目录或命名空间，具体位置在持久化阶段确定。v1 配置与历史作为只读参考；后续导入采用明确转换并保留原文件。共享第三方工具目录可以只读复用。

## UI 与状态

- 使用 MVVM 和编译绑定；每个绑定根与数据模板声明数据类型。
- ViewModel 调用核心入口，异步展示进度和结果；耗时处理不阻塞 UI 线程。
- 单个、批量和后续字幕工具各自拥有状态；切页保留输入，运行任务使用提交时快照。
- 表单参数、进度阶段与完成结果分开表示；以核心结果决定成功，日志用于诊断。
- 未实现的入口显示真实完成状态。原生窗口保持键盘操作、焦点反馈和内容滚动能力。

## 扩展与规则更新

后续预设、配置和队列等模块加入当前职责体系；模块对外暴露所需命令和结果，内部可变状态由自身维护。只有确实解决依赖或维护问题时才扩大项目结构。

改变项目依赖、单任务接口或状态归属时，在同一改动中更新本文与对应行为验证。SDK 与包版本直接查项目配置；更改依赖时一起维护锁文件。
