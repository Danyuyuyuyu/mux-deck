# ADR-0001：按职责、依赖与契约分层

- 状态：已接受（2026-10-13，经 [PR #1](https://github.com/Danyuyuyuyu/mux-deck/pull/1) 评审合并）
- 范围：`desktop/` 下的 v2
- 规则位置：分层、依赖与契约规则以[代码骨架](../agents/skeleton.md)为准；迁移步骤与完成条件以[开发流程](../agents/migration.md#架构分层重构)为准。本文只记录背景、决策与取舍。

## 背景

阶段一单个封装与组件管理已落地，下一步是可靠性加固和批量复用。现有两项目结构（`MuxDeck.Core` / `MuxDeck.Desktop`）在以下方面阻碍复用和验证：

| 问题 | 位置（迁移前） |
|---|---|
| 单个类承担过多职责 | `Core/Muxing/SingleMuxService.cs` 同时负责探测、AFS/mkvmerge/mkvextract 参数、QC、成品落位、日志与临时目录；`Desktop/ViewModels/SingleMuxViewModel.cs` 同时负责草稿、规则、导入、运行与展示 |
| 业务规则位于界面层 | SC/TC 默认轨规则、输入接纳、文件名语言推断、输出文件名规则、`CanStart` 前置条件位于 ViewModel；批量需要复制 |
| 跨功能互斥分散 | 封装/安装/卸载/探测互斥分布在 `MainWindowViewModel` 事件联动、`MainWindow.axaml.cs` 关闭等待与 Core 静态 `ComponentOperations.Gate` |
| 缺少装配入口 | 静态 `App.Workspace` / `App.Storage`；`EnvironmentViewModel` 自行创建 `ComponentVerifier` 等默认依赖；`StoragePaths` 在 Core，其持久化在 Desktop |
| 契约松散 | `MuxResult.Checks` 为文本列表、`Error` 为字符串；`MediaTrack.Type` 为字符串；`MuxStage` 混入 `Installing`；`SingleMuxService.UpdateWorkspace` 允许修改服务级配置 |
| 测试入口单一 | 仅 `MuxDeck.Smoke` 控制台按参数切换场景，规则无法单独做单元测试，依赖方向没有自动约束 |

开发主要由代理执行。只靠文档和命名空间约定的边界容易逐步失效，需要编译器和测试强制。

## 决策

1. **四个生产项目**：`MuxDeck.Domain`、`MuxDeck.Application`、`MuxDeck.Infrastructure`、`MuxDeck.Desktop`。依赖方向为 Domain ← Application ← Infrastructure，Desktop 只通过契约与用例接口使用 Application；只有装配代码引用 Infrastructure。
2. **契约暂不独立成项目**：位于 `src/MuxDeck.Application/Contracts/`，命名空间从一开始就是 `MuxDeck.Contracts`，只依赖 `System.*` 与 Domain，只含数据与接口。出现第二个调用方（如命令行入口）时拆为 `MuxDeck.Contracts` 项目，届时只移动文件、不改命名空间。
3. **草稿归 Application，规则归 Domain**：`MuxDraft` 负责草稿版本、准备→提交、运行时锁定与生成任务规格，单个与批量共用；默认轨、纳入、语言、命名、接纳等规则为 Domain 纯函数。ViewModel 只保留界面状态。
4. **单任务改为阶段流水线**：检查 → 探测 → 子集化 → 封装计划 → 封装 → 成品校验 → 落位，每阶段输入输出为 record；临时目录、任务日志与清理由 `TaskWorkspace` 负责。结果改为结构化契约（`CheckResult`、`JobFailure`、`Diagnostic`）。
5. **统一跨功能操作协调**：`IOperationCoordinator` 用租约表达封装、导入探测、组件检测/安装/卸载的占用与互斥，应用关闭等待全部租约释放，替换现有三处守卫。
6. **集中装配**：`Program` 为唯一装配入口，移除静态服务和 ViewModel 内的默认依赖创建。
7. **端口只建在真实外部边界**：外部工具、进程、文件落位、下载、持久化、时间/ID。不引入通用仓储、事件总线、中介者或万能任务接口。
8. **测试按层划分**：Domain 单元测试、Application 假端口流程测试、Infrastructure 真实工具集成测试、架构测试；`MuxDeck.Smoke` 保留为验收与 headless UI 入口。
9. **在阶段二进程归属竞态修复与阶段三批量之前完成**。竞态修复落在 Infrastructure，可直接用假进程端口覆盖极快 launcher 场景，避免修复后再搬迁。

## 备选方案

| 方案 | 结论 | 原因 |
|---|---|---|
| 保留 `MuxDeck.Core` 单项目，按命名空间分层 + 架构测试 | 不采用 | 改动最小，但 `internal` 无法跨层隔离，约束只靠测试；代理开发下漂移风险高。代码量约 290KB，批量前搬迁成本最低 |
| Contracts 立即独立成项目 | 暂不采用 | 当前唯一调用方是 Desktop，多一套 csproj 与锁文件而无额外隔离收益；按决策 2 的条件再拆 |
| 只拆 Core，Desktop 不动 | 不采用 | 规则仍留在 ViewModel，批量必须复制 SC/TC 默认轨与接纳规则 |
| 引入中介者/事件总线式架构 | 不采用 | 违背现有“在具体依赖处建立小接口”的原则，增加间接层且不改善业务表达 |

## 后果

- 收益：依赖方向由编译器和架构测试强制；规则可单元测试；单个与批量共用草稿、规则与单任务入口；结果可按结构展示，不再解析文案；互斥与关闭等待只有一处实现。
- 代价：生产项目从 2 个增至 4 个，需要维护各自锁文件；迁移期间新旧模型并存并需转换；`skeleton.md` 中迁移前的文件落位需随步骤逐节改写。
- 风险与控制：搬迁可能改变工具调用或 QC 行为。重构不改变用户可见行为和工具参数，涉及流水线的步骤须重跑真实工具链并与迁移前结果对照；发现的旧缺陷单独记录、单独修复。
