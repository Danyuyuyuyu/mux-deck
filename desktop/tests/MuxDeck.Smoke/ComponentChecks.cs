using C = MuxDeck.Contracts;
using MuxDeck.Infrastructure;
using MuxDeck.Infrastructure.Muxing;
using MuxDeck.Infrastructure.Components;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using MuxDeck.Core;
using MuxDeck.Core.Components;

internal static class ComponentChecks
{
    public static async Task<int> RunAsync(bool real = false, string? workspaceRoot = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "mux-deck-components-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var count = 0;
        void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException(name);
            count++; Console.WriteLine("PASS: " + name);
        }
        try
        {
            if (real)
            {
                var workspace = Workspace.Discover(workspaceRoot);
                var verifier = new ComponentVerifier();
                foreach (var definition in ComponentCatalog.All.Where(c => c.Id != "assfonts"))
                {
                    var result = await verifier.VerifyAsync(definition, workspace, default);
                    Check(result.State == ComponentState.Available && result.Version is not null, definition.Name + " 真实启动/必要依赖验证：" + result.Version + " " + result.Reason);
                }
                var realStorage = StoragePaths.ForRoot(root, Path.Combine(root, "tools"));
                var installer = new AssfontsInstaller();
                var resultInstall = await installer.InstallAsync("assfonts", realStorage, new InlineProgress(_ => { }), default);
                var installed = await verifier.VerifyAsync(ComponentCatalog.All.Single(c => c.Id == "assfonts"), new Workspace(root) { ToolsDirectoryOverride = realStorage.ToolsDirectory }, default);
                Check(installed.State == ComponentState.Available && installed.Version == "0.7.3", "真实下载、固定哈希、解压、启动验证、提交和重新检测");
                Check(File.Exists(resultInstall.LogPath) && File.Exists(Path.Combine(resultInstall.Directory, "assfonts.exe")), "真实安装日志与成品目录");
                Check(!Directory.EnumerateDirectories(realStorage.ToolsDirectory).Any(p => Path.GetFileName(p).StartsWith(".muxdeck")) && !Directory.EnumerateDirectories(realStorage.CacheDirectory).Any(), "真实安装临时资源清理");
                return 0;
            }
            var tools = Path.Combine(root, "tools");
            foreach (var path in new[] { "mkvtoolnix/mkvmerge.exe", "mkvtoolnix/mkvextract.exe", "mkvtoolnix/mkvpropedit.exe", "ffmpeg/bin/ffmpeg.exe", "assfontsubset/AssFontSubset.Console.exe", "assfonts/assfonts.exe" })
            {
                var full = Path.Combine(tools, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, "controlled");
            }
            var ws = new Workspace(root) { ToolsDirectoryOverride = tools };
            var runner = new ProbeRunner();
            var inspector = new ComponentVerifier(runner, _ => null);
            var mkv = ComponentCatalog.All.Single(c => c.Id == "mkvtoolnix");
            Check((await inspector.VerifyAsync(mkv, ws, default)).State == ComponentState.Available, "三个 MKV 子工具全部验证通过才可用");
            runner.MixedVersion = true;
            Check((await inspector.VerifyAsync(mkv, ws, default)).State == ComponentState.Invalid, "混用版本不是可用");
            runner.MixedVersion = false;
            var afs = ComponentCatalog.All.Single(c => c.Id == "afs");
            Check((await inspector.VerifyAsync(afs, ws, default)).State == ComponentState.DependencyMissing, "AFS 主程序存在但后端缺失单独表达");
            var ffmpeg = ComponentCatalog.All.Single(c => c.Id == "ffmpeg");
            runner.NoFilters = true;
            Check((await inspector.VerifyAsync(ffmpeg, ws, default)).State == ComponentState.Invalid, "FFmpeg 无 subtitles 滤镜不能用于预览");
            runner.NoFilters = false;
            runner.ExitCode = 7;
            Check((await inspector.VerifyAsync(mkv, ws, default)).State == ComponentState.Invalid, "文件存在但启动失败不显示可用");
            runner.ExitCode = 0;
            runner.Malformed = true;
            Check((await inspector.VerifyAsync(mkv, ws, default)).State == ComponentState.Invalid, "未知版本输出不猜测可用");
            runner.Malformed = false;
            var sourceBytes = Zip(("assfonts.exe", "controlled executable"), ("LICENSE.txt", "fixture"));
            var package = Package(sourceBytes);
            var candidateVerifier = new CandidateVerifier();
            var phases = new List<ComponentInstallPhase>();
            var storage = StoragePaths.ForRoot(Path.Combine(root, "successful"), Path.Combine(root, "successful", "tools"));
            using var client = new HttpClient(new ArchiveHandler(sourceBytes));
            var install = new AssfontsInstaller(client, candidateVerifier, package);
            var resultOk = await install.InstallAsync("assfonts", storage, new InlineProgress(p => phases.Add(p.Phase)), default);
            Check(candidateVerifier.Calls == 1 && candidateVerifier.SawCandidate, "暂存候选先验证，再正式提交");
            Check(File.ReadAllText(Path.Combine(resultOk.Directory, "assfonts.exe")) == "controlled executable", "提交完整文件到设置的组件目录");
            Check(phases.Distinct().SequenceEqual(new[] { ComponentInstallPhase.Preparing, ComponentInstallPhase.Downloading, ComponentInstallPhase.Installing, ComponentInstallPhase.Verifying, ComponentInstallPhase.Completed }), "行内安装阶段按实际流程推进");
            Check(File.Exists(resultOk.LogPath), "安装日志保存在设置的工作目录");
            Check(!Directory.EnumerateDirectories(storage.CacheDirectory).Any() && !Directory.EnumerateDirectories(storage.ToolsDirectory).Any(p => Path.GetFileName(p).StartsWith(".muxdeck")), "成功后清理暂存文件");
            await Failure("已有目录", sourceBytes, package, _ => { }, storage);
            Check(File.ReadAllText(Path.Combine(resultOk.Directory, "assfonts.exe")) == "controlled executable", "重装拒绝覆盖现有组件");
            await Failure("哈希不符", sourceBytes, package with { Sha256 = new string('0', 64) });
            var traversal = Zip(("../outside.exe", "bad"), ("assfonts.exe", "fixture"));
            await Failure("越界 ZIP", traversal, Package(traversal));
            Check(!File.Exists(Path.Combine(root, "outside.exe")), "越界 ZIP 不写出所属目录");
            var duplicate = Zip(("assfonts.exe", "a"), ("ASSFONTS.EXE", "b"));
            await Failure("重复 ZIP", duplicate, Package(duplicate));
            var invalid = new byte[] { 1, 2, 3 };
            await Failure("损坏 ZIP", invalid, Package(invalid));
            var noExe = Zip(("readme.txt", "a"));
            await Failure("主程序缺失", noExe, Package(noExe));
            var failingProbe = new CandidateVerifier { Fail = true };
            await Failure("候选验证失败", sourceBytes, package, verifier: failingProbe);
            var networkStorage = StoragePaths.ForRoot(Path.Combine(root, "network"), Path.Combine(root, "network", "tools"));
            using var failingClient = new HttpClient(new ArchiveHandler(sourceBytes) { Fail = true });
            try { await new AssfontsInstaller(failingClient, candidateVerifier, package).InstallAsync("assfonts", networkStorage, new InlineProgress(_ => { }), default); throw new Exception("预期网络失败"); }
            catch (ComponentInstallException ex) { Check(ex.Message.Contains("网络") && File.Exists(ex.LogPath), "网络错误保留原因与日志"); }
            var cancelStorage = StoragePaths.ForRoot(Path.Combine(root, "cancelled"), Path.Combine(root, "cancelled", "tools"));
            using var cts = new CancellationTokenSource();
            try
            {
                await install.InstallAsync("assfonts", cancelStorage, new InlineProgress(p => { if (p.Phase == ComponentInstallPhase.Installing) cts.Cancel(); }), cts.Token);
                throw new Exception("预期取消");
            }
            catch (OperationCanceledException) { Check(!Directory.Exists(Path.Combine(cancelStorage.ToolsDirectory, "assfonts")), "取消不发布半成品"); }
            Check(!Directory.EnumerateDirectories(cancelStorage.CacheDirectory).Any() && !Directory.EnumerateDirectories(cancelStorage.ToolsDirectory).Any(), "取消后清理下载/暂存");
            var retry = await install.InstallAsync("assfonts", cancelStorage, new InlineProgress(_ => { }), default);
            Check(Directory.Exists(retry.Directory), "取消后可以重试成功");
            var race = StoragePaths.ForRoot(Path.Combine(root, "race"), Path.Combine(root, "race", "tools"));
            try
            {
                await install.InstallAsync("assfonts", race, new InlineProgress(p =>
                {
                    if (p.Phase == ComponentInstallPhase.Verifying) { Directory.CreateDirectory(Path.Combine(race.ToolsDirectory, "assfonts")); File.WriteAllText(Path.Combine(race.ToolsDirectory, "assfonts", "keep.txt"), "keep"); }
                }), default);
                throw new Exception("预期目录竞态失败");
            }
            catch (ComponentInstallException) { Check(File.ReadAllText(Path.Combine(race.ToolsDirectory, "assfonts", "keep.txt")) == "keep", "提交竞态不覆盖期间新出现的目录"); }
            return 0;

            async Task Failure(string name, byte[] bytes, ComponentPackage manifest, Action<ComponentInstallProgress>? callback = null, StoragePaths? paths = null, CandidateVerifier? verifier = null)
            {
                paths ??= StoragePaths.ForRoot(Path.Combine(root, name), Path.Combine(root, name, "tools"));
                using var fakeClient = new HttpClient(new ArchiveHandler(bytes));
                try { await new AssfontsInstaller(fakeClient, verifier ?? candidateVerifier, manifest).InstallAsync("assfonts", paths, new InlineProgress(p => callback?.Invoke(p)), default); throw new Exception("预期失败：" + name); }
                catch (ComponentInstallException ex) { Check(File.Exists(ex.LogPath), name + "保留安装失败日志"); }
                if (name != "已有目录") Check(!Directory.Exists(Path.Combine(paths.ToolsDirectory, "assfonts")), name + "未发布组件");
            }
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Console.WriteLine($"Components: {count} PASS; Artifacts: {root}"); }
    }
    private static ComponentPackage Package(byte[] bytes) => new("https://example.invalid/assfonts.zip", Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
    private static byte[] Zip(params (string Name, string Content)[] files)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var file in files) { using var writer = new StreamWriter(zip.CreateEntry(file.Name).Open()); writer.Write(file.Content); }
        return stream.ToArray();
    }
    private sealed class InlineProgress(Action<ComponentInstallProgress> callback) : IProgress<ComponentInstallProgress> { public void Report(ComponentInstallProgress value) => callback(value); }
    private sealed class ArchiveHandler(byte[] bytes) : HttpMessageHandler
    {
        public bool Fail { get; init; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Fail) throw new HttpRequestException("网络失败");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
    private sealed class CandidateVerifier : IComponentVerifier
    {
        public int Calls { get; private set; }
        public bool SawCandidate { get; private set; }
        public bool Fail { get; init; }
        public Task<MuxDeck.Infrastructure.Components.ComponentInspection> VerifyAsync(ComponentDefinition component, C.ComponentConfiguration configuration, CancellationToken cancellationToken)
        {
            Calls++;
            var path = Path.Combine(configuration.ToolsDirectory, "assfonts", "assfonts.exe");
            SawCandidate = File.Exists(path) && configuration.ToolsDirectory.Contains(".muxdeck-assfonts-");
            return Task.FromResult(new MuxDeck.Infrastructure.Components.ComponentInspection(Fail ? ComponentState.Invalid : ComponentState.Available, [new("assfonts", "", path)], "0.7.3", Fail ? "启动失败" : null));
        }
    }
    private sealed class ProbeRunner : IComponentProbeRunner
    {
        public bool MixedVersion { get; set; }
        public bool NoFilters { get; set; }
        public bool Malformed { get; set; }
        public int ExitCode { get; set; }
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            var tool = Path.GetFileNameWithoutExtension(request.Executable);
            var text = tool switch
            {
                "AssFontSubset.Console" => "2.2.0",
                "ffmpeg" => request.Arguments.Contains("-filters") ? NoFilters ? "no filters" : " T.. subtitles V->V render" : "ffmpeg version 8.1",
                _ => tool + (MixedVersion && tool == "mkvextract" ? " v99.0" : " v100.0")
            };
            return Task.FromResult(new ProcessResult(ExitCode, Malformed ? "unknown" : text, ""));
        }
    }
}
