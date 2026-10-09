# Mux Deck 桌面重写

分支：`v2/avalonia-rewrite`。C# + .NET 10 + Avalonia 12.1.2，面向 Windows 桌面。

本阶段交付可运行骨架。现有 Python/Web 版本保留在 `app/`，作为业务行为的参考和迁移前的可用版本。

## 运行

安装 .NET 10 SDK，双击仓库根目录 `start_mux_desktop.bat`，或执行：

```powershell
cd desktop
dotnet restore MuxDeck.sln --locked-mode
dotnet run --project src/MuxDeck.Desktop
# 指定便携工具所在的工作区根目录（其下应有 bin/）：
dotnet run --project src/MuxDeck.Desktop -- --root E:\Video\mux-deck
```

依赖固定在 12.1.2（本机已有完整缓存，便于离线验证），不是浮动的 latest。提交各项目的 `packages.lock.json`，升级 Avalonia 时一起更新 Desktop、Fluent、Headless。

## 已实现

- 原生窗口、深色 Fluent 主题、侧栏导航、最小窗口尺寸、可滚动内容区。
- 单个封装表单：视频原生文件选择、简繁字幕路径和输出目录输入；切换页面保留当前表单。
- 六项工具路径检测：便携 `bin/` 优先，随后 PATH，再回退 MKVToolNix 默认安装位置。
- 工作区日志、重新检测环境。
- 可供后续工具调用复用的异步进程模块：参数列表传递、stdout/stderr 分流、Unicode 输出、取消时终止进程树。
- 控制台冒烟检查：真实子进程调用、取消、路径优先级、Avalonia 编译绑定、导航、状态保留及窗口渲染。

环境检测当前只确认文件位置，不执行工具或核验版本，不宣称所有依赖已满足。AFS 的 Python/fonttools 后端检测、版本检测及组件安装尚未迁移。

## 尚未实现

实际封装、字幕匹配与体检、字体子集化、QC、预设持久化、批量队列、字幕提取、预览、章节编辑、快速修补、自动安装与后处理命令。界面中的待迁移入口明确显示状态，没有模拟封装成功。

## 验证

```powershell
cd desktop
dotnet build MuxDeck.sln --no-restore -c Release
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release
# 可选：用真实 Skia 渲染生成无窗口截图：
dotnet run --project tests/MuxDeck.Smoke --no-build -c Release -- --screenshot preview.png
```

冒烟项目失败返回非零退出码；无需真实视频，不修改原版本配置，也不调用封装工具。原生文件选择器仍需在交互桌面人工验证。

开发前读取 [AGENTS.md](AGENTS.md)。架构、开发阶段与验收要求见 [架构导览](ARCHITECTURE.md)。

## 发布方向

后续以 `win-x64` 自包含发布为主，让用户不必安装 .NET SDK。当前交付开发骨架，尚未制作发布包或移除字体工具的运行时依赖。

参考：[Avalonia MVVM 项目结构](https://docs.avaloniaui.net/docs/get-started/create-your-first-project)、[编译绑定](https://docs.avaloniaui.net/docs/data-binding/compiled-bindings)、[固定包版本](https://www.nuget.org/packages/Avalonia.Desktop/12.1.2)。
