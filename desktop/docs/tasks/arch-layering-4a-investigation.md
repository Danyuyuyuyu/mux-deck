# 4a 后续调查（2026-10-14）

基于 `208796d`，只报告，不改组件目录查询、字幕读取或语言规则代码。4a-1 的导入改动仅替换探测依赖，不迁移接纳/校验逻辑。

## 1. 环境组件的目录显示与异步契约

`EnvironmentComponentViewModel.cs:125–137` 的 `UpdateManagedDirectories` 在构造、刷新开始和检测完成时同步更新卸载目录与按钮。第 134 行的 `ComponentDirectories.TryPlan` 调用 `CreatePlan`，后者用 `PrepareUninstallAsync(...).GetAwaiter().GetResult()` 同步等待。当前 Backend 返回 `Task.FromResult`，但 Application `ManageComponentsUseCase.PrepareUninstallAsync` 会先取得检测租约；接口并不保证同步完成。查询包含目录存在性、直属边界、祖先联接检查，失败时 `TryPlan` 返回 null；行上还检查实际工具是否位于候选组件目录，保留外部工具不可卸载的行为。

| 4a-2 方案 | 实施方式 | 对 UI 套件的影响 |
|---|---|---|
| UI 改为异步 | 用 `IManageComponents.PrepareUninstallAsync` 获取候选计划；构造时先显示未完成状态，再启动查询；刷新/检测完成/路径修改统一等待目录任务。对旧 TryPlan 捕获的目录异常保持不可卸载结果；增加取消、修订号与关闭/迟到保护，确认前仍重新校验。 | 目录和菜单状态不再保证构造返回后立即可用。环境 UI 23、安装 UI 37、卸载 UI 61 中读取菜单/目录/按钮的场景要等待目录查询任务；仅 `Dispatcher.RunJobs` 不足以保证异步 IO 完成。需要在 4a-2 改装配替身和等待逻辑，并增加旧路径查询迟到、关闭等待、查询失败的覆盖。接纳/自动导入中涉及卸载互斥的 UI 场景也需回归。不得在 UI 上同步阻塞等待。 |
| 契约新增只读查询 | 在 `IManageComponents` 增加明确的目录状态查询及不可变结果，返回当前组件/依赖候选目录、可管理状态等事实；Infrastructure 完成路径、存在性、联接与实际工具归属判断。该结果用于显示，不能作为删除授权；实际确认继续走 `PrepareUninstallAsync`，执行仍复核边界。若要求同步 UI 时序，查询必须明确为同步只读接口。 | 可保持构造与刷新后的即时目录/按钮投影，原环境/安装/卸载 UI 的等待时序通常不需变化；4a-2 仍需将 Core 模型与替身迁为契约，并覆盖查询异常、外部工具保留、路径切换和菜单状态。需新增契约/假实现/文件边界测试。同步文件系统检查仍会占用 UI 线程，网络盘或深祖先路径应考虑后续异步化。 |

两者均须保持“无目录/越界/联接 → 不显示可卸载候选”的现有含义，不应把预期的不可管理状态显示成操作失败。4d 的关闭租约拒绝行为单独处理。若优先保留现有 UI 时序，第二方案改动更集中；若要避免文件系统阻塞 UI，第一方案更合适。

## 2. `SubtitleText.Read` 与 `MuxInputReader.ReadTrackAsync`

