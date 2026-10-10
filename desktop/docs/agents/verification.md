# 验证要求

本文定义按改动选择验证及功能完成的证据。常规命令以 [README 的验证章节](../../README.md#验证)为准，项目与包版本以配置和锁文件为准。

## 按改动选择

| 改动 | 必要验证 |
|---|---|
| 仅文档 | 链接存在、规则无冲突、阶段事实一致、规范可纳入 Git |
| 纯逻辑、命令参数、校验规则 | Release 构建和涉及的行为用例，覆盖有效输入与关键反例 |
| 进程执行、编码、超时或取消 | 真实子进程验证参数、输出、退出码和结束行为；涉及工具后代进程时验证进程树 |
| ViewModel、绑定、导航或布局 | Release 构建、相关 UI 冒烟；布局改动渲染检查，原生选择器人工操作 |
| 子集化、封装、产物或落位 | 自动化检查加真实工具和隔离素材的端到端验证 |
| 外部工具输出、告警或诊断策略 | 读取[工具诊断分类](diagnostics.md)，运行 `--warning-policy` 公共服务回归；新增真实工具放行项时补真实 fixture 证据 |
| 批量、持久化或恢复 | 单任务回归，加混合结果、取消、重跑、冲突或重启场景 |

用例验证可观察行为，不以复制实现步骤的断言充当验收。检查通过后，仅在新改动、失败或未解决疑点需要时扩大或重复验证。

## 当前自动化能力

截至 2026-10-10，Release 构建为 0 warnings / 0 errors，`tests/MuxDeck.Smoke` 为 26/26；`--warning-policy` 为 57/57，报告见[告警策略回归](../../artifacts/warning-policy/20261010-020043-2d4a39abbdd94856927596db2dd90bc3/report.json)。其 fake runner 走公开 `SingleMuxService.RunAsync`，覆盖颜色告警、字体提示、失败阻断、UI 多 message 展示，以及 ASS transport Comment 重排通过、Dialogue 文本/时序/Style/字体/顺序和文本末尾空格变化拒绝。合成粗体真实 Regular-only fixture 的历史报告为[8/8](../../artifacts/warning-policy/real-2f43ddd6a3d744d081850c7a3fcf08ca/report.json)。

独立 `--integration` 使用真实 ffmpeg、mkvmerge、mkvextract、AssFontSubset/PyFontTools 与隔离素材。最新真实工具链报告 121/121 通过，包含 MKV/MP4、单/双/TC-only、逐轨旗标、字体附件/字形、冲突、安装竞态、提交后取消、源哈希、IETF QC、进程取消、渲染日志和 ASS transport 参考比较，见[2026-10-10 集成报告](../../artifacts/phase1/20261010-015647-daefe8254a624279bdb71f726b161b95/report.json)。`--ui-integration` 通过真实 MainWindow、ViewModel 与 Core 执行单 ASS、SC+TC、禁用 SC 的 TC-only，并核对结果详情成品概览、目标冲突与输入哈希；`--single-mux-ui-round1` 以新布局真实控件、生产分配窗口和模拟 DragDrop 核验面板开关/互斥/焦点/几何与真实可访问名称。两者当前运行的实际计数以上次执行输出为准；headless 不替代原生桌面与 DPI 验收。

进程树证据只覆盖已归属到 Windows Job Object 的工具及受控父/子进程、取消和管道收尾。`Process.Start()` 与后续 Assign Job 非原子，未验证极快 launcher 在归属前创建后代或退出；该边界列为阶段二 P2，不据此宣称通用进程树保证。

## 工作台重构验证（2026-10-10）

本轮 Release 构建 0 warnings / 0 errors，Smoke 26/26，输入接纳 88/88；UI 回归已扩展为 [174/174](../../artifacts/single-mux-round1-b/ui-628726c3b7b648788736da8e1c4cbc50/report.json)，新增源/外加共享列宽与运行中目录清空守卫检查。[工作台检查 38/38](../../artifacts/workbench-ui/0bf62a0b2f5349549d0b5ae1f169c341/report.json)覆盖数字显示 ID、添加顺序、空名称、标记更新、真实指针拖动、键盘调整、最小列宽、水平滚动与头行对齐、偏好独立读取/损坏回退/失败、菜单打开/清空和关闭时保存未结束的调整。

`--workbench-ui` 使用 artifacts 内注入配置，不写真实用户偏好；新窗口读取配置模拟重启，关闭钩子在保存完成后关闭。两尺寸截图已检查。Core、工具参数和包版本未改动，本轮未重放真实工具链；上述为 headless 与受控交互证据，原生选择器、Explorer、DPI、辅助技术与播放器仍需人工验证。

## 自动拖放与多轨封装（2026-10-10）

本次范围见[自动拖放任务](../tasks/automatic-media-drop.md)。Release 构建 0 warnings / 0 errors；自动识别 UI [25/25](../../artifacts/automatic-drop/ui-51fcba9c564d4efb8d02b66e8f59bfd5/report.json)，覆盖文件附带文本/URL、混合与跨区拖入、全批次拒绝、视频替换取消、真实输入 Track ID、多音轨、字体归位、关闭/迟到取消，以及更换音轨时保持编辑属性和选择。现有 UI [174/174](../../artifacts/single-mux-round1-b/ui-21d83e44c80f4a4db19963b21ef5eadf/report.json)、工作台 [38/38](../../artifacts/workbench-ui/f32c1eb79e4748c684f42516443e2f8b/report.json)、旧接纳 [88/88](../../artifacts/single-mux-round1-a/20261010-135849-5a103263eeec4f5ea82afc16c536462c/report.json)、告警策略 [57/57](../../artifacts/warning-policy/20261010-135849-d37555cda70741348718a6358494056b/report.json)、Smoke 26/26 通过。

真实新能力 [37/37](../../artifacts/automatic-drop/real-44b11257e18b44d787668cd75072a302/report.json) 覆盖 SRT-only、audio-only、多音轨 MKA、多 SRT、三份 ASS、ASS+SRT+audio；核验输出旗标、字幕全文、音频 fullraw 哈希、字体条件、目标冲突与源文件完整性，篡改新增音频语言或数据均阻止发布。既有完整真实工具链 [121/121](../../artifacts/phase1/20261010-141429-b8705af996324afcb8da8c733702cb1f/report.json) 和真实 UI [39/39](../../artifacts/single-mux-round1-b/real-f8c333122b954573a888ae6911cb7020/report.json) 亦重放通过。MP4 未声明时长时不使用字幕尾时间补值，避免新轨 QC 错误拒绝。

本轮改动包含 Core 参数和 QC，不适用上方工作台纯视觉轮的“无 Core 变更”说明。字幕扩展范围为 UTF-8 ASS/SRT；原生 Explorer 拖放、系统选择器、DPI、辅助技术和播放器仍需人工验收，模拟事件与真实封装证据不替代这些检查。

## 全窗口输入反馈（2026-10-10）

本次仅改 Desktop 反馈与窗口拖放宿主，Core/工具参数及依赖未改；任务见[窗口输入反馈](../tasks/window-input-feedback.md)。Release 构建 0 warnings / 0 errors；[新增反馈检查 24/24](../../artifacts/input-feedback/6029c2c3e07c4257a5a79efcf4b79e04/report.json) 覆盖侧栏拖放、全客户区尺寸/无指针拦截、跨控件无闪烁、离开/Drop/Esc/隐藏/关闭清理、真实 owned modal 遮挡、显式通知类型、成功五秒和普通四秒计时、错误按钮关闭、相同通知去重、实际旧计时不影响新错误、两尺寸长文本/属性栏避让及底部任务状态。计时用完整 Dispatcher 循环验证，不能只用 RunJobs 将“未计时”误报为通过。

现有 [UI 174/174](../../artifacts/single-mux-round1-b/ui-90d1ac91cc3f4e9fb8f974b6813995fb/report.json)、[自动导入 25/25](../../artifacts/automatic-drop/ui-94dd300517d948dab48084ee75819a90/report.json)、[工作台 38/38](../../artifacts/workbench-ui/9caec66d6fd14a8eb3f084139c086682/report.json)、[旧接纳 88/88](../../artifacts/single-mux-round1-a/20261010-150629-4f83f9b25d1542dca99ba15c48f650b8/report.json) 及 Smoke 26/26 通过。检查并保留[遮罩](../../artifacts/input-feedback/6029c2c3e07c4257a5a79efcf4b79e04/overlay-1280.png)、[成功通知](../../artifacts/input-feedback/6029c2c3e07c4257a5a79efcf4b79e04/success-1280.png) 与两尺寸属性栏/长通知截图。此轮未重放 Core 工具链，原生 Explorer、系统标题栏、DPI 及辅助技术仍需人工验收。

## 单个封装验收矩阵

| 场景 | 预期 |
|---|---|
| 单份 ASS | 子集化产物完整；成品包含正确字幕与字体；播放器渲染正确 |
| 简繁双 ASS | 两条新增字幕轨正确，语言与 default/forced 逐轨符合任务设置 |
| 中文、空格和特殊字符路径 | 输入、工具参数和产物位置保持正确 |
| 缺字体、缺字形、字体 face 兼容 | 缺字体/字形仍明确失败且不发布；只有 diagnostics.md 中的唯一 Regular 合成粗体，或具有完整/PostScript source provenance 的 native bold face 才能返回 Core 提示。generic family/未知替代仍阻断；AFS 工具白名单仅精确颜色告警 |
| 子集化失败或产物不全 | 不执行后续封装，不发布成品 |
| 封装失败或成品校验失败 | 明确失败，不发布最终成品 |
| 子集化或封装中取消 | 当前相关进程退出，不再执行后续阶段，不报告成功 |
| 目标已存在或落位时出现目标 | 拒绝覆盖，目标文件保持完整 |
| 任何失败或取消 | 源视频、字幕与字体保持完整，临时资源按任务归属处理 |

ASS 内容 QC 的唯一规则见[代码骨架](skeleton.md#职责与落位)：用同一 MKVToolNix 对修正 ASS 生成字幕参考并与成品提取全文比较，仅统一 CRLF/LF。2026-10-10 完整电影重放 9/9 通过，见[真实电影报告](../../artifacts/warning-policy/real-e223f0011b774942bf40123b7b1c76a3/report.json)。旧[失败报告](../../artifacts/warning-policy/real-52d8443120a74073b2438784789bc798/report.json)因 MKVToolNix 移动四条 Comment 到 `[Events]` 的 `Format` 行后而拒绝；该差异现由同工具参考修复。真实播放器与原生选择器仍待人工检查。

## 素材与结果

- 使用可生成或专门准备的小素材，在本次验证专属目录中运行；不对生产素材执行替换或删除。
- 新增验证产物放在 `desktop/artifacts/` 的专属子目录，或系统临时目录；不写入 v1 配置、历史与任务目录。
- 清理仅针对本次验证创建的目录；涉及文件变更的用例检查源文件和已有目标仍完整。
- 报告工具、输入场景、成品与日志位置及结果；机器绝对路径写入本地报告，长期规范使用相对路径。
- 真实封装首次验收与字体处理变化后，人工播放检查代表性字幕，包括中英文、粗斜体和 ASS 字体覆盖样式。

## 完成标准

阶段验收需同时满足当前 [开发流程](migration.md)的范围、相关自动化检查和真实场景预期。工具缺失或执行环境受限时保存已完成工作，明确未验证部分，阶段保持未验收。
