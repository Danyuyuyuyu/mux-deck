using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MuxDeck.Core;
using MuxDeck.Core.Components;
using MuxDeck.Desktop;
using MuxDeck.Desktop.Preferences;
using MuxDeck.Desktop.ViewModels;
using MuxDeck.Desktop.Views;

internal static class ComponentInstallationUiChecks
{
    public static int Run()
    {
        var root = Path.Combine(Workspace.Discover().Root, "desktop", "artifacts", "component-ui", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Exception? failure = null; var passed = 0;
        var thread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                var storage = StoragePaths.ForRoot(Path.Combine(root, "data"), Path.Combine(root, "tools"));
                var workspace = new Workspace(root) { ToolsDirectoryOverride = storage.ToolsDirectory };
                var checker = new Checker();
                var installer = new Installer(checker);
                var storePath = Path.Combine(root, "settings.json");
                var env = new EnvironmentViewModel(workspace, storage, new StorageSettingsStore(storePath), checker, installer);
                var main = new MainWindowViewModel(workspace, storage: storage, environment: env);
                var window = new MainWindow { DataContext = main };
                window.Show(); main.ShowEnvironmentCommand.Execute(null);
                UiInputTestSupport.Pump(env.ValidationTask);
                var row = env.ExtensionComponents.Single(c => c.Id == "assfonts");
                void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); passed++; Console.WriteLine("PASS: " + name); }
                Button Action(string text) => window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.DataContext, row) && Equals(b.Content, text) && b.IsEffectivelyVisible);
                void Snapshot(string label)
                {
                    foreach (var size in new[] { (1280, 900), (1040, 760) })
                    {
                        window.Width = size.Item1; window.Height = size.Item2;
                        UiInputTestSupport.Save(window, Path.Combine(root, label + "-" + size.Item1 + ".png"));
                        var scroll = window.FindControl<ScrollViewer>("EnvironmentScroll")!;
                        Check(scroll.Extent.Width <= scroll.Viewport.Width + 1, label + " 无横向溢出 " + size.Item1);
                    }
                }
                Check(row.StatusText == "未安装" && row.ShowInstall, "缺失且受支持的 assfonts 提供安装");
                Check(env.CoreComponents.All(c => c.StatusText == "可用" && !c.ShowInstall && !c.ShowGuide), "验证通过的正常组件没有多余操作");
                var dots = window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("env-dot") && !b.Classes.Contains("env-summary-dot")).ToArray();
                Check(dots.Length == 4 && dots.Count(b => b.Classes.Contains("ok")) == 3 && dots.Count(b => b.Classes.Contains("error")) == 1, "状态点与短文字共同表达正常/缺失");
                env.Refresh(); UiInputTestSupport.Pump(env.ValidationTask);
                Check(ReferenceEquals(row, env.ExtensionComponents.Single(c => c.Id == "assfonts")), "重新检测保留组件对象");
                Snapshot("missing");
                UiInputTestSupport.Click(window, Action("安装"));
                UiInputTestSupport.Until(() => row.StatusText == "下载中");
                Check(env.IsInstalling && main.Single.IsComponentInstalling && !env.CanRefresh, "安装锁与封装入口联动");
                Check(row.ShowProgress && row.ProgressValue == 40 && row.ShowCancel && !row.ShowInstall, "下载显示实际进度和取消，隐藏重复安装");
                var savedTools = env.ToolsDirectoryInput;
                env.ToolsDirectoryInput = Path.Combine(root, "changed");
                env.SaveSettingsCommand.Execute(null);
                Check(env.ToolsDirectoryInput == savedTools && !File.Exists(storePath), "安装期间即使强制执行也不能改路径");
                main.ShowSingleCommand.Execute(null);
                main.ShowEnvironmentCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                Check(row.StatusText == "下载中" && env.IsInstalling, "切页保留正在安装的条目");
                Snapshot("downloading");
                installer.Complete(false);
                UiInputTestSupport.Pump(env.InstallTask);
                UiInputTestSupport.Pump(env.ValidationTask);
                Check(row.HasInstallError && row.StatusText == "安装失败" && row.InstallActionText == "重试" && row.ShowInstall, "失败原地显示原因与重试");
                UiInputTestSupport.Click(window, Action("详情"));
                Check(row.IsDetailsOpen && row.DetailsText.Contains("受控网络失败"), "详情行内展开保留原始错误");
                Snapshot("failed");
                env.Refresh(); UiInputTestSupport.Pump(env.ValidationTask);
                Check(row.HasInstallError && row.IsDetailsOpen, "重新检测不擦掉失败详情");
                UiInputTestSupport.Click(window, Action("重试"));
                UiInputTestSupport.Until(() => row.IsInstalling);
                installer.Complete(true);
                UiInputTestSupport.Pump(env.InstallTask);
                UiInputTestSupport.Pump(env.ValidationTask);
                Check(row.IsHealthy && row.StatusText == "可用" && !row.ShowInstall && !row.HasInstallError, "重试成功自动检测转可用并撤下动作");
                Check(!env.IsInstalling && !main.Single.IsComponentInstalling && env.CanChangePaths, "安装结束释放路径与封装锁");
                Snapshot("available");
                checker.Missing.UnionWith(["mkvtoolnix", "ffmpeg", "afs"]);
                env.Refresh(); UiInputTestSupport.Pump(env.ValidationTask);
                Check(env.CoreComponents.Concat(env.ExtensionComponents).Where(c => c.Id != "assfonts").All(c => c.ShowInstall && !c.ShowGuide), "三个新组件缺失时均提供行内安装");
                Snapshot("all-missing");
                foreach (var other in env.CoreComponents.Concat(env.ExtensionComponents).Where(c => c.Id != "assfonts"))
                {
                    var button = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.DataContext, other) && Equals(b.Content, "安装") && b.IsEffectivelyVisible);
                    UiInputTestSupport.Click(window, button);
                    UiInputTestSupport.Until(() => other.IsInstalling);
                    Check(other.ShowCancel && other.ShowProgress && !other.ShowInstall, other.Name + " 共用行内进度/取消");
                    installer.Complete(true);
                    UiInputTestSupport.Pump(env.InstallTask); UiInputTestSupport.Pump(env.ValidationTask);
                    Check(other.IsHealthy && !other.ShowInstall && !other.ShowGuide, other.Name + " 安装后自动检测并收起操作");
                }
                checker.DependencyMissing = true;
                env.Refresh(); UiInputTestSupport.Pump(env.ValidationTask);
                var afs = env.CoreComponents.Single(c => c.Id == "afs");
                Check(afs.ShowInstall && afs.InstallActionText == "安装依赖" && afs.IsWarningStatus, "AFS 主程序存在但缺后端提供安装依赖");
                Snapshot("dependency-missing");
                UiInputTestSupport.Click(window, window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.DataContext, afs) && Equals(b.Content, "安装依赖") && b.IsEffectivelyVisible));
                UiInputTestSupport.Until(() => afs.IsInstalling);
                installer.Complete(true);
                UiInputTestSupport.Pump(env.InstallTask); UiInputTestSupport.Pump(env.ValidationTask);
                Check(afs.IsHealthy && !afs.ShowInstall, "补齐依赖后 AFS 自动恢复可用");
                checker.Installed = false;
                env.Refresh(); UiInputTestSupport.Pump(env.ValidationTask);
                UiInputTestSupport.Click(window, Action("安装"));
                UiInputTestSupport.Until(() => row.IsInstalling);
                UiInputTestSupport.Click(window, Action("取消"));
                UiInputTestSupport.Pump(env.InstallTask);
                Check(!env.IsInstalling && row.ShowInstall && !row.HasInstallError, "取消后恢复安装入口，不误报成功或失败");
                env.SetWorkGuard(() => true);
                Check(!env.CanChangePaths && !row.InstallCommand!.CanExecute(null), "封装/探测忙碌时禁用组件安装和路径保存");
                env.SetWorkGuard(() => false);
                UiInputTestSupport.Click(window, Action("安装"));
                UiInputTestSupport.Until(() => row.IsInstalling);
                var closed = false;
                window.Closed += (_, _) => closed = true;
                window.Close();
                UiInputTestSupport.Until(() => closed);
                Check(env.InstallTask.IsCompleted && installer.Cancelled > 0, "窗口关闭等待安装取消完成");
            }
            catch (Exception ex) { failure = ex; }
        });
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        Console.WriteLine($"Component UI: {passed} PASS; Screenshots: {root}");
        if (failure is not null) Console.Error.WriteLine(failure);
        return failure is null ? 0 : 1;
    }
    private sealed class Checker : IComponentVerifier
    {
        public bool Installed { get; set; }
        public HashSet<string> Missing { get; } = [];
        public bool DependencyMissing { get; set; }
        public Task<ComponentInspection> VerifyAsync(ComponentDefinition component, Workspace workspace, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var missing = component.Id == "assfonts" && !Installed || Missing.Contains(component.Id);
            var dependencies = component.Id == "afs" && DependencyMissing;
            return Task.FromResult(new ComponentInspection(missing ? ComponentState.Missing : dependencies ? ComponentState.DependencyMissing : ComponentState.Available,
                component.ToolNames.Select(n => new ToolStatus(n, "", missing ? null : Path.Combine(workspace.ToolsDirectory, n + ".exe")) { Source = "Mux Deck" }).ToArray(),
                missing ? null : ComponentReleases.All[component.Id].Version, missing ? "未检测到可执行文件。" : dependencies ? "缺少 fonttools 后端。" : null));
        }
    }
    private sealed class Installer(Checker checker) : IComponentInstaller
    {
        private TaskCompletionSource<bool>? completion;
        public int Cancelled { get; private set; }
        public void Complete(bool success) => completion!.TrySetResult(success);
        public async Task<ComponentInstallResult> InstallAsync(string id, StoragePaths storage, IProgress<ComponentInstallProgress> progress, CancellationToken cancellationToken)
        {
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            progress.Report(new(ComponentInstallPhase.Downloading, "下载中", 4 * 1048576, 10 * 1048576));
            try
            {
                if (!await completion.Task.WaitAsync(cancellationToken)) throw new ComponentInstallException("受控网络失败，请重试。", "controlled.log");
                progress.Report(new(ComponentInstallPhase.Installing, "正在安装"));
                if (id == "assfonts") checker.Installed = true;
                checker.Missing.Remove(id);
                if (id == "afs") checker.DependencyMissing = false;
                return new(Path.Combine(storage.ToolsDirectory, ComponentReleases.All[id].Directory), "controlled.log");
            }
            catch (OperationCanceledException) { Cancelled++; throw; }
        }
    }
}
