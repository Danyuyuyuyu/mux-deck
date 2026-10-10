# 已确认布局落地：负责人审查

日期：2026-10-10。结论：布局主体与 B-fix 通过，存在五项已动态复现的 P2，整体暂不验收。下一步仅执行 [修复任务](single-mux-ui-approved-layout-fix.md)，不启动下一功能轮。

审查范围：当前工作区及未跟踪的真实布局代码；基线 `0422069f8f05dac54999c76a4229f2cc8947911d`。依据 [布局合同](single-mux-ui-approved-layout.md)、三张已确认 PNG、适用 Desktop 规范。标准与合同两个只读审查代理并行，负责人独立构建、回归及动态复现；去重后的问题如下。未修改生产代码、提交或推送。

## 确认问题

| 编号 | 轴 | 位置 | 触发与实际影响 |
|---|---|---|---|
| F1 / P2 | 合同、标准 | `SingleMuxViewModel.cs:59–60`；`SingleMuxView.axaml:179,190` | 从空态选择字体/输出目录，计算属性 `HasFontsDirectory/HasOutputDirectory` 已 true，实际“打开”仍 false。setter 未通知派生属性，次级复制菜单同样绑定它们。不能正常使用新目录操作。 |
| F2 / P2 | 标准、合同 | `SingleMuxViewModel.cs:259–273`；`SingleMuxView.axaml:237` | 打开 SC 属性后同槽换文件，固定 ScTrack 引用保留，`InputPath` 已更新，却未通知 `SelectedTrackFilePath`。面板显示旧 ASS 路径，复制 getter 取得新路径，来源展示与实际提交不一致。 |
| F3 / P2 | 合同、标准 | `SingleMuxView.axaml.cs:53–59` | 面板遮挡控制只设置命中测试。轨道 Inspector 获焦后真实 Shift+Tab 到被遮的 `OutputNameInput`，输入 `hidden-edit` 实际改了 `OutputFileName`。结果 Inspector 同样可逆向进入该控件。被遮按钮与行也应核查，指针检查不能证明键盘隔离。 |
| F4 / P2 | 合同 | `SingleMuxView.axaml.cs:198–203` | 打开 SC → 同槽更换（重建行控件）→ 键盘激活 ×。`inspectorTrigger` 为已脱离视觉树的旧按钮，仅判断 visible/enabled 后调用 Focus，忽略失败；实际关闭后焦点为 null，未回到当前 SC 行。移除触发入口须有可用回退。 |
| F5 / P2 | 合同 | `SingleMuxView.axaml:113–140` 与行交互 | 实际 SC 行获焦后发送普通键盘 Down，焦点与选择都仍 SC，不能导航 TC。ItemsControl/Button 未提供方向键导航，框架默认 XYFocus 未启用普通键盘；不满足合同 Tab/方向键操作。 |

F1/F2 是 MVVM 派生展示缺少通知，F3/F4/F5 是键盘合同缺口；不要求改变三张布局。P2 表示下一次交付前需要修复，不推断为成品损坏或覆盖风险。

负责人独立最小复现保存在 `desktop/artifacts/approved-layout-review/`：`ReviewCheck.csproj`、`Program.cs`、`reproduction.json`。只启动真实 Avalonia headless 窗口，初始入口用 Focus+Space，导航使用实际原始 Shift+Tab / Down，隐藏字段编辑使用 KeyTextInput；未改生产代码、未执行外部打开/复制。字幕路径展示复现通过公开 setter 隔离派生通知，现有生产同槽 Commit 使用同一 SetSubtitlePath；修复验收还需走 Prepare/确认/Commit 或真实替换控件链。

## 通过的部分与证据边界

1040×760 的普通、轨道属性、结果详情原生截图已视检：普通 3+2 无主页面滚动，两个 Overlay 保持设置与状态位置，320 宽/300 高，清单内部滚动成立。设置 121 高和清单预算的轻微偏差可接受，不要求为 3px 重做布局。源包含只读投影、菜单禁用音频、面板互斥和实际 peer 名称均成立。

SC/TC 实际 peer 名称已可辨认，原 B-fix 名称缺陷关闭；新问题不否定此项修复。成品探测绑定成功 Result 输出快照、保护迟到回写、关闭等待均未发现新的缺陷；不要求重构 Core 或引入结构化 QC。

负责人本次实际执行：

| 检查 | 结果 | 证据 |
|---|---|---|
| Release 构建 | 0 warnings / 0 errors | 标准串行 `dotnet build MuxDeck.sln --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false` |
| 常规 Smoke | 26/26 | `dotnet run --project tests/MuxDeck.Smoke --no-build -c Release` |
| A 回归 | 88/88 | `artifacts/single-mux-round1-a/20261010-081758-ff554065b74140d68d4244747fa54caf/report.json` |
| 当前 UI harness | 85/85 | `artifacts/single-mux-round1-b/ui-30e9ad496f294274ac26759cdb112fdf/report.json` |
| warning-policy | 57/57 | `artifacts/warning-policy/20261010-081840-9d000a6c6f2b442aa31dca19954f32c5/report.json` |
| 五项定向复现 | 缺陷成立 | `artifacts/approved-layout-review/reproduction.json` |
| `git diff --check` | exit 0 | 只有已有 LF→CRLF 提示 |

UI 85/85 不覆盖本报告的真实 Tab 遍历、方向键、目录动作启用及同槽后路径刷新；修复时补行为断言，不把已有绿色计数当作这些场景通过。

执行者真实 Core 报告 `artifacts/single-mux-round1-b/real-4c98631aa5d1482a9fe93620b106dca7/report.json` 为 39/39。负责人读取了报告并独立计算三份成品 SHA-256，均匹配；记录的实际轨道/附件总数分别为 single-ass 3/4、sc-tc 4/8、tc-only 3/4。本次未重跑真实 Core，不把证据读取当作负责人独立执行 39/39。

原生 Windows 选择器、Explorer 拖入、系统剪贴板/目录打开、125%/150% DPI、弹出菜单 Esc、真实辅助技术和播放器仍待人工。F3/F4/F5 已能在 headless 真实键盘事件下复现，须先修复，不能只归为待人工。第一轮及 migration 完成状态暂不标通过。
