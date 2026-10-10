# 布局收尾 F1–F5：负责人复审

后续状态：本报告的 G1–G4 已由负责人直接修复并通过 UI 151/151 验证，见 [最终验收](single-mux-ui-approved-layout-acceptance.md)。下文保留修复前的审查依据。

日期：2026-10-10。结论：F1、固定 SC/TC 的 F2、原 F4 复现和 F5 通过；F3 仍不完整，源轨选择的 F2 边界未满足。整体暂不验收，下一步仅执行 [键盘与源选择收尾](single-mux-ui-approved-layout-keyboard-fix.md)。

审查依据：[修复合同](single-mux-ui-approved-layout-fix.md)、已确认三图布局与适用 Desktop 文档。两个只读代理分别审查标准与合同，负责人动态确认下列四项 P2。无生产代码修改、提交或推送。

## 标准轴

1. **G1 / P2：结果面板自身操作被误排除。** `SingleMuxView.axaml.cs:230` 将所有候选控件都与 ResultInspector 做完全遮挡检查，没有先排除面板自身及其后代。关闭、复制路径/诊断、日志等可见控件均被移出 Tab 清单。违反 skeleton 的面板键盘操作合同。动态完整前向循环记录结果面板内部 Tab 站点为 0；正常结果详情按钮打开后也成立。
2. **G2 / P2：轨道面板背后的添加入口仍可获得焦点。** 同文件 `:229` 只排除视频摘要和设置区域，漏掉清单右侧操作。实际 Tab 循环到 `AddTrackButton`，其矩形完全处于轨道 Overlay 下面。违反修复合同明确要求处理“轨道面板盖住的右侧操作”；附件入口也需使用同一规则处理。
3. **G4 / P2：未选字幕替换改变源轨选择及路径。** `SingleMuxViewModel.cs:407–425` 重建源行，但用旧对象引用决定保留选择。选中源视频轨道 0，仅 Prepare/确认/Commit 替换 TC，SelectedTrack 变 SC，Inspector 从视频路径变为 SC ASS 路径。违反 F2“未选槽位替换不错误改所选路径；移除/选源轨也正确”。此次目录/固定槽位通知本身正确，缺口是源轨身份重建。

没有需要报告的代码气味判断。未发现新的 Core 请求、输出探测或诊断政策缺陷。

## 合同轴

1. **G1 / P2：结果面板控件不能经 Tab 操作。** 同上述原因，违反“面板打开与焦点移入协调…Tab/方向键可操作”。现有结果测试只逆向移动一次，并检查不进入设置，未检查进入面板自身操作。
2. **G3 / P2：面板打开时 Tab 被困在封装页。** `SingleMuxView.axaml.cs:246–256,335` 只枚举 SingleMuxView 后代，并取模循环、消费所有 Tab。两面板开时均无法从页面 Tab 到可见侧栏导航，实际完整循环的窗口其他控件站点为 0。违反非模态面板和不禁用可见页面操作的合同；不要求增加新导航能力，只保留原窗口的键盘可达性。

标准轴三项、合同轴两项，其中 G1 重复；负责人确认共四个独立修复场景。未动态确认的滚动裁剪/关闭恢复猜测不列为缺陷，不过修复 G1–G3 的完整遍历应包含多轨场景。

## 负责人动态验证与已通过部分

独立构建 0 warnings/errors；Smoke 26/26；A 88/88（`artifacts/single-mux-round1-a/20261010-083808-6a8686687abd49408faf4f378d286960/report.json`）；UI **106/106**（`artifacts/single-mux-round1-b/ui-d6c79b9cadeb49a4a914b72af0d0ca37/report.json`）。`git diff --check` exit 0，仅已有 LF→CRLF 提示。未重跑 warning-policy 或真实 Core；用户本次报告的 57/57 与既有真实封装证据保持独立标记。

原独立复现重新执行后：字体/输出打开按钮已 enabled；同槽新路径文本正确；Shift+Tab 不再到 OutputNameInput，文本输入不改输出名；同槽行重建后关闭回到当前 SC 行；Down 可移到 TC。新增 21 项断言确实覆盖这些原复现场景，不否定其价值，但未覆盖 G1–G4。

额外原生 headless 完整循环（上限 80 次，重复站点停止）证据：

| 场景 | 实际结果 |
|---|---|
| 正常 ShowResultButton 打开 → Tab 循环 | 10 个站点，面板内部 0；日志/复制/关闭都未进入 |
| 轨道 Inspector 打开 → Tab 循环 | 进入完全被遮挡的 AddTrackButton |
| 两种面板各自循环 | 页面之外站点 0，侧栏不可达 |
| 源视频轨道 0 选中 → 只替换 TC | Commit 成功；所选 Id 从 0 变 SC，Inspector 路径从源视频变 SC ASS |

证据与隔离程序：`desktop/artifacts/approved-layout-review/fix-reproduction.json`、`Program.cs`、`ReviewCheck.csproj`。循环使用真实 KeyPress/KeyRelease，不在每一步强行指定焦点。结果展示复用既有报告的真实成功快照，通过隔离程序反射设置到 Result 后从正常 ShowResultButton 打开；没有生成新的封装，也不将其称为 Core 端到端验证。源选择复现对既有真实小 MKV 执行只读公开 ProbeVideoAsync，TC 复制到隔离 artifacts 后走公开 PrepareSubtitleInputs→CommitInput(true)。

1040×760 普通截图已视检，目录按钮恢复启用，布局未变。原生选择器、Explorer、剪贴板/目录打开、DPI、辅助技术、弹出菜单 Esc、播放器仍待人工；G1–G4 为自动可复现问题，不留给人工。第一轮/migration 完成状态继续暂缓。
