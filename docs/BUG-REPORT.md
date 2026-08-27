# mux-deck Bug 审查总报告

> ## ✅ 终版状态：全部 45 项已修复并验收通过
>
> **验收方式**：python -m py_compile（server.py，含两轮补丁后 exit=0）；PowerShell Parser（两个 .ps1，0 错误）；node --check（index.html 脚本段，node v24 两轮）；bat 逐行复核 + 关键分支 cmd /c 实跑。跨文件契约（音轨正交协议 A1/A2/A3）由主审在三个文件中逐行比对确认闭环。
>
> **验收期间追加的三处补丁**（子代理修复之外）：
> 1. ass_mux_manual.ps1 H2：回滚失败分支的暂存原件在 $tmp 内会被 Fail() 清理删除——补救援到视频旁 `.restore_failed` 后缀文件，救不动则保住临时目录。
> 2. server.py C2：同名拦截补大小写归一（Windows 文件系统大小写不敏感，`A.mkv`/`a.mkv` 也要拦）。
> 3. **start_mux_ui.bat / install_autostart.bat（用户实跑发现的回归）**：括号块内 echo 文本含未转义 `)`（`(check "Add to PATH")`），右括号提前闭合 `if` 块，cmd 报「此时不应有 or。」并中止批处理。两文件均已改写为无括号文案，并用真实块结构做了解析回归（REACHED_END，exit 0）。教训：bat 的逐行复核必须警惕括号块内的 `)`，子代理的静态复核漏掉了这一 cmd 解析陷阱。
> 4. **index.html init 崩溃（用户拖放实跑发现的存量 bug，2026-08-27 13:28 版重构引入，非本次修复产生但审查亦未覆盖）**：静态 `#videoCard` 是空 div，`#video` 输入框由 `renderVideoCard()` 自己创建，但函数开头就先读 `$('video').value` ——鸡生蛋，页面加载即抛 TypeError（init 处），拖放识别成功返回后填充也抛（`$('video').value = ...`），表现为「长时间识别不出」。已修为 null 安全（`($('video') && $('video').value || '').trim()`）并通过 node --check。教训：node --check 只验语法不验运行时；init 顺序的 DOM 依赖需要真实打开页面冒烟。另注：拖放「长时间」本身是 `resolve_drop` 未命中即全量重建 D:\Video 索引的既有设计（本次未改语义）。
> 5. **renderVideoCard 重建丢值（上条的连环坑，同版引入）**：else 分支重建卡片时新造的 `#video` 不带值——拖放/浏览填值 → change → 重渲染 → 值蒸发，表现为「查看轨道提示请选择视频、粘性条显示尚未选择视频，但卡片外观有路径」。已修：重建后回填 `$('video').value = v`。
> 6. **文件浏览器困在 D 盘 + 不记忆目录（用户要求）**：「上层」到 `D:\` 后被 `|| 'D:\'` 钉死，永远到不了盘符列表（服务端 `list_dir("")` 本就返回 drives）。已修：盘根再向上进入盘符列表，可切 E: 等任意盘；起始目录改为「字段当前值 → 上次浏览目录（localStorage）→ 工作目录」，每次成功浏览记住目录。
> 7. **第二轮用户需求（4 项）**：① 目录记忆分槽位（fonts/out/video/sub/audio 各自独立 localStorage 键）；② 封装/批量日志乱码根修——mkvmerge 管道输出 UTF-8，经 PS 5.1 按 GBK 解码再编码即成「姝ｅ湪鍐欏叆」，两个 PS1 头部统一设置 `[Console]::Input/OutputEncoding = UTF-8`（提取不走 PS 层所以正常，佐证诊断）；③ 工作目录（扫描根）可设定：`config.json` 持久化 + `/api/config` 读写 + 高级选项 UI，保存后索引失效重建，并作为浏览器默认起点（CONTEXT.md 扫描根术语同步）；④ 提取文件名以语言结尾：前端传 lang，服务端命名 `{stem}_track{id}.{lang}.{ext}`（语言栏提取表本就存在）。验收：py_compile / node --check / 双 PS1 Parser 全绿（subset 恢复 BOM 后 0 错误——edit 工具会剥 BOM，改该文件后需重新补 BOM）。
> 8. **预览支持内封字幕轨（用户需求）**：字幕下拉原本只有 SC/TC/自定义路径，读不到视频内封轨。已实现：「读取内封轨道」按钮探测并填充下拉（含语言/名称，视频变更自动失效）；服务端对 `track:<id>:<ext>` 先用 mkvextract 抽轨、再抽视频自带字体附件作 fontsdir（无附件且无字体目录时回落系统字体），走既有渲染管线；PGS 明确提示不可渲染并指引走字幕提取。cleaner 增加对预览 `*_fonts` 目录的回收。
> 9. **拖放识别增强（用户选型 Q1+Q2，未做 Q3 路径记忆）**：① 索引条目升级为 {path, size}（构建时 stat）；② 拖放命中判定：单候选直用 → 多候选按拖入文件大小（DataTransfer 原生提供）唯一匹配 → 仍歧义则弹出候选选择器（路径+大小，逐文件单选）；③ 不再静默取第一个候选，杜绝「E 盘文件显示成 D 盘路径」类事故；④ 兼容旧服务端字符串索引的过渡防护。Q3（basename 路径记忆）与 Q4（多根目录）未做。
> 11. **【重大】init 块 JS 崩溃（用户控制台报 `Cannot set properties of null (setting 'onclick')`，L1951）**：初始启动设置弹层的 HTML 被插到 `</script>` 之后——脚本同步执行 `$('btnSetupBrowse').onclick` 时该元素尚未进入 DOM，抛 TypeError 使**整个 init 块终止**，其后的 btnCfgScan 接线、config 预填、/api/version 状态检查与自动重连探测、renderVideoCard **全部不执行**。这一根因串联解释了用户连续报告的三个症状：右上角「连接中」、拖放「长时间无法识别」、工作目录「浏览失效」。修复：弹层 HTML 移到 `<script>` 之前；showBrowser 的 /api/list 加 try/catch 错误可见化。教训：**单文件前端的静态 HTML 必须放在 `<script>` 之前**，node --check 只验语法验不出 DOM 顺序问题。
>
> 10. **零安装便携打包（用户需求：拷贝即用，新电脑不装任何软件）**：① 目录结构 `runtime/python`（Python 3.12.8 embeddable，35 文件含 pythonw）+ `tools/mkvtoolnix`（mkvmerge/mkvextract 便携 zip）+ `tools/ffmpeg`（gyan essentials 9.0.1，含 libass，已验证 ass 滤镜存在）+ 既有 `assfonts/`；② 回落链改造：server.py 常量 `_first_existing(自带路径) → which → 硬编码`（mkvextract 补上了此前缺的 which 回落；ffmpeg 由纯 which 改为自带优先），ass_mux_manual.ps1 与 ass_subset_mux.ps1 的 mkvmerge/mkvextract 查找加自带路径优先，start/install 两个 bat 的 python/pythonw 探测加 `runtime\python` 优先；③ 新增「环境自检.bat」逐项检查五依赖与端口；④ 踩坑记录：python.org 3.15.0/3.12.14 的 embeddable 直链 404（该时间线仅部分版本可用，3.12.8 稳定）；MKVToolNix releases.json 为嵌套 dict 且不含直链（需按 `windows/releases/{ver}/mkvtoolnix-{ver}-win64.zip` 拼）；沙箱会话 schannel 拿不到凭据须 full-access 直连（现策略下已正常）；edit 工具剥 BOM 的老问题再次出现。
>
> **已知残余**（记录在案，不构成缺陷）：
> - install_autostart.bat：`%PYW%` 解析为 `pyw -3` 且 server.py 路径含空格的组合下，生成的 VBS 命令行参数拆分有歧义（当前部署路径无空格）。
> - 备份序号风格不一致：manual 路径用 `EP01.1.mkv`，subset 路径用 `EP01_1.mkv`（纯外观差异）。
> - E1 歧义判定的量化阈值：big5 侧繁体特征字 ≥3 次且 ≥ gbk 侧 2 倍（裁决只定了方向，阈值为实现选择）。
> - MKVEXTRACT 常量仍为硬编码（裁决仅点名 mkvmerge/assfonts，二者已有 which 回落）。

