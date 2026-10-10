# 阶段一：单视频封装验收任务

本文件描述第一阶段的任务范围、接口、验收门槛与实际证据。Core、Desktop 和验收 harness 已实现；自动化、真实工具链和完整电影重放均通过。原生文件选择器与播放器人工检查仍未覆盖，因此严格阶段验收尚未全部完成。阶段进度的权威记录仍是[开发流程](../agents/migration.md)。

## 范围

输入一个视频（包含 MKV 与 MP4 源素材场景）、一个或两个 UTF-8 ASS 字幕、一个显式字体目录和一个独立输出目录。第一阶段固定使用 AssFontSubset 与 PyFontTools 后端；不引入替代子集化工具、字幕自动匹配、其他字幕格式或批量队列。

每条新增字幕轨都显式提交并核验轨道名、语言、default 与 forced。SC+TC 时 SC 是默认轨、TC 不是默认轨；仅 TC 时 TC 是默认轨。源视频及全部源音轨保留，源字幕轨和源附件移除，新字幕和本任务子集字体加入输出。默认文件名为 `<sourceStem>.muxed.mkv`；任何已有目标均拒绝覆盖。

源视频、源字幕和字体文件只读。失败或取消不得发布最终成品；成功产物先在本任务临时目录生成并通过检查，再以不覆盖方式落位。验收输入副本、临时素材、工具 stdout/stderr、报告、截图和测试输出位于 `desktop/artifacts/phase1/<run-id>/`；Core 原始任务日志在 `desktop/artifacts/runs/<task-id>/task.log`，集成 harness 复制真实链路任务日志到该验收 run 的 `task-logs/`。Smoke 其他临时 fixture 位于 `desktop/artifacts/smoke-<run-id>/`。这项隔离约定针对测试数据；应用实际运行的成品须写入请求指定的 `OutputDirectory`。不得写入仓库根 `data/tmp` 或 v1 配置/历史目录。

## Core 接口约定

单任务公开入口位于 `MuxDeck.Core.Muxing`：

```csharp
SubtitleInput(Path, Name, Language, IsDefault, IsForced = false)
MuxRequest(VideoPath, Subtitles, FontsDirectory, OutputDirectory, OutputFileName = null)
MuxStatus.Succeeded | Failed | Cancelled
MuxProgress(Stage, Message, Percent = null)
MuxResult(Status, OutputPath, Error, LogPath, Checks)
MuxResult.Warnings: IReadOnlyList<MuxWarning> = []  // structured advisory only; does not replace Checks
MuxWarning(Code, Message, Occurrences)
SingleMuxService(Workspace).RunAsync(request, progress?, ct)
SingleMuxService.ProbeAsync(path, ct) -> MediaProbe
```

当前确认的探测数据模型是 `MediaTrack(Id, Type, Codec, CodecId, Language, LanguageIetf, Name, IsDefault, IsForced, PixelDimensions, AudioChannels, AudioSamplingFrequency, HasDefaultFlag = true, HasForcedFlag = true)`、`MediaAttachment(Id, FileName, ContentType, Size)` 与 `MediaProbe(Path, Tracks, Attachments, DurationNanoseconds)`。Has*Flag 区分容器未提供旗标字段与显式 false；每个轨道的 `Id` 和类型以实际 mkvmerge JSON 为依据；验收不以语言单独匹配轨道。

Core 工具适配需要将固定 AFS 参数传入 `AssFontSubset.Console.exe`，包含 `--subset-backend PyFontTools` 和从 Python312 `Scripts` 目录解析的 `--bin-path`；正式 fixture 脚本须从运行时实际可执行文件路径构造参数，并保留子集化和封装日志。封装参数显式给出每条新增字幕轨的语言、名称、default、forced；输入视频与子集 ASS 之外的源字幕/附件不得隐式传入。

已按本机工具帮助核对命令形状：AFS 接受一个或多个 ASS 路径及 `--fonts`、`--output`、`--subset-backend`、`--bin-path`；mkvmerge 使用 `--output`、每输入文件的 `--no-subtitles`/`--no-attachments`，新轨用 `--language`、`--track-name`、`--default-track-flag`、`--forced-display-flag`，字体通过 `--attachment-name`、`--attachment-mime-type`、`--attach-file` 加入；探测用 `-J`。mkvextract 以 `tracks TID:out` 和 `attachments AID:out` 提取数据。当前 ffmpeg `subtitles` filter 支持 `filename` 与 `si`，集成验收用它渲染输出 MKV 内封轨道，并保存 debug 日志。

## 隔离 fixture

复用 `tests/MuxDeck.Smoke` 控制台，不增加测试包或项目，使用 `--integration` 入口。每次调用创建独立 run-id 目录，不共享或并行执行 fixture。真实 UI 验收使用 `--ui-integration <fixtureDirectory> <screenshotPath>`，重用同一次 `--integration` 的 `input/phase1-source.mkv`、`input/sc.ass`、`input/tc.ass` 与 `fonts-yahei/`，并把 UI 截图及 UI 输出写入该 run 目录。测试生成并记录：

