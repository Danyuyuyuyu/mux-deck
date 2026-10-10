# 外部工具诊断分类

本文是 Core 外部工具诊断、告警白名单和未知诊断策略的唯一权威来源。涉及 stdout/stderr 的解析、工具失败、可继续告警或用户可见 warning 时，按本文更新实现与对应验收。

## AFS 物理行

`MuxDeck.Core.Muxing.AfsDiagnosticClassifier` 在去除 ANSI 控制序列后，按 AFS 最外层物理日志行分类。可识别带 ISO 时间戳或裸 `|LEVEL|message` 的记录。stdout/stderr 都保留在任务日志；流来源不改变诊断级别。

| 分类 | 规则 | 任务行为 |
|---|---|---|
| `LogOnly` | `INF`、`DBG`、`TRC`、`VRB`；任意普通文本中仅出现 `WRN`、`missing` 等关键词也属于日志 | 仅记录，不创建结果 warning，也不阻止后续步骤 |
| `Warning` | 唯一可继续的 AFS 工具告警：消息精确匹配 `ASS color tag \c has more than 6 hex digits; high byte ignored.`，其中标签可为 `\c`、`\1c`、`\2c`、`\3c` 或 `\4c` | 继续完整的 ASS、字体、字形、封装、产物与 QC 检查；成功结果通过 `MuxResult.Warnings` 返回固定 code `AFS_COLOR_HIGH_BYTE_IGNORED`、说明和合并后的出现次数 |
| `Fatal` | `ERR`、`FTL`、任意未登记的 `WRN`、未知级别、不能可靠解析的诊断，以及字体缺失、重复字体、命令执行失败等已知工具错误 | 任务失败，不发布最终成品；错误原因保留在结果与日志中 |

颜色告警表示 AFS 忽略 ASS 颜色标签中的高字节。白名单只接受完整、精确的已验证文本；相似拼写、额外前后缀、未知 warning 或无法分类的诊断都不继承放行资格。新增可继续告警必须同时有真实工具证据、分类器规则和公共 `SingleMuxService` 行为回归。

## Core 字体兼容提示

`AfsDiagnosticClassifier` 的 AFS 告警白名单仍只有上述精确颜色告警。下面两种提示由 Core 字形验证器根据子集字体和来源元数据产生，不是工具日志放行规则。

`FONT_SYNTHETIC_BOLD` 适用于 ASS 请求粗体、精确粗体 face 未产出，且同家族、同斜体状态的 Regular face 恰好只有一个、字体权重允许 libass 合成加粗的情况。该 face 的 cmap 仍须覆盖全部请求字形。渲染时播放器会对内封字体合成粗体；这不是对字体缺失或字形缺失的豁免。

`FONT_INTRINSIC_WEIGHT` 只适用于 ASS `Bold=false` 明确引用完整/PostScript face 名，而 AFS mapping 将它映射到唯一 native-bold 子集 face 的情况。Core 还须证明 AFS mapping 与原 ASS 的字体及字形集合一致、源字体按 basename/faceIndex 唯一定位、源字体 nameID 4/6 与原引用精确匹配、完整名称不等于 nameID 16（若没有 16 则比较 nameID 1），且源与子集的 bold、italic 和 weight 元数据一致。候选字体的 cmap 也必须覆盖全部请求字形。满足这些条件时保留原生粗体 face，并提示播放器没有额外合成粗体；该规则不根据 generic family 推断 face 身份。

普通候选 face 不唯一、家族或斜体状态不匹配、字重无法确认、来源映射或元数据不一致、任何字形缺失时，仍须失败且不发布成品。合成规则只允许补粗体：ASS 同时请求粗体和斜体时，候选自身须已有匹配的斜体 face；仅请求斜体时也须有精确斜体 face。`Bold=false` 的 generic family 引用仍不得反向替换成粗体 face；只有满足上一段完整来源证明的显式 full/PostScript face 可例外。字体提示不接受或放行未知 AFS `WRN`，也不推断其他字体替代策略。