审查方式：三路并行深读（前端 / server.py / CLI 脚本），关键结论由主审逐行核对，部分条目经实机验证（mkvmerge v100 + PowerShell 5.1）。
分级标注：**严重度**（高=错误结果/丢数据/崩溃；中=边界条件或资源问题；低=健壮性）× **置信度**（确定/很可能/可疑）× **核验**（亲核/实测/静态）。

修复必须遵守文末「裁决记录」（Q7–Q17 已由用户拍板）。

---

## 第一栏：Web UI 路径（server.py + index.html + ass_mux_manual.ps1 共用链路）

### 音频语义（Q7A 正交化，三条一起修）

- **A1** 部分勾选音轨 + 外部音轨 → 外部轨被静默丢弃
  位置 ass_mux_manual.ps1 L175-177（elseif 互斥）/ index.html L1240 / server.py L248 | 高/确定/亲核
  触发：查看轨道→取消勾选部分音轨→④填外部音轨→封装。输出只有勾选的源轨，外部轨消失，无报错。
  修复：PS1 去互斥；`-AudioTracks` 只决定源轨去留，外部音轨恒为独立第二输入追加。

- **A2** 全不选 + 外部音轨 → 命令行出现重复 `-Audio`，PowerShell 绑定必败
  server.py L249-251 / index.html L1239 | 高/确定/亲核
  触发：①全不选 + ④外部音轨 → 任务报错。
  修复：正交化后 `audio_mode:'none'` 只翻译为源轨 `--no-audio`，外部轨独立追加，前端不再同发冲突字段。