- 短时长、有可检查视频帧和一条音轨的源 MKV；再用 mkvmerge 为源容器加一条旧字幕轨和一个旧字体附件，以验证替换行为。
- UTF-8 SC/TC ASS，含英文与中文、粗体/斜体、ASS 覆盖样式的不同字体引用；为单字幕和双字幕用例准备素材。
- 只从 `C:\Windows\Fonts` 复制选中的 Arial 或 Microsoft YaHei 字体文件到该 run 的 `fonts-yahei/`；另建仅含 Arial 的缺字形目录和空的缺字体目录。引用字体必须在子集产物和日志中能追踪；不得把系统字体目录直接作为任务字体目录。
- 从 ToolLocator 解析 ffmpeg、mkvmerge、mkvextract、AssFontSubset 与所需 Python 后端；运行前记录路径、版本或帮助输出。命令行参数按工具当前帮助核对。

建议先用 Arial 验证中英混排是否完整；若缺少目标中文字形，显式改用 Microsoft YaHei 的对应常规、粗体和斜体文件后重建 fixture。缺字形用例单独构建一个字体目录，不能靠系统 fallback 掩盖。源视频与字幕在任务前后计算 SHA-256。

## 验收场景与证据

| 场景 | 通过条件 | 证据位置/状态 |
|---|---|---|
| 单 ASS | 新字幕轨、正确名称/语言/default/forced、视频与源音轨保留，旧字幕和旧附件消失；新字体附件及 ASS 字体引用完整 | 通过；见[真实链路报告](../../artifacts/phase1/20261010-015647-daefe8254a624279bdb71f726b161b95/report.json) |
| SC+TC 双字幕 | 两条新增轨身份明确；SC default/non-forced，TC non-default/forced；两个语言和名称正确 | 通过；同上 |
| TC only | 唯一字幕轨 default=true；名称、语言、forced 正确 | 通过；同上 |
| MP4 源 | MP4 输入产出 MKV；视频与音轨的 codec 名、源中已知媒体属性/轨元数据保持；源未声明的 codec_id、default/forced 字段不猜测比较 | 通过；同上 |
| 已有目标 / 安装竞态 | 已有目标哈希保持；Installing 回调后并发创建目标时失败且并发目标哈希保持 | 通过；同上 |
| 提交后取消 | Completed 回调取消；已提交结果仍返回 Succeeded，成品可读 | 通过；同上 |
| 字体缺失或缺字形 | 缺字体错误包含工具字体诊断；缺字形错误列 U+ 码位；两者均不发布目标 | 通过；同上 |
| 取消 | 子集化阶段同步取消，以及 AFS 实际进程运行中取消；返回 Cancelled、不发布目标、后代进程退出 | 通过；同上 |
| 管道收尾 | 父进程退出、后代仍持有 stdout/stderr 时 ProcessRunner 有界完成并收尾后代 | 通过该受控后代场景；不覆盖 Start→Assign Job 启动竞态 |
| 源音轨 IETF QC | fake probe runner 修改源/输出 audio 的 language_ietf 而保持其他轨字段，QC 必须失败且不发布目标 | 通过；同上 |
| 外部工具与字体提示（[分类规则](../agents/diagnostics.md)） | AFS 仍只允许精确颜色高字节 warning；Core 可提示 Regular-only face 的 libass 合成粗体，或在完整/PostScript face、AFS mapping、唯一源文件及源/子集元数据严格一致时提示 `FONT_INTRINSIC_WEIGHT`。generic family、mapping/候选歧义、缺字形、未知工具 warning、产物或 QC 错误仍阻止发布 | fake/public service 回归 57/57，见[报告](../../artifacts/warning-policy/20261010-020043-2d4a39abbdd94856927596db2dd90bc3/report.json)；完整电影重放 9/9，见[真实报告](../../artifacts/warning-policy/real-e223f0011b774942bf40123b7b1c76a3/report.json) |
| ASS transport 内容 QC | 使用同一 MKVToolNix 对修正 ASS 生成 subtitle-only 参考，参考提取与成品提取全文一致；只统一 CRLF/LF，Dialogue 内容、时序、Style、字体、顺序和边缘空格不能变化 | fake runner 覆盖合法 Comment 重排与 7 类损坏拒绝；真实电影 SC/TC 均通过，见[回归报告](../../artifacts/warning-policy/20261010-020043-2d4a39abbdd94856927596db2dd90bc3/report.json)和[电影报告](../../artifacts/warning-policy/real-e223f0011b774942bf40123b7b1c76a3/report.json) |
| 源文件完整性 | 视频、字幕和字体源文件 SHA-256 前后不变 | 通过；同上 |
| 日志与 QC | libass `fontselect` 指向 MKV 附件中的子集重命名字体，而非系统 fallback | 最新真实工具链 121/121 通过；见[集成报告](../../artifacts/phase1/20261010-015647-daefe8254a624279bdb71f726b161b95/report.json) |
| 渲染图 | ffmpeg 输出两帧 PNG；人工检查中英、粗斜和覆盖字体样式 | 通过并已视检：[SC](../../artifacts/phase1/20261010-015647-daefe8254a624279bdb71f726b161b95/render-sc.png)、[TC](../../artifacts/phase1/20261010-015647-daefe8254a624279bdb71f726b161b95/render-tc.png) |
| 真实 UI | 真实探测轨表和移除/保留策略、StartCommand 成功路径、运行锁定、结果/日志可定位、窗口安全退出 | 10/10 通过；[UI 截图](../../artifacts/phase1/20261009-113607-f1ca9f2ec2694a72aa06fbdc0a31deb3/real-ui-success.png)；任务日志 `../../artifacts/runs/20261009-113726-d9c8ba10afff49c38aa08802992ff9f0/task.log` |
| 原生文件选择器 | 在交互桌面测试系统文件/目录选择器 | 未覆盖；人工项 |
| 播放器 | 代表性字幕在真实播放器中播放检查 | 未覆盖；人工项 |