| 行为 | Core `SubtitleText.Read` | Infrastructure `MuxInputReader.ReadTrackAsync` |
|---|---|---|
| 严格 UTF-8 | 非 ASS 使用 `new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path))`；非法 UTF-8 抛 `DecoderFallbackException`。 | 对存在的外加字幕且扩展名为 ASS/SRT，使用相同严格解码。 |
| BOM | 解码后 `TrimStart('\uFEFF')`，删除开头所有 U+FEFF；正文中的 U+FEFF 保留。 | 完全相同。UTF-16/32 BOM 不会触发自动识别；按 UTF-8 处理，通常拒绝。 |
| 编码回退 | 无 GBK、ANSI、UTF-16 或替换字符回退。 | 同样没有回退。`DecoderFallbackException` 不在 `PortBoundary.Guard` 的转换列表中，因此仍原样抛出。 |
| 空 SRT | `Read` 返回空字符串；后续 `SubtitleText.Validate` 的时间轴正则拒绝。 | 返回 `Exists=true`、`SubtitleText=""`；后续 `RunSingleMuxUseCase.ValidateAsync` 的时间轴正则拒绝。读取阶段均不直接拒绝空 SRT。 |
| ASS 路径 | `.ass` 直接调用 Infrastructure `AssGlyphValidator.ReadAss`。该方法严格 UTF-8、去开头 BOM，并用 Domain `AssDocument.HasRequiredSections` 检查必要节；空 ASS 被拒绝。`Validate` 随后调用 `ReadUsage` 做结构/样式校验。 | 对 ASS 同样调用 `AssGlyphValidator.ReadAss`，随后单任务用例调用 Domain `AssDocument.ReadUsage`。两者共用实际 ASS 文件读取实现，并非两套解码器。 |
| 不存在/不支持/非字幕 | `Read` 直接读取，缺文件抛文件异常；所有非 ASS 扩展名也按严格 UTF-8 读取。`Validate` 才提前限制 ASS/SRT。 | 先规范化路径并检查存在性；缺文件返回 `Exists=false`、文本 null；非字幕或不支持的扩展名不读取文本。 |
| 返回与失败边界 | 只返回原文，原文件异常与 `InvalidDataException` 直接抛出；未接受取消令牌。 | 返回规范路径、存在性与原文事实，入口检查取消。IO 失败转换为 `ToolFailureException(MUX_WORKSPACE_IO_FAILED)`，ASS 必要节失败转换为 `MUX_TOOL_OUTPUT_INVALID`；取消和已有端口失败保留。 |

结论：解码、BOM 与 ASS 读取行为一致，差异主要在输入筛选、缺文件事实、路径规范化和失败边界。4a-3 合并读取时需显式保留这些入口差异。SRT 时间轴确有重复：`Core/Muxing/SubtitleText.cs:16` 与 `Application/RunSingleMuxUseCase.cs:313` 使用同一正则，按计划迁入 Domain 共用；本次未改。

## 3. 语言规则与重复

调查范围为 `desktop/src` 的 v2 生产 C#，不把测试预期或冻结的 v1 Python 算作第二份 v2 实现。

- **导入语言推断没有第二份实现**：`Domain/LanguageRules.cs:8–15` 为唯一规则；`Desktop/ViewModels/MediaImport.cs:87–88` 只提取文件名并调用它。
- **QC 语言匹配没有第二份实现**：`Domain/LanguageRules.cs:17–21` 为唯一规则；`Domain/OutputQcRules.cs:51`、`:68` 调用它。旧 Core 转换只保留原始语言、null/空 IETF，不重新实现匹配。
- **语法校验没有第二份实现，但仍位于 Application**：`Application/RunSingleMuxUseCase.cs:280`、`:312` 的 `ValidLanguage` / `LanguagePattern` 同时用于字幕与音频（`:135`、`:143`），Domain `LanguageTag` 仅保存值，不重复验证。
- **别名知识重复出现**：`Domain/LanguageRules.cs:19` 的 `chi/zho→zh`、`eng→en`、`jpn→ja` 与 `Desktop/ViewModels/LanguageDisplay.cs:13–19` 的显示名称映射包含同一组别名；后者仅本地化显示，并非 QC 匹配副本。显示表额外支持区域中文、韩/法/德等语言，不能直接用 QC 的有限映射替代。
- **IETF 优先取值重复出现**：`Domain/LanguageRules.cs:10` 与 `Desktop/ViewModels/SingleMuxViewModel.cs:489` 都使用 `LanguageIetf ?? Language`。前者还做未声明语言的文件名推断，后者只是展示源轨道，不能把推断应用到源轨道。`LanguageDisplay` 归一化显示查找键，封装参数仍传原值。

本次仅记录上述重叠知识与尚在 Application 的纯校验，不移动或统一语言代码。