- **A3** keep_src_audio 硬编码 true，协议里名存实亡
  index.html L1243 | 低/确定/亲核
  修复（Q9A）：随 Q7A 一并删除/固定语义，源轨取舍归 ①。

### 任务生命周期与并发（server.py）

- **G1** run_to_file 超时不杀进程：UI 已判失败，子进程继续完成封装并落盘
  server.py L63-69 | 高/确定/亲核
  触发：mkvmerge/assfonts/ffmpeg 卡死超过超时。迟到副作用 + 与新任务并发写同一路径。
  修复：捕获 TimeoutExpired 后 `taskkill /T /F` 进程树，状态标 timeout。

- **G2** 串行检查 TOCTOU：并发提交可双跑，「批量串行」承诺失效
  server.py L273-289（extract_subs 同型）| 高/确定/亲核（结构）
  修复：检查+登记+线程启动放进同一 JOBS_LOCK 区间。

- **G3** probe 共用 probe_tmp.json，并发探测互踩/读半截
  server.py L95-106 | 中/确定/亲核
  修复：每请求唯一临时文件名。

- **G4** prep_subs 固定文件名 sc/tc_converted.ass，并发覆盖张冠李戴
  server.py L157 | 中/可疑/静态
  修复：唯一文件名（uuid 或内容哈希）。

- **G5** stop 与 state["proc"] 赋值存在窗口期，极快停止无效
  server.py L66-67 vs L580+ | 中/可疑/静态
  修复：Popen 前预置哨兵/在锁内完成登记。

- **G6** 历史状态靠日志文本扫描：killed / 提取失败被标 done
  server.py L529-535 | 中/很可能/亲核
  修复：worker 落盘 state.json 终态，history 读它而非扫日志。

- **G7** 文件索引并发重建互相覆盖
  server.py L363-389 | 低/可疑/静态
  修复：索引重建加锁或版本号防回退。

### 落位与备份（ass_mux_manual.ps1）

- **H1** 二次封装覆盖真原片：备份目录同名 -Force 覆盖，原始版本丢失
  ass_mux_manual.ps1 L265-267 | 高/确定/亲核+实测
  修复（Q17A）：同名冲突自动加序号/时间戳，永不覆盖已有备份。

- **H2** -NoBackup 先删原片后落位，落位失败即丢片
  ass_mux_manual.ps1 L260-263 | 高/确定/亲核
  修复：原片先移入暂存→输出落位→成功后再删暂存。

- **H3** 备份分支第二步失败：原位空缺、成品滞留 TEMP，无回滚无指引
  ass_mux_manual.ps1 L267-268 | 中/很可能/亲核
  修复：失败时回滚（备份移回原位），FAIL 文案给出恢复指引。

- **H4** 失败路径不清理 %TEMP%\manual_mux_*
  ass_mux_manual.ps1 L104 vs L272 | 低/确定/静态
  修复：Fail() 内清理 $tmp。

- **H5** 输出校验按逗号拆串猜轨数，生产日志已有误报现场
  ass_mux_manual.ps1 L236-239 | 低/可疑/亲核（jobs/d8e3ef9f7ffb 有实样）
  修复：用 probe JSON 的实际轨数校验，不猜。

### 字体与编码（Q11A/Q12A 已裁决）

- **D2** 字体体检漏报：空名 Missing font 被丢弃、Missing codepoints 整行不匹配、rc 不查、日志截尾
  server.py L186-196 | 高/确定（机制）/亲核+仓库日志实锤（tmp/fontcheck_*/sub.log）
  触发：字幕引用缺失字体或缺字形时显示「字体齐全」。**用户已实际中招。**
  修复（Q12A）：两类都报（「未命名字体缺字形」「某字体缺 N 码点」）+ 检查 rc + 全量读日志。

