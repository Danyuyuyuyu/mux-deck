# 任务 B 负责人独立审查

日期：2026-10-10。结论：主体实现和自动功能证据成立；待一个 P2 收尾修复及原生人工验收，暂不宣称第一轮完整通过。用户要求完成本轮后先讨论 UI，重点为布局与信息密度；本记录不派发第二轮。

## 规范轴

未发现已确认的硬规范违背。B 保持 Core、诊断政策和包配置；原生输入边界、接纳互斥、异步返回时身份/编辑锁/修订复核、finally 收尾和订阅解绑符合现有骨架；平台动作传递精确参数。

非阻断维护观察：视图 `Preflight` 与 ViewModel 最终校验重复了类型、扩展名和存在性检查。悬停预判与提交重验确有不同职责，本轮不要求抽象；后续格式变动需核对两处一致。

## 任务轴

确认一个 P2：`SingleMuxView.axaml:108` 外加行名称按钮 Content 为 StackPanel，未设明确 AutomationProperties.Name/LabeledBy。负责人在独立 headless 小程序中读取实际按钮的 `AutomationPeer.GetName()`，SC、TC 均为 `Avalonia.Controls.StackPanel`。这不符合任务 B 对可访问名称的要求。修复范围见 [收尾任务](single-mux-ui-round1-b-fix.md)。

本机复现程序与构建产物只在 `desktop/artifacts/round1-b-review-peer/`，未修改应用或已有测试。Avalonia 12.1.2 [ContentControlAutomationPeer 源码](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.2/src/Avalonia.Controls/Automation/Peers/ContentControlAutomationPeer.cs)也说明了 Content 文本/peer 名称不存在时的 ToString 回退。复现结果：

```json
[{"Id":"SC","Name":"Avalonia.Controls.StackPanel"},{"Id":"TC","Name":"Avalonia.Controls.StackPanel"}]
```

其余未发现整组接纳、取消/确认、迟到编辑锁、元数据提交、结果动作或同路径重新探测的确定性合同错误。旧摘要边界已修复，自动用例覆盖文件消失与工具失败两条路径。

## 负责人验证与证据核对

| 项目 | 实际结论 | 本次证据 |
|---|---|---|
| Release 构建 | 0 warnings / 0 errors | 本聊天工具输出；独立复现项目的最终串行构建也重建了全部生产项目与 Smoke |
| B UI / 模拟拖入 | 独立重跑 65/65 | `../../artifacts/single-mux-round1-b/ui-a2a97e180c8a4e8ba7d270ac2ad604ea/report.json` |
| A 行为回归 | 独立重跑 88/88 | `../../artifacts/single-mux-round1-a/20261010-040243-496b1bd30b394b8ba492903cb8200896/report.json` |
| 常规 Smoke | 独立重跑 26/26 | 本聊天工具输出 |
| warning-policy | 独立重跑 57/57 | `../../artifacts/warning-policy/20261010-070510-db209ce4dc0547d79c4b45a9d6baf4de/report.json` |
| 新 UI + 真实 Core | 核对执行者 36/36 报告、实际测试接线及产物；没有重新执行完整三任务封装 | `../../artifacts/single-mux-round1-b/real-af5905f844a94540a3b2fa5a62cf073b/report.json` |
| 产物哈希 | 实际读取三份成品，SHA-256 均与报告一致；报告中 SC/SC+TC/TC-only 分别为 1/2/1 条字幕、4/8/4 个字体附件 | 同上 |
| diff 检查 | 无空白错误，只有 LF/CRLF 转换提示 | 本聊天工具输出 |

复现项目首次默认 restore 使用了与原缓存不同的包目录，导致 Smoke 的本地 assets 出现离线 NU1301；已用现有全局缓存和 locked-mode 串行 restore 恢复，未改包版本/锁文件。复现程序自身的 Windows 平台分析告警已加平台判断解决。最终构建和复现成功，这两次辅助程序失败不属于 B 缺陷。

## 布局观察与人工限制

负责人查看了执行者的空态、多源轨、Inspector 展开/收起及真实双字幕成功截图。控件和固定状态区可用，但默认页面纵向占用偏大：视频摘要包含多行说明和独立路径折叠条；源附件为零及未选 Inspector 仍占用整条；完整源清单推低外加字幕和设置；长目录文本仍较显眼。成功后的 QC/日志路径也大量占据正文。这些归下一步设计讨论，不作为业务流程失败或当场修改理由。

原生选择器、Explorer 拖入、剪贴板/定位、DPI、原生键盘/辅助技术和播放器仍待人工。清单为 `../../artifacts/single-mux-round1-b/validation-20261010/manual-checklist.md`。无原生桌面控制能力，headless 不替代人工项。

两轴汇总：规范硬违背 0；任务确认缺陷 1（P2 名称）；维护建议 1，不阻断。本轮继续仅安排最小修复与人工检查，设计讨论可以并行进行。
