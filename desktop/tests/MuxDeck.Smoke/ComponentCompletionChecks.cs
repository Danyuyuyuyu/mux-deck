using MuxDeck.Desktop.Composition;
using MuxDeck.Infrastructure;
using MuxDeck.Infrastructure.Muxing;
using MuxDeck.Infrastructure.Components;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using MuxDeck.Core;
using MuxDeck.Core.Components;
using MuxDeck.Core.Muxing;

internal static class ComponentCompletionChecks
{
    public static async Task<int> RunAsync(bool real = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "mux-deck-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var checks = new List<string>();
        string? error = null;
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); checks.Add(name); Console.WriteLine("PASS: " + name); }
        try
        {
            // 无全局 Python/AFS 回退，验证组件包自身及独立后端。
            Environment.SetEnvironmentVariable("PATH", "");
            if (real)
            {
                var storage = StoragePaths.ForRoot(Path.Combine(root, "data"), Path.Combine(root, "tools 空格 中文"));
                var installer = new ComponentInstaller();
                var verifier = new ComponentVerifier();
                foreach (var id in new[] { "mkvtoolnix", "ffmpeg", "afs" })
                {
                    var result = await installer.InstallAsync(id, storage, new Progress(_ => { }), default);
                    var inspection = await verifier.VerifyAsync(ComponentCatalog.All.Single(c => c.Id == id), new Workspace(root) { ToolsDirectoryOverride = storage.ToolsDirectory }, default);
                    var release = ComponentReleases.All[id];
                    Check(inspection.State == ComponentState.Available && inspection.Version?.Split('+', '-')[0] == (release.ReportedVersion ?? release.Version), id + " 真实下载/哈希/布局/启动/提交/重检测 " + inspection.Version + " " + inspection.Reason);
                    Check(File.Exists(result.LogPath), id + " 真实安装日志");
                }
                var workspace = new Workspace(root) { ToolsDirectoryOverride = storage.ToolsDirectory, StorageOverride = storage };
                var backend = FontToolsLocator.Find(workspace);
                Check(backend == Path.Combine(storage.ToolsDirectory, "assfontsubset", "python", "Scripts"), "未安装用户 Python 时选择组件自带后端");
                await VerifyMux(workspace, root, Check);
                // 将已安装便携目录换位置，再验证相对启动器及真实 AFS 主程序。
                var moved = Path.Combine(root, "moved 中文 tools"); Directory.Move(storage.ToolsDirectory, moved);
                var movedWorkspace = workspace with { ToolsDirectoryOverride = moved };
                Check((await verifier.VerifyAsync(ComponentCatalog.All.Single(c => c.Id == "afs"), movedWorkspace, default)).State == ComponentState.Available, "移动组件目录后独立后端仍可运行");
                // 已有主程序只安装依赖，验证不改写主程序且实际检测使用新依赖。
                var externalRoot = Path.Combine(root, "dependency-only");
                var externalStorage = StoragePaths.ForRoot(Path.Combine(externalRoot, "data"), Path.Combine(externalRoot, "tools"));
                var afsPath = Path.Combine(externalStorage.ToolsDirectory, "assfontsubset", "AssFontSubset.Console.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(afsPath)!);
                File.Copy(Path.Combine(moved, "assfontsubset", "AssFontSubset.Console.exe"), afsPath);
                var before = SHA256.HashData(File.ReadAllBytes(afsPath));
                var depsResult = await installer.InstallAsync("afs", externalStorage, new Progress(_ => { }), default);
                var externalWorkspace = new Workspace(externalRoot) { ToolsDirectoryOverride = externalStorage.ToolsDirectory };
                Check(depsResult.Directory.EndsWith("fonttools") && before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(afsPath))), "只装依赖不覆盖已有 AFS 主程序");
                Check((await verifier.VerifyAsync(ComponentCatalog.All.Single(c => c.Id == "afs"), externalWorkspace, default)).State == ComponentState.Available, "独立依赖落位后已有 AFS 可用");
                Check(new[] { moved, externalStorage.ToolsDirectory }.All(directory => !Directory.EnumerateDirectories(directory).Any(p => Path.GetFileName(p).StartsWith(".muxdeck"))) &&
                    new[] { storage.CacheDirectory, externalStorage.CacheDirectory }.All(directory => !Directory.EnumerateDirectories(directory).Any(p => Path.GetFileName(p).StartsWith("component-install-"))), "真实三组件安装清理其暂存和下载缓存");
                return 0;
            }
            var downloads = new Dictionary<string, byte[]>();
            ComponentPackage Package(string key, byte[] bytes)
            {
                var url = "https://example.invalid/" + key; downloads[url] = bytes;
                return new(url, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
            }
            var releases = new Dictionary<string, ComponentRelease>
            {
                ["mkvtoolnix"] = new("mkvtoolnix", "101.0", Package("mkv", Zip(("mkvtoolnix/mkvmerge.exe", "m"), ("mkvtoolnix/mkvextract.exe", "e"), ("mkvtoolnix/mkvpropedit.exe", "p"))), "mkvtoolnix", ["mkvmerge.exe", "mkvextract.exe", "mkvpropedit.exe"]),
                ["ffmpeg"] = new("ffmpeg", "8.1.1", Package("ffmpeg", Zip(("release/bin/ffmpeg.exe", "ff"), ("release/bin/ffprobe.exe", "probe"), ("release/bin/ffplay.exe", "play"))), "release", ["bin/ffmpeg.exe", "bin/ffprobe.exe", "bin/ffplay.exe"]),
                ["afs"] = new("assfontsubset", "2.2.0", Package("afs", Zip(("AssFontSubset.Console.exe", "afs"))), "", ["AssFontSubset.Console.exe"])
            };
            var deps = new ManagedFontToolsPackages(
                Package("python", Zip(("python.exe", "exe"), ("python312.zip", "stdlib"), ("python312._pth", "old"))),
                Package("fonttools", Zip(("fontTools/__init__.py", ""))),
                Package("launchers", Zip(("pip/_vendor/distlib/t64.exe", "launcher"), ("pip/_vendor/distlib/LICENSE.txt", "license"))));
            using var client = new HttpClient(new Handler(downloads));
            var verifierFake = new Verifier();
            var installerFake = new ComponentInstaller(client, verifierFake, releases, deps);
            StoragePaths Paths(string name) => StoragePaths.ForRoot(Path.Combine(root, name, "data"), Path.Combine(root, name, "tools"));
            foreach (var id in releases.Keys)
            {
                var paths = Paths(id);
                var result = await installerFake.InstallAsync(id, paths, new Progress(_ => { }), default);
                foreach (var file in releases[id].RequiredFiles) Check(File.Exists(Path.Combine(result.Directory, file)), id + " 发布包按工具检测布局安装 " + file);
                Check(verifierFake.SawCandidate, id + " 检测正式落位前的候选组件");
                Check(!Directory.EnumerateDirectories(paths.CacheDirectory).Any() && !Directory.EnumerateDirectories(paths.ToolsDirectory).Any(p => Path.GetFileName(p).StartsWith(".muxdeck")), id + " 清理暂存");
            }
            var afs = Paths("afs");
            var scripts = FontToolsLocator.Find(new Workspace(root) { ToolsDirectoryOverride = afs.ToolsDirectory });
            Check(scripts == Path.Combine(afs.ToolsDirectory, "assfontsubset", "python", "Scripts"), "组件验证和封装共用独立依赖定位");
            Check(File.ReadAllText(Path.Combine(afs.ToolsDirectory, "assfontsubset", "python", "python312._pth")) == "python312.zip\n.\nLib/site-packages\n", "独立依赖不导入用户 site-packages/PYTHONPATH");
            Check(File.ReadAllText(Path.Combine(scripts!, "ttx.exe")).Contains("<launcher_dir>\\..\\python.exe"), "启动器不携带暂存绝对路径");
            var dependencyOnly = Paths("dependencies-only");
            var existing = Path.Combine(dependencyOnly.ToolsDirectory, "assfontsubset", "AssFontSubset.Console.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(existing)!); File.WriteAllText(existing, "keep");
            var depResult = await installerFake.InstallAsync("afs", dependencyOnly, new Progress(_ => { }), default);
            Check(depResult.Directory.EndsWith("fonttools") && File.ReadAllText(existing) == "keep", "仅缺依赖时新增独立依赖目录，主程序不变");
            Check(verifierFake.SawBackendOverride, "依赖候选验证不回退到全局 Python");
            foreach (var id in releases.Keys)
            {
                var bad = releases.ToDictionary(p => p.Key, p => p.Value);
                bad[id] = bad[id] with { Package = bad[id].Package with { Sha256 = new string('0', 64) } };
                await Failure(id, new ComponentInstaller(client, verifierFake, bad, deps), Paths("hash-" + id), "主包哈希失败");
                await Failure(id, installerFake, id == "afs" ? dependencyOnly : Paths(id), "已有目录不覆盖");
            }
            var badDeps = deps with { FontTools = deps.FontTools with { Sha256 = new string('0', 64) } };
            await Failure("afs", new ComponentInstaller(client, verifierFake, releases, badDeps), Paths("bad-dependencies"), "依赖校验失败不发布主程序");
            var malformed = releases.ToDictionary(p => p.Key, p => p.Value);
            malformed["ffmpeg"] = malformed["ffmpeg"] with { ArchiveRoot = "wrong" };
            await Failure("ffmpeg", new ComponentInstaller(client, verifierFake, malformed, deps), Paths("layout"), "未知压缩包目录结构失败");
            var incomplete = releases.ToDictionary(p => p.Key, p => p.Value);
            incomplete["mkvtoolnix"] = incomplete["mkvtoolnix"] with { RequiredFiles = ["missing.exe"] };
            await Failure("mkvtoolnix", new ComponentInstaller(client, verifierFake, incomplete, deps), Paths("missing-tool"), "缺少子工具不发布");
            var longPaths = Paths("long") with { ToolsDirectory = Path.Combine(root, new string('x', 150), "tools") };
            await Failure("mkvtoolnix", installerFake, longPaths, "原生可执行路径过长在下载前拒绝");
            foreach (var id in releases.Keys)
            {
                using var cancel = new CancellationTokenSource();
                var paths = Paths("cancel-" + id);
                try
                {
                    await installerFake.InstallAsync(id, paths, new Progress(p => { if (p.Phase == ComponentInstallPhase.Verifying) cancel.Cancel(); }), cancel.Token);
                    throw new InvalidOperationException("取消未生效");
                }
                catch (OperationCanceledException) { Check(!Directory.Exists(Path.Combine(paths.ToolsDirectory, releases[id].Directory)), id + " 取消不发布"); }
                Check(!Directory.EnumerateDirectories(paths.CacheDirectory).Any() && !Directory.EnumerateDirectories(paths.ToolsDirectory).Any(), id + " 取消清理完整");
            }
            return 0;

            async Task Failure(string id, ComponentInstaller installer, StoragePaths paths, string message)
            {
                var existed = Directory.Exists(Path.Combine(paths.ToolsDirectory, releases[id].Directory));
                try { await installer.InstallAsync(id, paths, new Progress(_ => { }), default); throw new InvalidOperationException("预期失败"); }
                catch (ComponentInstallException ex) { Check(File.Exists(ex.LogPath), id + " " + message + "保留错误日志"); }
                if (!existed) Check(!Directory.Exists(Path.Combine(paths.ToolsDirectory, releases[id].Directory)), id + " " + message + "无正式半成品");
            }
        }
        catch (Exception ex) { error = ex.ToString(); Console.Error.WriteLine(ex); return 1; }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            await File.WriteAllTextAsync(Path.Combine(root, "report.json"), JsonSerializer.Serialize(new { Real = real, Checks = checks, Passed = checks.Count, Failed = error is null ? 0 : 1, Error = error }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Component completion: {checks.Count} PASS; Artifacts: {root}");
        }
    }

    private static async Task VerifyMux(Workspace workspace, string root, Action<bool, string> check)
    {
        var fonts = Path.Combine(root, "fonts"); Directory.CreateDirectory(fonts);
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "arial.ttf"), Path.Combine(fonts, "arial.ttf"));
        var video = Path.Combine(root, "source.mkv");
        var ffmpeg = new ToolLocator(workspace).Inspect().Single(p => p.Name == "ffmpeg").Path!;
        var generated = await new ProcessRunner().RunAsync(new ProcessRequest(ffmpeg, ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=black:s=320x180:r=24:d=1", "-c:v", "libx264", "-pix_fmt", "yuv420p", video]));
        check(generated.ExitCode == 0 && File.Exists(video), "新安装 FFmpeg 生成真实隔离素材");
        var subtitle = Path.Combine(root, "test.ass");
        await File.WriteAllTextAsync(subtitle, """
            [Script Info]
            ScriptType: v4.00+
            PlayResX: 320
            PlayResY: 180
            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
            Style: Default,Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,1,0,2,10,10,10,1
            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:00.00,0:00:00.90,Default,,0,0,0,,Managed fonttools test
            """);
        var result = await AppComposition.CreateSingleService(workspace).RunAsync(new(video, [new(subtitle, "Test", "eng", true)], fonts, Path.Combine(root, "output")), new MuxProgressSink(), default);
        check(result.Status == MuxStatus.Succeeded && File.Exists(result.OutputPath), "新安装三组件与独立后端完成真实 ASS 子集、封装和任务 QC：" + result.Error);
        check(File.ReadAllText(result.LogPath!).Contains(Path.Combine(workspace.ToolsDirectory, "assfontsubset", "python", "Scripts")), "真实任务使用 Mux Deck 管理的依赖目录");
        check(!Directory.EnumerateFiles(workspace.TaskCacheDirectory, "*", SearchOption.AllDirectories).Any(), "真实任务完成后缓存工作数据已清理");
    }
    private sealed class MuxProgressSink : IProgress<MuxProgress> { public void Report(MuxProgress value) { } }
    private sealed class Progress(Action<ComponentInstallProgress> callback) : IProgress<ComponentInstallProgress> { public void Report(ComponentInstallProgress value) => callback(value); }
    private static byte[] Zip(params (string Name, string Content)[] files)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var file in files) { using var writer = new StreamWriter(zip.CreateEntry(file.Name).Open()); writer.Write(file.Content); }
        return stream.ToArray();
    }
    private sealed class Handler(Dictionary<string, byte[]> downloads) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(downloads[request.RequestUri!.AbsoluteUri]) });
    }
    private sealed class Verifier : IComponentVerifier
    {
        public bool SawCandidate { get; private set; }
        public bool SawBackendOverride { get; private set; }
        public Task<ComponentInspection> VerifyAsync(ComponentDefinition component, Workspace workspace, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SawCandidate = workspace.ToolsDirectory.Contains(".muxdeck-");
            if (component.Id == "afs") SawBackendOverride = workspace.FontToolsDirectoryOverride is { } path && File.Exists(Path.Combine(path, "ttx.exe"));
            return Task.FromResult(new ComponentInspection(ComponentState.Available, [], "fixture"));
        }
    }
}
