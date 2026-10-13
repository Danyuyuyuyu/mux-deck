# 组件验证与行内安装第一轮

本文保留首轮能力与验收记录；当前四组件安装及独立后端能力见[补齐组件安装](component-installation-complete.md)。

输入为当前工具解析结果和已保存的组件/工作目录。输出为经过启动验证的组件状态、依赖原因与 assfonts 安装结果。范围包含状态点、异步检测、稳定条目、行内进度/取消/重试/详情、切页保留和关闭清理。

本轮自动安装只支持 Windows x64 assfonts v0.7.3。MKVToolNix、FFmpeg、AssFontSubset 缺失时提供官方指南；AFS 的 Python/fonttools 缺失单独提示。assfonts 尚未接入 V2 封装备份后端，工具可用只表示程序验证通过。

安装固定官方 Release URL、包大小与本轮下载核对的 SHA-256。下载到配置的 cache，日志到 logs，组件先在目标目录内唯一暂存子目录解压与验证，再不覆盖地提交。拒绝越界、重复 ZIP 路径、符号链接、异常体积、缺少主程序和任何已存在的目标目录；失败/取消不留下正式半成品或覆盖系统工具。

检测状态与安装阶段分别维护，页面刷新不销毁组件对象或安装错误。保存目录/安装互斥，封装或探测忙碌时不允许修改运行路径；安装期间阻止开始新的封装。关闭等待环境操作取消与清理。路径保存继续使用现有存储模型与默认值，增加完整绝对路径校验，避免安装目标随当前工作目录变化。

验收覆盖受控子进程结果、后端缺失、真实本机工具探测；安装覆盖校验失败、损坏/越界 ZIP、网络失败、取消、目标竞态、重试和真实固定发布包在隔离目录落位。UI 覆盖状态点/短文案/按钮能力、行内进度/失败详情、切页和两种窗口尺寸。

## 已完成验证（2026-10-12）

Core、Desktop、Smoke 的 Release 构建均为 0 warnings / 0 errors，未新增包依赖。本机受限沙箱不能写仓库构建产物，复用现有 restore assets，输出和测试 artifacts 隔离在 `C:/Users/ZhenXun/.codex/visualizations/2026/10/11/01a12b48-aa2a-7643-8f63-25ccea505082/desktop-build2/`。

下表目录相对于该根目录的 `bin-smoke/desktop/artifacts/`；计数来自实际运行输出，组件检查不生成 JSON 报告。

| 检查 | 结果 | 证据目录 |
|---|---|---|
| `--components` | 31/31 | `components/0e96602f1f3f42678a529a2b4644e277/` |
| `--components-real` | 6/6 | `components/30d83eeae4c244babf101143252ea406/` |
| `--component-install-ui` | 24/24 | `component-ui/2c4aebcec752428a8021eac8e8933491/` |
| `--environment-ui` | 23/23 | `environment-ui/49fed7a4050844f2a8c618762d34f13b/` |
| 默认 Smoke | 28/28 | 实际控制台输出 |
| `--single-mux-ui-round1` | 176/176 | `single-mux-round1-b/ui-037c7bbb50d641aa91bc2e5d6c18cc52/report.json` |

真实检测通过 MKVToolNix 100.0、AssFontSubset 2.1.1 与本机 PyFontTools、带 subtitles 滤镜的 FFmpeg 8.1.1；真实联网安装固定 [assfonts v0.7.3 官方发布包](https://github.com/wyzdwdz/assfonts/releases/tag/v0.7.3)，包大小 14,448,047 字节，SHA-256 为 `4DD11A3AA86CF03B8428FFD644B54C25D9A0AB7AB212C1460E6A34EAF22D7FA4`。其下载、解压、启动、提交、重新检测、日志和临时目录清理在独立测试目录通过，未安装到用户当前组件目录。

已目视检查 1280×900 与 1040×760 的未安装、下载中和失败详情截图：状态/动作列对齐，下载进度在行内，失败详情随页面滚动，长路径省略后有完整 tooltip。原生选择器、真实辅助技术和 125%/150% DPI 未进行本轮人工验证；headless 证据不替代这些项目。封装参数、任务 QC 与 AFS 告警规则未因本轮组件功能改动。
