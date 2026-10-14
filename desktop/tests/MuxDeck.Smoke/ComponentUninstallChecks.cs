using C = MuxDeck.Contracts;
using MuxDeck.Application;
using MuxDeck.Infrastructure;
using MuxDeck.Infrastructure.Muxing;
using MuxDeck.Infrastructure.Components;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using MuxDeck.Core;

internal static class ComponentUninstallChecks
{
    public static async Task<int> RunAsync(string? packages = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "mux-deck-uninstall-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var checks = new List<string>(); string? error = null;
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); checks.Add(name); Console.WriteLine("PASS: " + name); }
        StoragePaths Paths(string name) => StoragePaths.ForRoot(Path.Combine(root, name, "data"), Path.Combine(root, name, "tools"));
        void FileAt(string path, string text = "manual") { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
        var remover = new ComponentUninstaller();
        async Task Remove(StoragePaths storage, string id, ComponentRemovalKind kind = ComponentRemovalKind.Component)
            => await remover.UninstallAsync(ComponentDirectories.CreatePlan(storage, id, kind), storage, new Progress(), default);
        try
        {
            Environment.SetEnvironmentVariable("PATH", "");
            if (packages is not null)
            {
                var storage = Paths("real 中文 空格");
                using var client = new HttpClient(new PackageHandler(packages));
                var installer = new ComponentInstaller(client);
                var verifier = new ComponentVerifier();
                var workspace = new Workspace(root) { ToolsDirectoryOverride = storage.ToolsDirectory };
                foreach (var id in new[] { "mkvtoolnix", "ffmpeg", "assfonts", "afs" })
                {
                    await installer.InstallAsync(id, storage, new Progress(), default);
                    Check((await verifier.VerifyAsync(ComponentCatalog.All.Single(c => c.Id == id), workspace, default)).State == ComponentState.Available, id + " 真实固定包安装可用");
                    var managedDirectory = Path.Combine(storage.ToolsDirectory, ComponentReleases.All[id].Directory);
                    if (id == "afs")
                    {
                        var external = Path.Combine(root, "external"); Directory.CreateDirectory(external);
                        File.Copy(Path.Combine(managedDirectory, "AssFontSubset.Console.exe"), Path.Combine(external, "AssFontSubset.Console.exe"));
                    }
                    await Remove(storage, id);
                    Check(!Directory.Exists(managedDirectory) && Directory.Exists(storage.ToolsDirectory), id + " 真实目录卸载仅删除对应子目录");
                    if (id == "mkvtoolnix")
                    {
                        var after = await verifier.VerifyAsync(ComponentCatalog.All.Single(c => c.Id == id), workspace, default);
                        Check(after.Parts.All(p => p.Path is null || p.Source == "系统"), "卸载目录内 MKV 后回退系统或未安装，不删除系统工具");
                    }
                    if (id == "afs") Check((await verifier.VerifyAsync(ComponentCatalog.All.Single(c => c.Id == id), workspace, default)).State == ComponentState.Missing, "AFS 主程序与随包 Python 一起卸载");
                }
                var externalDirectory = Path.Combine(root, "external");
                var externalMain = Path.Combine(externalDirectory, "AssFontSubset.Console.exe");
                var hash = SHA256.HashData(File.ReadAllBytes(externalMain));
                Environment.SetEnvironmentVariable("PATH", externalDirectory);
                await installer.InstallAsync("afs", storage, new Progress(), default);
                Check(Directory.Exists(Path.Combine(storage.ToolsDirectory, "fonttools")) && !Directory.Exists(Path.Combine(storage.ToolsDirectory, "assfontsubset")), "外部真实 AFS 只补独立依赖");
                await Remove(storage, "afs", ComponentRemovalKind.Dependencies);
                Check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(externalMain))), "卸载独立依赖保留外部 AFS 文件与哈希");
                var noBackend = workspace with { FontToolsDirectoryOverride = Path.Combine(storage.ToolsDirectory, "fonttools", "Scripts") };
                Check((await verifier.VerifyAsync(ComponentCatalog.All.Single(c => c.Id == "afs"), noBackend, default)).State == ComponentState.DependencyMissing, "依赖卸载后无其他后端时为缺少依赖");
                await installer.InstallAsync("afs", storage, new Progress(), default);
                Check((await verifier.VerifyAsync(ComponentCatalog.All.Single(c => c.Id == "afs"), workspace, default)).State == ComponentState.Available, "独立依赖卸载后重新安装恢复可用");
                Environment.SetEnvironmentVariable("PATH", "");
                await installer.InstallAsync("afs", storage, new Progress(), default);
                Check(!Directory.Exists(Path.Combine(storage.ToolsDirectory, "assfontsubset", "python")) && (await verifier.VerifyAsync(ComponentCatalog.All.Single(c => c.Id == "afs"), workspace, default)).State == ComponentState.Available, "重新安装 AFS 主程序复用真实独立依赖，不重复下载 Python");
                await Remove(storage, "afs");
                Check(Directory.Exists(Path.Combine(storage.ToolsDirectory, "fonttools")), "卸载 AFS 不连带删除独立依赖");
                await Remove(storage, "afs", ComponentRemovalKind.Dependencies);
                Check(!Directory.EnumerateDirectories(storage.ToolsDirectory).Any() && Directory.EnumerateFiles(storage.LogsDirectory, "uninstall-*.log", SearchOption.AllDirectories).Any(), "真实卸载清空目标组件并保留操作日志");
                return 0;
            }
            var data = Paths("manual"); data.EnsureDirectories();
            FileAt(Path.Combine(data.ConfigDirectory, "keep.json")); FileAt(Path.Combine(data.LogsDirectory, "keep.log"));
            FileAt(Path.Combine(data.ToolsDirectory, "keep.txt"));
            foreach (var release in ComponentReleases.All)
            {
                foreach (var required in release.Value.RequiredFiles) FileAt(Path.Combine(data.ToolsDirectory, release.Value.Directory, required));
                var result = await remover.UninstallAsync(ComponentDirectories.CreatePlan(data, release.Key, ComponentRemovalKind.Component), data, new Progress(), default);
                Check(!Directory.Exists(result.Directory) && File.Exists(result.LogPath), release.Key + " 手动副本可卸载并保留日志");
            }
            Check(File.Exists(Path.Combine(data.ToolsDirectory, "keep.txt")) && File.Exists(Path.Combine(data.ConfigDirectory, "keep.json")) && File.Exists(Path.Combine(data.LogsDirectory, "keep.log")), "保留组件根目录、用户配置、日志和其他文件");
            var afs = Paths("afs");
            FileAt(Path.Combine(afs.ToolsDirectory, "assfontsubset", "AssFontSubset.Console.exe"));
            FileAt(Path.Combine(afs.ToolsDirectory, "assfontsubset", "python", "Scripts", "ttx.exe"));
            FileAt(Path.Combine(afs.ToolsDirectory, "fonttools", "Scripts", "ttx.exe"));
            await Remove(afs, "afs");
            Check(!Directory.Exists(Path.Combine(afs.ToolsDirectory, "assfontsubset")) && Directory.Exists(Path.Combine(afs.ToolsDirectory, "fonttools")), "主程序卸载包含随包依赖，保留独立依赖");
            Check(ComponentDirectories.TryPlan(afs, "afs", ComponentRemovalKind.Dependencies) is not null, "主程序缺失后独立依赖仍可管理");
            FileAt(Path.Combine(afs.ToolsDirectory, "assfontsubset", "AssFontSubset.Console.exe"), "keep");
            await Remove(afs, "afs", ComponentRemovalKind.Dependencies);
            Check(File.ReadAllText(Path.Combine(afs.ToolsDirectory, "assfontsubset", "AssFontSubset.Console.exe")) == "keep", "独立依赖卸载保留主程序");
            var locked = Paths("locked"); var lockedFile = Path.Combine(locked.ToolsDirectory, "assfonts", "assfonts.exe"); FileAt(lockedFile);
            using (var file = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                await Failure(locked, ComponentDirectories.CreatePlan(locked, "assfonts", ComponentRemovalKind.Component), "文件占用拒绝卸载");
                Check(File.Exists(lockedFile), "占用失败不误报成功");
            }
            await Remove(locked, "assfonts"); Check(!Directory.Exists(Path.GetDirectoryName(lockedFile)), "释放占用后可重试成功");
            var readOnly = Paths("read-only"); var readOnlyFile = Path.Combine(readOnly.ToolsDirectory, "assfonts", "assfonts.exe"); FileAt(readOnlyFile);
            File.SetAttributes(readOnlyFile, FileAttributes.ReadOnly);
            await Failure(readOnly, ComponentDirectories.CreatePlan(readOnly, "assfonts", ComponentRemovalKind.Component), "只读文件预检查拒绝");
            Check(File.Exists(readOnlyFile), "只读失败保留原目录");
            File.SetAttributes(readOnlyFile, FileAttributes.Normal); await Remove(readOnly, "assfonts");
            var concurrent = Paths("concurrent"); FileAt(Path.Combine(concurrent.ToolsDirectory, "assfonts", "assfonts.exe"));
            var blocking = new BlockingProgress();
            var coordinator = new OperationCoordinator();
            var manager = new ManageComponentsUseCase(new InfrastructureComponentManager(new ComponentVerifier(), new ComponentInstaller(), remover), coordinator);
            var concurrentConfiguration = new C.ComponentConfiguration(concurrent.AppDataRoot, concurrent.ToolsDirectory, concurrent.LogsDirectory, concurrent.CacheDirectory);
            var concurrentPlan = await manager.PrepareUninstallAsync("assfonts", C.ComponentRemovalKind.Component, concurrentConfiguration);
            var removal = manager.UninstallAsync(concurrentPlan, concurrentConfiguration, new ProgressAdapter(blocking), default);
            await blocking.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                var blockedStorage = Paths("blocked-install");
                var blockedConfiguration = new C.ComponentConfiguration(blockedStorage.AppDataRoot, blockedStorage.ToolsDirectory, blockedStorage.LogsDirectory, blockedStorage.CacheDirectory);
                var installResult = await manager.InstallAsync("assfonts", blockedConfiguration);
                Check(installResult.Status == C.ComponentOperationStatus.Failed && !Directory.Exists(blockedStorage.ToolsDirectory), "卸载与真实安装服务共用守卫，不启动下载");
                var duplicate = await manager.UninstallAsync(concurrentPlan, concurrentConfiguration);
                Check(duplicate.Status == C.ComponentOperationStatus.Failed && Directory.Exists(Path.Combine(concurrent.ToolsDirectory, "assfonts")), "重复卸载不并行删除");
            }
            finally { blocking.Release(); }
            var completedRemoval = await removal; Check(completedRemoval.Status == C.ComponentOperationStatus.Succeeded && !Directory.Exists(Path.Combine(concurrent.ToolsDirectory, "assfonts")), "持有守卫的卸载正常收尾");
            var safe = Paths("safe"); FileAt(Path.Combine(safe.ToolsDirectory, "assfonts", "assfonts.exe"));
            var plan = ComponentDirectories.CreatePlan(safe, "assfonts", ComponentRemovalKind.Component);
            var outside = Path.Combine(root, "outside"); FileAt(Path.Combine(outside, "keep.txt"));
            await Failure(safe, plan with { TargetDirectory = outside }, "外部目标拒绝");
            await Failure(safe, plan with { TargetDirectory = safe.ToolsDirectory }, "根目录拒绝");
            await Failure(safe, plan with { Kind = ComponentRemovalKind.Dependencies }, "非 AFS 依赖类型拒绝");
            var next = Paths("next"); FileAt(Path.Combine(next.ToolsDirectory, "assfonts", "assfonts.exe"));
            await Failure(next, plan, "确认后路径变化拒绝");
            Check(File.Exists(Path.Combine(outside, "keep.txt")) && File.Exists(Path.Combine(safe.ToolsDirectory, "assfonts", "assfonts.exe")), "非法请求不删除确认范围外内容");
            using (var cancel = new CancellationTokenSource())
            {
                cancel.Cancel();
                try { await remover.UninstallAsync(plan, safe, new Progress(), cancel.Token); throw new Exception("预期取消"); }
                catch (OperationCanceledException) { Check(Directory.Exists(plan.TargetDirectory), "开始前取消不删除"); }
            }
            var junction = Path.Combine(plan.TargetDirectory, "linked");
            var junctionResult = await new ProcessRunner().RunAsync(new ProcessRequest(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), ["/d", "/c", "mklink", "/J", junction, outside]));
            Check(junctionResult.ExitCode == 0 && (File.GetAttributes(junction) & FileAttributes.ReparsePoint) != 0, "真实目录联接反例已建立");
            await Failure(safe, plan, "子目录联接拒绝");
            Check(File.Exists(Path.Combine(outside, "keep.txt")) && File.Exists(Path.Combine(plan.TargetDirectory, "assfonts.exe")), "链接检查在任何删除前完成且不遍历外部");
            var linkRoot = Paths("linked-root"); Directory.CreateDirectory(linkRoot.ToolsDirectory);
            var rootLink = Path.Combine(linkRoot.ToolsDirectory, "assfonts");
            await new ProcessRunner().RunAsync(new ProcessRequest(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), ["/d", "/c", "mklink", "/J", rootLink, outside]));
            Check(ComponentDirectories.TryPlan(linkRoot, "assfonts", ComponentRemovalKind.Component) is null, "组件目录本身是联接时不提供卸载");
            return 0;

            async Task Failure(StoragePaths paths, ComponentRemovalPlan requested, string name)
            {
                try { await remover.UninstallAsync(requested, paths, new Progress(), default); throw new Exception("预期失败 " + name); }
                catch (ComponentRemovalException ex) { Check(File.Exists(ex.LogPath), name + "保留失败详情和日志"); }
            }
        }
        catch (Exception ex) { error = ex.ToString(); Console.Error.WriteLine(ex); return 1; }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            await File.WriteAllTextAsync(Path.Combine(root, "report.json"), JsonSerializer.Serialize(new { Checks = checks, Passed = checks.Count, Failed = error is null ? 0 : 1, Error = error }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Component uninstall: {checks.Count} PASS; Artifacts: {root}");
        }
    }
    private sealed class ProgressAdapter(IProgress<ComponentRemovalProgress> sink) : IProgress<C.ComponentProgress>
    { public void Report(C.ComponentProgress value) => sink.Report(new(value.Message)); }
    private sealed class Progress : IProgress<ComponentRemovalProgress>, IProgress<ComponentInstallProgress>
    {
        public void Report(ComponentRemovalProgress value) { }
        public void Report(ComponentInstallProgress value) { }
    }
    private sealed class PackageHandler(string root) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var manifests = new[] { (ComponentReleases.All["mkvtoolnix"].Package, "mkv.zip"), (ComponentReleases.All["ffmpeg"].Package, "ffmpeg.zip"),
                (ComponentReleases.All["afs"].Package, "afs.zip"), (ComponentReleases.All["assfonts"].Package, "assfonts.zip"),
                (ComponentReleases.Python, "python.zip"), (ComponentReleases.FontTools, "fonttools.whl"), (ComponentReleases.Launchers, "pip.whl") };
            var file = manifests.Single(m => m.Item1.Url == request.RequestUri!.AbsoluteUri).Item2;
            var stream = File.OpenRead(Path.Combine(root, file));
            var content = new StreamContent(stream); content.Headers.ContentLength = stream.Length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
    private sealed class BlockingProgress : IProgress<ComponentRemovalProgress>
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => completion.TrySetResult();
        public void Report(ComponentRemovalProgress value) { Started.TrySetResult(); completion.Task.GetAwaiter().GetResult(); }
    }
}