- **D1** 无字幕封装：源字幕保留但字体全丢，弹窗却承诺「重新嵌入字体」
  index.html L1247 + PS1 L148/L205 | 中/确定/亲核
  修复（Q8A）：无外部字幕时自动保留源附件，文案改如实。

- **E1** BIG5 被 GBK 静默误判，转码副本成乱码
  server.py L127-133 | 中/很可能/实测（52 常用繁体字 41 个可被 GBK 解码）
  修复（Q11A）：GBK 命中后再试 BIG5，分歧时按繁体特征字判定，无把握则前端警告人工确认。

### 预览（Q14A 已裁决）

- **F1** 跨盘 os.path.relpath 抛 ValueError，连接直接断、无 JSON 响应
  server.py L217（do_POST L677-713 无包裹）| 高/确定/亲核+实测
  触发：本副本（E:）+ 字体目录在 D: → 任何一次字幕预览。
  修复：try 包裹返回错误 JSON；或改用绝对路径 fontsdir。

- **F2** SRT 直接按 ASS 渲染：黑帧假成功
  server.py L214-218 | 中/可疑/静态
  修复（Q14A）：预览前 ffmpeg 转 ASS（默认样式）再渲染。

- **F3** subtitle 模式 -ss 作用于 d=10 的 color 源，t>10 黑帧
  server.py L221-222 | 低/可疑/亲核
  修复：源时长加大或 -ss 后置。

- **F4** 时间点非数值静默按 0 渲染
  index.html L1193/L1212 + server.py L696 | 低/确定/亲核
  修复：前端校验 + NaN 防护。

### 批量

- **C1** 批量手输视频路径不写回 batchItems，重绘即被清空
  index.html L1303-1311（对照 L1317 batchBrowse 有写回）| 中/确定/亲核
  修复：change 处理器补 `batchItems[i].video = v`。

- **C2** 批量 + 公共 out_dir 同名输出互相覆盖，两条都记 ok
  server.py L300-302 + PS1 L253-257 | 中/确定/亲核（-Force）
  修复（Q13A）：提交时校验冲突，拒绝并列出冲突项。

- **C3** btnMatchAll 的 Promise.all 无失败处理，卡「正在匹配」
  index.html L1325-1331 | 低/可疑/静态
  修复：catch + 状态复位。

### 前端状态与健壮性

- **B1** probe 乱序：换视频后旧响应覆盖 trackSel，轨道操作作用于错误视频
  index.html L1126-1147 | 中/很可能/亲核（结构）
  修复：响应到达时校验 `$('video').value === v` 才应用。

- **B2** SC/TC 徽章静态，只填繁体时界面与实际封装结果相反
  index.html L525/L545 | 低/确定/亲核
  修复（Q10）：随输入动态切换徽章。

- **B3** xtTracks 不随 x_video 失效，换视频后用旧轨道 ID 提取
  index.html L1528-1556 | 中/确定/静态
  修复：x_video 变更时清空/重新探测。

- **J1** 四个按钮 disabled 后裸 await，异常即永久锁死
  index.html L1153/L1174/L1196/L1214 | 中/确定/亲核
  修复：try/finally。

- **J2** 三个轮询回调无异常保护：服务器重启后无限重试、状态锁死
  index.html L1256-1280/L1376-1394/L1559-1573 | 中/确定/亲核
  修复：try/catch + 连续失败计数复位 UI。

- **J3** 提取轮询缺 killed 分支，按钮永久禁用
  index.html L1559-1573 | 中/很可能/静态
  修复：补 killed 分支。

- **J4** 任务结束粘性条不刷新，残留进度文案
  index.html done/error/killed 分支 | 低/确定/静态
  修复：终态调 refreshSticky() 并隐藏进度条。

- **J5** openDir 拼接 encodeURIComponent 不转义单引号，路径含 ' 时 onclick 断裂
  index.html L1265/L1385/L1522 | 低/很可能/静态
  修复：改 data 属性 + addEventListener。

- **J6** 拖放与浏览弹窗竞态覆盖、无 sc/tc 标识的 .ass 一律落简体槽无提示
  index.html L1401-1457 | 低/可疑/静态
  修复：弹窗打开时拒绝拖放；无标识落槽时提示。

### 资源与杂项

