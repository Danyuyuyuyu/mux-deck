using MuxDeck.Desktop.Composition;
using MuxDeck.Infrastructure;
using MuxDeck.Infrastructure.Muxing;
using MuxDeck.Infrastructure.Components;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MuxDeck.Core;
using MuxDeck.Core.Components;
using MuxDeck.Core.Muxing;
using MuxDeck.Desktop;
using MuxDeck.Desktop.Preferences;
using MuxDeck.Desktop.ViewModels;
using MuxDeck.Desktop.Views;

internal static class ComponentUninstallUiChecks
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "mux-deck-uninstall-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Exception? failure = null; var passed = 0;
        var thread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                var storage = StoragePaths.ForRoot(Path.Combine(root, "data"), Path.Combine(root, "tools"));
                foreach (var release in ComponentReleases.All.Values)
                    foreach (var file in release.RequiredFiles) Write(Path.Combine(storage.ToolsDirectory, release.Directory, file));
                Write(Path.Combine(storage.ToolsDirectory, "fonttools", "Scripts", "pyftsubset.exe"));
                Write(Path.Combine(storage.ToolsDirectory, "fonttools", "Scripts", "ttx.exe"));
                var checker = new Checker();
                var installer = new Installer();
                var remover = new DelayedRemover();
                var workspace = new Workspace(root) { ToolsDirectoryOverride = storage.ToolsDirectory };
                var env = AppComposition.CreateEnvironment(workspace, storage, Path.Combine(root, "settings.json"), checker, installer, remover);
                var probeRunner = new BlockingProbeRunner();
                var main = AppComposition.CreateMain(workspace, AppComposition.CreateSingleService(workspace, probeRunner), preferences: new UserPreferencesStore(Path.Combine(root, "preferences.json")), storage: storage, environment: env);
                var window = new MainWindow { DataContext = main };
                window.Show(); main.ShowEnvironmentCommand.Execute(null); UiInputTestSupport.Pump(env.ValidationTask);
                var assfonts = env.ExtensionComponents.Single(c => c.Id == "assfonts");
                var afs = env.CoreComponents.Single(c => c.Id == "afs");
                var ffmpeg = env.ExtensionComponents.Single(c => c.Id == "ffmpeg");
                void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); passed++; Console.WriteLine("PASS: " + name); }
                Button Button(EnvironmentComponentViewModel row, string text) => window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.DataContext, row) && Equals(b.Content, text) && b.IsEffectivelyVisible);
                void Request(EnvironmentComponentViewModel row, string item)
                {
                    var button = Button(row, "⋯");
                    UiInputTestSupport.Click(window, button);
                    var flyout = (MenuFlyout)button.Flyout!;
                    var menu = flyout.Items.OfType<MenuItem>().Single(m => Equals(m.Header, item) && m.IsVisible);
                    Check(menu.Command is not null, item + " 菜单绑定真实命令");
                    // headless 受控调用菜单控件的绑定命令，确认按钮仍走实际键盘事件。
                    menu.Command!.Execute(menu.CommandParameter); flyout.Hide(); Dispatcher.UIThread.RunJobs();
                    var confirmation = window.GetVisualDescendants().OfType<Border>().Single(b => ReferenceEquals(b.DataContext, row) && b.Classes.Contains("removal-confirmation"));
                    var scroll = window.FindControl<ScrollViewer>("EnvironmentScroll")!;
                    var point = confirmation.TranslatePoint(default, scroll)!.Value;
                    Check(point.Y >= -1 && point.Y + confirmation.Bounds.Height <= scroll.Viewport.Height + 1, "确认目录及按钮自动滚入可视区域");
                }
                void Confirm(EnvironmentComponentViewModel row)
                {
                    UiInputTestSupport.Click(window, Button(row, "确认卸载"));
                    UiInputTestSupport.Pump(env.RemovalTask); UiInputTestSupport.Pump(env.ValidationTask);
                }
                void Snapshot(string label)
                {
                    foreach (var size in new[] { (1280, 900), (1040, 760) })
                    {
                        window.Width = size.Item1; window.Height = size.Item2;
                        Dispatcher.UIThread.RunJobs();
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        Dispatcher.UIThread.RunJobs();
                        window.UpdateLayout();
                        var scroll = window.FindControl<ScrollViewer>("EnvironmentScroll")!;
                        var confirmation = window.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("removal-confirmation") && b.IsEffectivelyVisible);
                        var reveal = (Control?)confirmation ?? window.GetVisualDescendants().OfType<StackPanel>().FirstOrDefault(panel => panel.DataContext is EnvironmentComponentViewModel { IsDetailsOpen: true } && panel.IsEffectivelyVisible && panel.Children.OfType<TextBox>().Any());
                        if (reveal is not null) reveal.BringIntoView();
                        else scroll.Offset = default;
                        UiInputTestSupport.Save(window, Path.Combine(root, label + "-" + size.Item1 + ".png"));
                        Check(scroll.Extent.Width <= scroll.Viewport.Width + 1, label + " 无横向溢出 " + size.Item1);
                        if (confirmation is not null)
                        {
                            var point = confirmation.TranslatePoint(default, scroll)!.Value;
                            Check(confirmation.Bounds.Height > 80 && point.Y >= -1 && point.Y + confirmation.Bounds.Height <= scroll.Viewport.Height + 1, label + " 截图中确认范围完整可见 " + size.Item1);
                        }
                    }
                }
                Check(env.CoreComponents.Concat(env.ExtensionComponents).All(c => c.ShowManagementMenu && c.CanRemoveComponent), "手动复制的四组件提供轻量卸载菜单");
                Snapshot("manual");
                Request(assfonts, "卸载组件");
                Check(assfonts.IsConfirmingRemoval && assfonts.RemovalDirectory == Path.Combine(storage.ToolsDirectory, "assfonts") && assfonts.RemovalDescription.Contains("全部内容"), "行内确认显示准确目录和全部内容范围");
                Snapshot("confirm-component");
                UiInputTestSupport.Click(window, Button(assfonts, "取消"));
                Check(!assfonts.IsConfirmingRemoval && Directory.Exists(Path.Combine(storage.ToolsDirectory, "assfonts")), "取消确认不删除组件");

                var blocked = Path.Combine(storage.ToolsDirectory, "assfonts", "assfonts.exe");
                using (var handle = new FileStream(blocked, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Request(assfonts, "卸载组件"); Confirm(assfonts);
                    Check(assfonts.HasRemovalError && assfonts.StatusText == "卸载失败" && assfonts.ShowRemovalRetry && !assfonts.ShowInstall, "卸载失败原地重试，不误导成安装重试");
                    UiInputTestSupport.Click(window, Button(assfonts, "详情"));
                    Check(assfonts.DetailsText.Contains("uninstall-") && assfonts.DetailsText.Contains(blocked), "失败详情保留文件原因和操作日志");
                    Snapshot("failed");
                }
                UiInputTestSupport.Click(window, Button(assfonts, "重试卸载")); Confirm(assfonts);
                Check(assfonts.IsMissing && assfonts.ShowInstall && !assfonts.ShowManagementMenu && !assfonts.HasOperationError, "重试成功重新检测为未安装并恢复安装");
                Write(blocked); env.Refresh(); UiInputTestSupport.Pump(env.ValidationTask);
                using (var handle = new FileStream(blocked, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Request(assfonts, "卸载组件"); Confirm(assfonts);
                }
                UiInputTestSupport.Pump(new ComponentUninstaller().UninstallAsync(ComponentDirectories.CreatePlan(storage, "assfonts", ComponentRemovalKind.Component), storage, new SilentProgress(), default));
                env.Refresh(); UiInputTestSupport.Pump(env.ValidationTask);
                Check(assfonts.IsMissing && assfonts.ShowInstall && !assfonts.HasRemovalError, "目标已从外部清理时重新检测解除旧卸载错误并恢复安装");

                Request(afs, "卸载独立依赖");
                Check(afs.RemovalDescription.Contains("主程序保留"), "依赖确认说明保留主程序");
                Snapshot("confirm-dependency");
                Confirm(afs);
                Check(File.Exists(Path.Combine(storage.ToolsDirectory, "assfontsubset", "AssFontSubset.Console.exe")) && afs.State == ComponentState.DependencyMissing && afs.InstallActionText == "安装依赖", "卸载独立依赖保留主程序并恢复安装依赖");
                UiInputTestSupport.Click(window, Button(afs, "安装依赖"));
                UiInputTestSupport.Pump(env.InstallTask); UiInputTestSupport.Pump(env.ValidationTask);
                Check(afs.IsHealthy && afs.CanRemoveDependencies, "安装依赖后恢复可用及依赖管理");
                Request(afs, "卸载组件"); Confirm(afs);
                Check(afs.IsMissing && afs.ShowInstall && afs.CanRemoveDependencies && afs.HasIndependentDependencies, "主程序卸载后独立依赖与入口保留");
                Snapshot("main-missing-dependency-retained");
                UiInputTestSupport.Click(window, Button(afs, "安装"));
                UiInputTestSupport.Pump(env.InstallTask); UiInputTestSupport.Pump(env.ValidationTask);
                Check(afs.IsHealthy && !Directory.Exists(Path.Combine(storage.ToolsDirectory, "assfontsubset", "python")), "重新安装主程序复用独立依赖");

                var external = Path.Combine(root, "external", "ffmpeg.exe"); Write(external);
                checker.External["ffmpeg"] = external;
                Request(ffmpeg, "卸载组件"); Confirm(ffmpeg);
                Check(ffmpeg.IsHealthy && ffmpeg.SourceText == "PATH" && !ffmpeg.ShowManagementMenu && File.Exists(external), "卸载后回退外部来源，隐藏卸载并保留外部文件");
                var externalAfs = Path.Combine(root, "external", "AssFontSubset.Console.exe"); Write(externalAfs);
                checker.External["afs"] = externalAfs;
                Request(afs, "卸载组件"); Confirm(afs);
                Check(!afs.CanRemoveComponent && afs.CanRemoveDependencies && afs.SourceText == "PATH", "外部 AFS 只提供目录内独立依赖卸载");
                checker.ExternalBackend = true;
                Request(afs, "卸载独立依赖"); Confirm(afs);
                Check(afs.IsHealthy && !afs.ShowManagementMenu && File.Exists(externalAfs), "存在其他后端时卸载依赖后保持可用，保留外部主程序");

                var mkv = env.CoreComponents.Single(c => c.Id == "mkvtoolnix");
                var video = Path.Combine(root, "probe.mkv"); Write(video);
                probeRunner.Hold = true;
                var import = main.Single.PrepareMediaInputAsync([new(video, InputCandidateKind.LocalFile)]);
                Check(main.Single.HasActiveToolProbes && !env.CanChangePaths, "媒体识别探测未结束时禁止卸载和路径修改");
                env.RequestRemoval(mkv, ComponentRemovalKind.Component);
                Check(!mkv.IsConfirmingRemoval, "正在使用工具的导入探测阻止卸载确认");
                probeRunner.Release(); UiInputTestSupport.Pump(import);
                Check(!main.Single.HasActiveToolProbes && env.CanChangePaths, "探测真正结束后解除目录操作锁");
                env.SetWorkGuard(() => true);
                env.RequestRemoval(mkv, ComponentRemovalKind.Component);
                Check(!mkv.IsConfirmingRemoval && Directory.Exists(Path.Combine(storage.ToolsDirectory, "mkvtoolnix")), "封装或探测忙碌时拒绝卸载请求");
                env.SetWorkGuard(() => false);
                Request(mkv, "卸载组件");
                env.ToolsDirectoryInput = Path.Combine(root, "other-tools");
                env.WorkDirectoryInput = Path.Combine(root, "other-data"); env.SaveSettingsCommand.Execute(null);
                UiInputTestSupport.Pump(env.ValidationTask);
                Check(!mkv.IsConfirmingRemoval && Directory.Exists(Path.Combine(storage.ToolsDirectory, "mkvtoolnix")), "保存新路径使旧确认失效，不删除旧目录");
                env.ToolsDirectoryInput = storage.ToolsDirectory; env.WorkDirectoryInput = storage.AppDataRoot;
                env.SaveSettingsCommand.Execute(null); UiInputTestSupport.Pump(env.ValidationTask);

                remover.Hold = true;
                Request(mkv, "卸载组件");
                UiInputTestSupport.Click(window, Button(mkv, "确认卸载"));
                UiInputTestSupport.Until(() => remover.Started);
                Check(env.IsUninstalling && !env.CanChangePaths && !env.CanRefresh && main.Single.IsComponentUninstalling && !main.Single.CanEdit && !main.Single.CanStart, "卸载锁定安装路径、开始封装与新探测");
                var before = env.ToolsDirectoryInput; env.ToolsDirectoryInput = Path.Combine(root, "blocked-path");
                Check(env.ToolsDirectoryInput == before, "卸载期间强制赋值也不能修改路径");
                main.ShowSingleCommand.Execute(null); main.ShowEnvironmentCommand.Execute(null); Dispatcher.UIThread.RunJobs();
                Check(mkv.IsUninstalling && env.SummaryTitle == "正在卸载组件", "卸载切页保留进度与中性顶部状态");
                Snapshot("uninstalling");
                var closed = false; window.Closed += (_, _) => closed = true;
                window.Close(); Dispatcher.UIThread.RunJobs();
                Check(!closed && !env.RemovalTask.IsCompleted, "关闭窗口等待已开始删除收尾");
                remover.Release(); UiInputTestSupport.Until(() => closed);
                Check(env.RemovalTask.IsCompleted && !Directory.Exists(Path.Combine(storage.ToolsDirectory, "mkvtoolnix")), "删除完成后窗口关闭，不遗留后台卸载");
            }
            catch (Exception ex) { failure = ex; }
        });
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        Console.WriteLine($"Component uninstall UI: {passed} PASS; Screenshots: {root}");
        if (failure is not null) Console.Error.WriteLine(failure);
        return failure is null ? 0 : 1;
    }
    private static void Write(string path) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "controlled manual"); }
    private sealed class Checker : IComponentVerifier
    {
        public Dictionary<string, string> External { get; } = [];
        public bool ExternalBackend { get; set; }
        public Task<ComponentInspection> VerifyAsync(ComponentDefinition component, Workspace workspace, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var release = ComponentReleases.All[component.Id];
            var parts = component.ToolNames.Select((name, index) =>
            {
                var own = Path.Combine(workspace.ToolsDirectory, release.Directory, release.RequiredFiles[index]);
                var path = File.Exists(own) ? own : External.GetValueOrDefault(component.Id);
                return new ToolStatus(name, "", path) { Source = path == own ? "Mux Deck" : path is null ? "未找到" : "PATH" };
            }).ToArray();
            var missing = parts.All(p => p.Path is null);
            bool Pair(string scripts) => File.Exists(Path.Combine(scripts, "pyftsubset.exe")) && File.Exists(Path.Combine(scripts, "ttx.exe"));
            var depsMissing = component.Id == "afs" && !ExternalBackend && !Pair(Path.Combine(workspace.ToolsDirectory, "fonttools", "Scripts")) && !Pair(Path.Combine(workspace.ToolsDirectory, "assfontsubset", "python", "Scripts"));
            return Task.FromResult(new ComponentInspection(missing ? ComponentState.Missing : depsMissing ? ComponentState.DependencyMissing : ComponentState.Available, parts, missing ? null : "1.0", missing ? "未检测到可执行文件。" : depsMissing ? "缺少 fonttools 后端。" : null));
        }
    }
    private sealed class Installer : IComponentInstaller
    {
        public Task<ComponentInstallResult> InstallAsync(string id, StoragePaths storage, IProgress<ComponentInstallProgress> progress, CancellationToken cancellationToken)
        {
            if (File.Exists(Path.Combine(storage.ToolsDirectory, "assfontsubset", "AssFontSubset.Console.exe")))
            {
                Write(Path.Combine(storage.ToolsDirectory, "fonttools", "Scripts", "pyftsubset.exe"));
                Write(Path.Combine(storage.ToolsDirectory, "fonttools", "Scripts", "ttx.exe"));
            }
            else foreach (var file in ComponentReleases.All[id].RequiredFiles) Write(Path.Combine(storage.ToolsDirectory, ComponentReleases.All[id].Directory, file));
            return Task.FromResult(new ComponentInstallResult(storage.ToolsDirectory, "controlled.log"));
        }
    }
    private sealed class DelayedRemover : IComponentUninstaller
    {
        public bool Hold { get; set; }
        public bool Started { get; private set; }
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => completion.TrySetResult();
        public async Task<ComponentRemovalResult> UninstallAsync(ComponentRemovalPlan plan, StoragePaths storage, IProgress<ComponentRemovalProgress> progress, CancellationToken cancellationToken)
        {
            if (Hold) { Started = true; progress.Report(new("正在卸载")); await completion.Task; cancellationToken = default; }
            return await new ComponentUninstaller().UninstallAsync(plan, storage, progress, cancellationToken);
        }
    }
    private sealed class BlockingProbeRunner : IMuxToolRunner
    {
        public bool Hold { get; set; }
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() { Hold = false; completion.TrySetResult(); }
        public async Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessLine>? progress, CancellationToken cancellationToken)
        {
            if (Hold) await completion.Task.WaitAsync(cancellationToken);
            return new(0, """{"tracks":[{"id":0,"type":"video","codec":"AVC","properties":{"codec_id":"V_MPEG4/ISO/AVC","pixel_dimensions":"320x180","language":"und","track_name":"","default_track":true,"forced_track":false}}],"attachments":[]}""", "");
        }
    }
    private sealed class SilentProgress : IProgress<ComponentRemovalProgress> { public void Report(ComponentRemovalProgress value) { } }
}
