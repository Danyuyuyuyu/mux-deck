# Mux Deck · 字幕封装助手

用 mkvmerge 把 视频 + ASS 字幕 + 字体附件 合成 MKV 的本地工具：字体子集化（assfonts）、字体体检、编码转换、预览帧、批量队列、内封轨道预览与提取，全部在浏览器界面完成。

**零安装便携**：整个文件夹拷走即用（Windows 10/11，无需安装任何软件）。无 pip 依赖，`server.py` 纯 Python 标准库。

## 快速开始

```powershell
# 1. 首次使用（或 bin/ 为空时）拉取便携运行时：
powershell -ExecutionPolicy Bypass -File scripts\bootstrap.ps1
#    需要代理： powershell -ExecutionPolicy Bypass -File scripts\bootstrap.ps1 -Proxy http://127.0.0.1:7890
#    或直接双击 安装环境.bat（连 python 一行都不用敲）

# 2. 自检依赖（可选）：
环境自检.bat

# 3. 启动：
start_mux_ui.bat            # 浏览器自动打开 http://127.0.0.1:8765
```

系统已装 Python / MKVToolNix / ffmpeg 时也可以不跑 bootstrap——程序按「自带 bin\ → 系统 PATH → 默认安装路径」自动回落。

## 功能

- **封装 (mux)**：mkvmerge 合成 MKV；简体存在时简体为默认轨道，仅给繁体时繁体自动默认
- **字体子集化**：assfonts 提取实际用到的字形嵌入，保证播放器不缺字
- **字体体检**：封装前检查字体缺失（含「字体存在但缺字形」），不再封装到一半才发现
- **编码转换**：自动检测 GBK / BIG5 / UTF-16 并生成 UTF-8 副本（不改原文件）；歧义时按简/繁槽位判定
- **预览帧**：ffmpeg + libass 渲染指定时间点；支持外部字幕（SRT 自动转 ASS）与**内封字幕轨**（自动抽取轨道与视频自带字体附件）
- **轨道选择**：① 查看轨道 勾选源音轨/源字幕轨/保留附件；④ 外部音轨与源音轨**正交组合**
- **批量封装**：串行队列、按集数自动匹配字幕（命中按简/繁汇报）、输出同名冲突拦截、无字幕项开始前确认
- **字幕提取**：mkvextract 提取，文件名以语言结尾（`EP01_track3.zh-Hans.ass`）
- **拖放识别**：按文件名 + 文件大小双匹配定位，歧义时弹出候选选择器（不再静默张冠李戴）

## 目录结构

```
mux-deck/
├── app/                                       # 服务端 + 前端 + 用户配置（server.py / index.html / config.json）
├── start_mux_ui.bat / install_autostart.bat / 安装环境.bat / 环境自检.bat   # 操作入口（根目录，双击即用）
├── scripts/                                   # 内部脚本：bootstrap、子集 CLI（ass_mux_manual/ass_subset_mux）
├── bin/                                       # 第三方运行时（scripts\bootstrap.ps1 获取，不随 git 分发）
│   ├── python/                               # Python embeddable（服务端运行时）
│   ├── mkvtoolnix/                           # mkvmerge / mkvextract
│   ├── ffmpeg/                               # 含 libass（预览必需）
│   └── assfonts/                             # bootstrap 自动下载（v0.7.3）
├── data/                                     # 运行时数据：jobs（任务历史）/ log / tmp / previews
└── docs/                                     # CONTEXT.md（领域术语表）/ BUG-REPORT.md / backups
```

## 环境要求

| 组件 | 系统级 | bin/ 自带 | 说明 |
|---|---|---|---|
| Windows 10/11 | ✅ 必须 | — | PowerShell、浏览器均为系统内置 |
| Python 3.8+ | 可选 | ✅ embeddable | 纯标准库，无 pip 依赖 |
| MKVToolNix | 可选 | ✅ v101.0 | 封装/提取/内封轨预览 |
| ffmpeg | 可选 | ✅ 9.0.1 | 预览；必须含 libass |
| assfonts | 可选 | ✅ v0.7.3 | bootstrap 自动从 GitHub Releases 下载 |

## 工作目录（扫描根）

「高级选项 → 工作目录」可切换拖放识别搜索的根目录（默认 `D:\Video`），持久化在 `config.json`。

## 许可与第三方声明

- 本项目代码随用途自由使用（个人工具）。
- 随附第三方二进制均为各自许可证分发：MKVToolNix（GPLv2，许可见其安装目录 doc/）、ffmpeg（LGPL/GPL 构建）、Python（PSF）、assfonts（许可见其自带 LICENSE.txt）。
- `bin/` 不进入 git 仓库，通过 `bootstrap.ps1` 从官方源获取。