- **I1** tmp 目录从不清理（fontcheck_* 子集字体、converted.ass 无限累积）
  server.py L723-737 | 中/确定/亲核
  修复：cleaner 加 TMP_DIR（含 fontcheck_* 子目录）。

- **I2** 索引按目录名跳过，误伤用户自己的 jobs/tmp/previews 目录
  server.py L368 | 低/确定/静态
  修复（Q15A）：只按绝对路径跳过工具自身目录。

- **I3** 接口类型校验缺失：add() 对非字符串 .strip() 崩、float(time) 裸炸
  server.py L239-241/L696 | 低/可疑/亲核
  修复：入口处类型清洗。

- **I4** 单实例探测把任何 200 当「已在运行」（server main + start_mux_ui.bat 两处）
  server.py L739-746；start_mux_ui.bat L7-13 | 低/确定/亲核
  修复：校验 /api/version 返回 JSON 体。

- **I5** mkvmerge/assfonts/python 路径硬编码
  PS1 L101、server.py L14-15、start_mux_ui.bat L16、install_autostart.bat L6 | 低/确定/静态
  修复：shutil.which / py -3 回落链。

### 安全（Q16A 已裁决：顺手加）

- **L1** 无 Origin/CSRF 校验：恶意网页可驱动 mux/stop/open/枚举目录
  server.py do_GET/do_POST | 中/确定/亲核（/api/file 有 basename+前缀校验，不受影响）
  修复：校验 Origin/Referer（同源或空）再处理写操作。

- **L2** jid 无校验直接拼路径，可越出 jobs 目录读日志
  server.py L542-561 | 低/确定/亲核
  修复：`^[0-9a-f]{12}$` 白名单。

---

## 第二栏：CLI 遗留路径（低优先，按 Q1 只记录/低优先处理）

- **K1** 纯繁体时繁体不是默认轨（违反术语，与共用路径分叉）
  ass_subset_mux.ps1 L112-115 | 高/确定/亲核+实测
  修复：对齐 `if (-not $sc -and $tc) { tcDef=0:1 }`。

- **K2** 替换=直接删原片、无备份目录、先删后移
  ass_subset_mux.ps1 L131-134 | 高/确定/亲核
  修复：对齐「替换」术语（备份 + 安全顺序）。

- **K3** OutDir 扁平命名同名覆盖 | 中/很可能/亲核
- **K4** assfonts 建库失败不查退出码，误报为「缺字体」 | 中/确定/实测（退出码 105）
- **K5** 子集字体不按扩展名过滤，可能嵌错文件 | 中/可疑/静态
- **K6** --track-order 冗余假设（当前实测无害） | 低/确定/实测
- **K7** -eq 与 -match 大小写逻辑不自洽 | 低/可疑/静态
- **K8** .bat 中文双重编码乱码，删原片警告不可读 | 低/确定/实测
- **K9** .bat 失败仍显示「处理完成」 | 低/确定/静态
- **K10** start_mux_ui.bat Python 单用户路径硬编码 | 中/确定/静态
- **K11** start_mux_ui.bat 探测任何响应都算已运行 | 中/很可能/静态
- **K12** install_autostart.bat pythonw 硬编码 + 静默失败 | 中/确定/静态
- **K13** VBS echo 拼接对特殊字符脆弱 | 低/可疑/静态
- **K14** CLI 路径无编码转换：GBK 字幕直接封装有乱码风险（与 UI 流程不一致） | 中/很可能/静态

---

## 裁决记录（修复必须遵守）

| # | 裁决 |
|---|---|
| Q7A | 音轨正交组合：① 决定源轨去留，④ 外部轨恒追加 |
| Q8A | 无外部字幕时自动保留源附件 |
| Q9A | 「保留源音轨开关」废除，源轨取舍归 ① |
| Q10 | SC/TC 徽章随输入动态切换 |
| Q11A | GBK/BIG5 分歧按繁体特征字判定，无把握警告人工确认 |
| Q12A | 缺字形也算体检不通过 |
| Q13A | 批量输出同名冲突：提交时拒绝并列出 |
| Q14A | SRT 预览前转 ASS |
| Q15A | 索引只按绝对路径跳过工具自身目录 |
| Q16A | 加 Origin 校验 + jid 白名单 |
| Q17A | 备份永不覆盖：同名加序号/时间戳 |

其他总则：全读分栏（CLI 低优先）、三级标注、静态为主高危才实测、先报告后修、矛盾逐条裁决（已全部裁决完毕）。