合成粗体的真实证据来自 Regular-only 方正小标宋：本机忽略目录中的 libass 粗体 [fontselect 日志](../../artifacts/diagnostics/face-8a039b09b3834c3c871d41e217eee288/render-bold.log)、常规 [fontselect 日志](../../artifacts/diagnostics/face-8a039b09b3834c3c871d41e217eee288/render-regular.log) 与 [渲染像素比较](../../artifacts/diagnostics/face-8a039b09b3834c3c871d41e217eee288/render-pixels.json) 证实同一字体 family 被选择且粗体渲染更重。固有粗体依据来自 [ExHeavy face 元数据与渲染记录](../../artifacts/diagnostics/face-8a039b09b3834c3c871d41e217eee288/evidence.md)：ASS 使用完整 face 名，源字体为 native weight 800，渲染日志选中同一 face 且无 fallback/glyph error。这些本机证据不会随 Git 提交；粗体合成阈值依据 [libass 字体选择实现](https://github.com/libass/libass/blob/master/libass/ass_font.c)。

## 行与严重级别

真实进程按最多 8 KiB 的部分块交付。分类必须先按同一 stdout 或 stderr 流重组 `IsPartial` 片段，再去除 ANSI 并识别完整物理行；单行聚合上限为 64 KiB，超过上限时保守失败，不以截断内容猜测。若 runner 同时提供实时行和最终 stdout/stderr，诊断仍须复核最终返回数据，重复事件不能重复累计同一条 warning。普通 INFO/DEBUG 后续消息不能清除或覆盖已识别的致命错误；已识别的 fatal 原因应稳定出现在任务错误中。

诊断分类不代替退出码、产物检查或成品 QC：非零退出仍失败；通过白名单的 warning 也必须继续验证修正 ASS、字体文件与字形、封装文件、轨道、附件、提取内容和不覆盖落位。每种失败均不得发布最终成品，并须保持输入源不变。

## 验收入口

`tests/MuxDeck.Smoke --warning-policy` 使用注入的 `IMuxToolRunner` 走公开 `SingleMuxService.RunAsync`，验证精确放行、可见计数、重复去重、ANSI、8 KiB 分块、INF/DBG 文本、致命原因保留、未知及长格式级别 warning、带危险后缀或 CR/LF 的颜色消息、结构化日志格式变体、缺字体/缺字形、合成粗体提示、原生粗体来源证明、映射和候选拒绝、产物不全、QC 不匹配与非零退出。它还验证颜色 warning 的实时/最终流计数和 UI 多 message 展示。当前实际通过数量与报告链接见[验证要求](verification.md#当前自动化能力)。该 harness 在 `desktop/artifacts/warning-policy/<run-id>/` 创建隔离工作区、日志与输出。

`tests/MuxDeck.Smoke --warning-real` 用真实工具生成短时颜色告警 fixture。`--warning-synthetic-bold-real` 生成只含 Arial Regular 的短时 fixture 并请求粗体；历史通过证据见[Regular-only fixture 报告](../../artifacts/warning-policy/real-2f43ddd6a3d744d081850c7a3fcf08ca/report.json)。两者都可带 `core-task.log` 从已有请求快照读取输入。重放时视频、字幕和字体目录只读，字幕以字节一致副本运行，不额外修改原始 ASS。输出、生成素材、报告和任务日志副本落入 `desktop/artifacts/warning-policy/<run-id>/`；应用 Core 原始任务日志仍位于 `desktop/artifacts/runs/<task-id>/task.log`。完整电影重放在同一 MKVToolNix transport 参考路径下 9/9 通过，见[真实报告](../../artifacts/warning-policy/real-e223f0011b774942bf40123b7b1c76a3/report.json)。旧[失败记录](../../artifacts/warning-policy/real-52d8443120a74073b2438784789bc798/report.json)因四条 Comment 移位被拒绝，现已修复；测试计数的最新权威位置见[验证要求](verification.md#当前自动化能力)。调用方式与本机工具前置条件见[README 验证命令](../../README.md#验证)。
