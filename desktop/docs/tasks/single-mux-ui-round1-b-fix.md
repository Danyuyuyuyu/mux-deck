# 任务 B 收尾：外加字幕按钮的可访问名称

状态：2026-10-10 随 [布局落地任务](single-mux-ui-approved-layout.md)完成，负责人独立 UI 检查 85/85 确认实际 SC/TC peer 名称可辨认，本项关闭。下文保留原缺陷与修复合同；布局新增路径/键盘问题另见 [审查](single-mux-ui-approved-layout-review.md)，不否定本项修复。

建议模型：`gpt-6-luna`，推理强度 `xhigh`。任务小且范围明确；复杂问题先反馈负责人。

先读 `desktop/AGENTS.md` 和 [B 审查](single-mux-ui-round1-b-review.md)。保留 A/B 全部工作区改动，不提交或推送。

## 问题与修复

`src/MuxDeck.Desktop/Views/SingleMuxView.axaml` 的外加行名称按钮（当前第 108 行）内容是 StackPanel，没有明确可访问名称。实际 SC/TC 两行 AutomationPeer.GetName 都为 `Avalonia.Controls.StackPanel`。

为该按钮设置可辨认、表达动作的 `AutomationProperties.Name` 或等价正确绑定，例如按 Id 显示“查看 SC 字幕详情”与“查看 TC 字幕详情”；绑定须在两行实际控件上生效。外加行的启用、编辑、选择和详情动作保持当前行为。

在 `tests/MuxDeck.Smoke/SingleMuxUiRound1Checks.cs` 增加少量有意义的行为断言：创建 SC+TC 后，从实际两行名称按钮取得 AutomationPeer.GetName，名称分别辨认 SC/TC 且不是控件类型文本；按钮动作仍打开正确轨道详情。不以只检查 XAML 或读取 AutomationProperties 属性代替真实 peer 检查。

允许修改仅上述 XAML 和对应 UI 检查文件。不做布局、字体、颜色、其他功能或 Core 改动，不扩大为全应用无障碍重构。

## 验证与交付

串行 Release 构建和 `--single-mux-ui-round1` 必须通过；常规 Smoke 核对新动作接线。该修复不涉及 Core，不需重复三份真实封装、完整电影或告警政策全量回归。

返回 diff、SC/TC 两行的实际 peer 名称、命令/计数/报告路径及未覆盖项。完成后交负责人复核，不派发第二轮，不修改第一轮整体通过状态。