## 首阶段边界

- ASS `\t(...)` 动画中的字体、粗体或斜体切换不支持，非标准 ASS 字重不支持；输入遇到这些结构时应明确失败，不以漏字或错用字体继续封装。
- 使用固定 AFS 子集器时，混合 `\p` 绘图与文本可能无法成功子集化；此类工具失败必须停止任务且不发布最终文件。
- 字体页当前只配置本次字体目录。成品实际附件以 Core QC/probe 和任务日志中的子集附件结果为准，不把界面配置误称为已完成播放器验证。
- 原生文件选择器和真实播放器播放检查仍是独立人工项；PNG 渲染和 headless UI 验收不能代替它们。

两帧截图、输入摘要、SHA-256、最终 mkv、逐轨/附件 probe 摘要和每个场景的退出状态均保留于所属 run 目录。集成 harness 将 Core 每个任务返回的日志复制到该 run 的 `task-logs/`，Core 原始日志保留在 `desktop/artifacts/runs/<task-id>/`。拒绝覆盖、失败和取消场景须保留原目标哈希与源哈希记录。

`desktop/artifacts/` 是本机忽略目录，当前文档中的 run-id、绝对日志路径和截图链接是本次工作站证据，不随 Git 提交。其他工作站重新验收时会生成新的 run-id 和证据目录。

失败注入只在 Core 现有工具边界确有必要时增加小接口；真实链路用例是主要验收。测试至少覆盖真实单字幕、双字幕、TC-only 默认轨、输出冲突、字体失败、取消及源文件不变。未执行的播放器检查、当前工具环境缺失或不能确定的字体选择结果应逐项标记，不得宣称整阶段通过。

## 目前状态

- 2026-10-10 Release 构建为 0 warnings / 0 errors；Smoke 26/26；`--warning-policy` 57/57，报告为[告警策略回归](../../artifacts/warning-policy/20261010-020043-2d4a39abbdd94856927596db2dd90bc3/report.json)。
- 完整电影重放 9/9 通过：[真实报告](../../artifacts/warning-policy/real-e223f0011b774942bf40123b7b1c76a3/report.json)。报告验证 1 条 video、2 条 audio、SC/TC 字形与 ASS 参考全文 QC、16 个字体附件提取及哈希，视频元数据和源 ASS/字体哈希不变；颜色告警 102 次，4 种合成粗体提示各 2 次，固有粗体提示 2 次。旧[重放失败报告](../../artifacts/warning-policy/real-52d8443120a74073b2438784789bc798/report.json)因四条 Comment transport 重排而拒绝，已由同工具参考机制修复。
- 真实工具链：121/121，最新报告为[2026-10-10 阶段一集成](../../artifacts/phase1/20261010-015647-daefe8254a624279bdb71f726b161b95/report.json)。SC/TC 渲染图已生成并视检，字体选择记录对应内封的子集重命名字体。
- 合成粗体另有 Arial Regular-only 真实 fixture 历史证据 8/8：[报告](../../artifacts/warning-policy/real-2f43ddd6a3d744d081850c7a3fcf08ca/report.json)。
- 真实 UI：10/10；截图为 `../../artifacts/phase1/20261009-113607-f1ca9f2ec2694a72aa06fbdc0a31deb3/real-ui-success.png`，实际任务日志为 `../../artifacts/runs/20261009-113726-d9c8ba10afff49c38aa08802992ff9f0/task.log`。
- 实现和自动/真实链路验收通过；严格阶段验收仍未全部完成，因为原生文件选择器和真实播放器检查未覆盖。不得据此宣称所有首阶段人工验收完成。
- Windows `Process.Start()` 与随后 Assign Job 不原子；已覆盖的同步工具和受控子进程用例不构成任意 launcher 的全进程树保证。该启动竞态列为阶段二 P2 可靠性待处理。
