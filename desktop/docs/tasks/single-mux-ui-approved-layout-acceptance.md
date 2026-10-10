# 单个封装 UI 第一轮：代码收尾验收

日期：2026-10-10。用户本次授权负责人直接修复并提交。结论：A/B、三张已确认布局、B-fix、F1–F5 及 G1–G4 的代码/自动化验收通过；原生桌面及播放器项目仍待人工。未启动下一功能轮，未推送。

## 本次直接修复

- 窗口级键盘接线保留侧栏和固定状态区的非模态导航，视图脱离时解除；隐藏封装页/其他页签按框架默认导航。
- 活动 Inspector 及后代不当作被遮控件；底层按实际裁剪后可见区域过滤，轨道面板下添加/附件和两面板下设置不能经 Tab 激活。完整双向遍历可到结果关闭、复制、日志及所有侧栏入口。
- 方向键的可滚入目标独立于 Tab 当前可见目标。多轨可 Down 到 TC、Up 回首源轨；结果面板下将目标滚入仍露出的清单。仅在清单内部补末尾滚动余量，关闭归零，普通布局尺寸不变。
- 源行重建按来源/类型/轨道 ID 保留当前选择，替换或移除未选 SC/TC 不跳选外加字幕；换视频先清空旧选择。目录派生通知、固定槽路径刷新、原子修订、焦点回落和可访问名称保持已验证行为。

本次直接改动生产文件为 `SingleMuxView.axaml(.cs)`、`SingleMuxViewModel.cs`；新增有意义的原始按键/绑定回归并更新事实文档。提交还包括此前经审查的 A/B 与布局全部工作区实现、测试及设计/任务文档，使功能形成完整可构建快照。Core、包配置与 v1 无改动，隔离 artifacts/生成物不提交。

## 验证

| 项目 | 结果 | 本次证据 |
|---|---|---|
| 串行 Release 构建 | 0 warnings / 0 errors | `dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false` |
| 常规 Smoke | 26/26 | `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release` |
| A 行为 | 88/88 | `artifacts/single-mux-round1-a/20261010-084806-18e36836da804c58a062e5ea3e248e70/report.json` |
| UI 新布局/原始键盘 | **151/151** | `artifacts/single-mux-round1-b/ui-ffc7c0a9cf694933a8143131838d43e5/report.json` |
| warning-policy | 57/57 | `artifacts/warning-policy/20261010-085329-3c84d166bc604bb8a804c643be3a2d06/report.json` |

UI 新增覆盖双面板完整正向/反向循环、窗口侧栏实际键盘切页/返回、不可见添加/附件、结果操作可达、多轨裁剪与上下跨视口、源视频/音频选中后未选 SC/TC 替换/拒绝/移除、视频更换不继承旧选择。末次代码审查未发现新的具体问题；审查发现的跨视口方向键回退已修复并动态验证。

1040×760/1280×900 多状态截图仍由 UI harness 输出，普通与结果详情已视检；设置底 632、状态顶 644，320 宽/300 高 Overlay、普通 3+2 无整体滚动的断言通过。

既有新布局真实 Core 小 fixture 报告 `artifacts/single-mux-round1-b/real-4c98631aa5d1482a9fe93620b106dca7/report.json` 为 39/39，单 ASS / SC+TC / TC-only 三产物哈希已独立复核匹配。本次不涉及 Core/请求规则，不重复三份封装或完整电影，不把历史真实链路计数冒充本次运行。

此前原复现与两次审查保留在 [首审](single-mux-ui-approved-layout-review.md)、[复审](single-mux-ui-approved-layout-fix-review.md)。中间自动检查 106/106 不覆盖完整导航；新增完整回归已补齐。开发中失败的验证记录保留在隔离 artifacts，不计入最终通过报告。

## 人工边界

Windows 原生选择器、Explorer 拖入、系统剪贴板/资源管理器打开、125%/150% DPI、真实辅助技术、弹出菜单 Esc、播放器渲染仍须交互桌面。代码与 headless 检查通过不等于这些项目通过；阶段一整体人工验收保持未完成。阶段二进程启动/Windows Job 归属竞态仍按 migration 已记录限制处理，本次不扩大范围。
