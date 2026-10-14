using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using MuxDeck.Contracts;

namespace MuxDeck.Infrastructure.Components;

public sealed record ManagedFontToolsPackages(ComponentPackage Python, ComponentPackage FontTools, ComponentPackage Launchers);

public sealed class ComponentInstaller(HttpClient? httpClient = null, IComponentVerifier? verifier = null,
    IReadOnlyDictionary<string, ComponentRelease>? releases = null, ManagedFontToolsPackages? dependencies = null, IComponentProbeRunner? dependencyProbeRunner = null) : IComponentInstaller
{
    private static readonly HttpClient DefaultClient = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly HttpClient client = httpClient ?? DefaultClient;
    private readonly IComponentVerifier checker = verifier ?? new ComponentVerifier();
    private readonly IReadOnlyDictionary<string, ComponentRelease> catalog = releases ?? ComponentReleases.All;
    private readonly ManagedFontToolsPackages backendPackages = dependencies ?? new(ComponentReleases.Python, ComponentReleases.FontTools, ComponentReleases.Launchers);
    private readonly IComponentProbeRunner backendRunner = dependencyProbeRunner ?? new ComponentProbeRunner();

    public async Task<ComponentInstallResult> InstallAsync(string id, StoragePaths storage, IProgress<ComponentInstallProgress> progress, CancellationToken cancellationToken)
    {
        if (!ComponentCatalog.SupportsInstall(id) || !catalog.ContainsKey(id)) throw new NotSupportedException("此组件当前不支持自动安装。");
        return await InstallImplementationAsync(id, storage, progress, cancellationToken).ConfigureAwait(false);
    }
    private sealed class LegacyInstallProgress(IProgress<ComponentInstallProgress> progress) : IProgress<MuxDeck.Contracts.ComponentProgress>
    {
        public void Report(MuxDeck.Contracts.ComponentProgress value) => progress.Report(new(Enum.Parse<ComponentInstallPhase>(value.Phase.ToString()), value.Message, value.Bytes, value.TotalBytes));
    }

    private async Task<ComponentInstallResult> InstallImplementationAsync(string id, StoragePaths storage, IProgress<ComponentInstallProgress> progress, CancellationToken cancellationToken)
    {
        if (!ComponentCatalog.SupportsInstall(id) || !catalog.TryGetValue(id, out var release)) throw new NotSupportedException("此组件当前不支持自动安装。");
        var runId = Guid.NewGuid().ToString("N");
        var cache = Path.Combine(storage.CacheDirectory, "component-install-" + runId);
        var stage = Path.Combine(storage.ToolsDirectory, ".muxdeck-" + id + "-" + runId);
        var currentWorkspace = new ComponentConfiguration(storage.AppDataRoot, storage.ToolsDirectory, storage.LogsDirectory, storage.CacheDirectory);
        var dependenciesOnly = id == "afs" && new ToolLocator(currentWorkspace).Inspect().Any(p => p.Name == "AssFontSubset" && p.Path is not null);
        var folder = dependenciesOnly ? "fonttools" : release.Directory;
        var target = Path.Combine(storage.ToolsDirectory, folder);
        var logPath = Path.Combine(storage.LogsDirectory, "components", runId + ".log");
        StreamWriter? log = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            log = new StreamWriter(logPath) { AutoFlush = true };
            Report(ComponentInstallPhase.Preparing, dependenciesOnly ? "准备安装独立 fonttools 依赖" : $"准备安装 {id} {release.Version}");
            // 用户已有目录不合并、不覆盖，正式落位只新增本次完整验证的候选。
            if (Directory.Exists(target) || File.Exists(target)) throw new IOException(folder + " 目标目录已存在，未覆盖。请检查目录内容或选择其他组件目录。");
            string? reusableBackend = null;
            if (id == "afs" && !dependenciesOnly && Directory.Exists(Path.Combine(storage.ToolsDirectory, "fonttools")))
            {
                var managed = ComponentDirectories.TryPlanCore(storage, id, ComponentRemovalKind.Dependencies);
                if (managed is null) throw new IOException("独立依赖目录包含链接或无法读取，请检查组件目录。");
                reusableBackend = Path.Combine(managed.TargetDirectory, "Scripts");
                if (!await CanReuseBackendAsync(reusableBackend, cancellationToken).ConfigureAwait(false))
                    throw new IOException("独立 fonttools 依赖无法运行，请卸载独立依赖后重试安装。");
                Write("复用可运行的独立依赖 " + reusableBackend);
            }
            var candidate = Path.Combine(stage, folder);
            var executablePaths = dependenciesOnly ? Array.Empty<string>() : release.RequiredFiles.Select(file => Path.Combine(candidate, file));
            if (id == "afs") executablePaths = executablePaths.Append(Path.Combine(candidate, dependenciesOnly ? "Scripts" : "python/Scripts", "pyftsubset.exe"));
            // Win32 启动进程仍受可执行路径长度限制，下载前给出可操作的目录提示。
            if (executablePaths.Any(path => Path.GetFullPath(path).Length >= 260)) throw new IOException("组件目录路径过长，无法启动暂存工具。请选择较短的组件目录后重试。");
            Directory.CreateDirectory(cache);
            Directory.CreateDirectory(stage);
            var archive = dependenciesOnly ? null : await DownloadAsync(release.Package, "组件", "package.zip");
            string? pythonZip = null, fontToolsZip = null, launcherZip = null;
            if (id == "afs" && reusableBackend is null)
            {
                pythonZip = await DownloadAsync(backendPackages.Python, "Python", "python.zip");
                fontToolsZip = await DownloadAsync(backendPackages.FontTools, "fonttools", "fonttools.whl");
                launcherZip = await DownloadAsync(backendPackages.Launchers, "启动器", "launchers.whl");
            }
            // 所有依赖先下载并通过哈希检查，再进入安装阶段，避免条目状态来回跳转。
            Report(ComponentInstallPhase.Installing, "正在安装");
            if (!dependenciesOnly)
            {
                var unpacked = Path.Combine(stage, "archive");
                await ExtractAsync(archive!, unpacked, release.Package, cancellationToken).ConfigureAwait(false);
                var content = release.ArchiveRoot.Length == 0 ? unpacked : Path.Combine(unpacked, release.ArchiveRoot);
                if (!Directory.Exists(content)) throw new IOException("安装包目录结构与固定发布包不符。");
                foreach (var required in release.RequiredFiles)
                    if (!File.Exists(Path.Combine(content, required))) throw new IOException("安装包缺少 " + required + "。");
                Directory.Move(content, candidate);
            }
            string? backend = reusableBackend;
            if (id == "afs" && reusableBackend is null)
            {
                // 主程序与依赖一起暂存；只装依赖时独立提交 fonttools 文件夹。
                var pythonRoot = dependenciesOnly ? candidate : Path.Combine(candidate, "python");
                Report(ComponentInstallPhase.Installing, "正在配置独立 Python/fonttools");
                await ExtractAsync(pythonZip!, pythonRoot, backendPackages.Python, cancellationToken).ConfigureAwait(false);
                await ExtractAsync(fontToolsZip!, Path.Combine(pythonRoot, "Lib", "site-packages"), backendPackages.FontTools, cancellationToken).ConfigureAwait(false);
                var launcherRoot = Path.Combine(stage, "launchers");
                await ExtractAsync(launcherZip!, launcherRoot, backendPackages.Launchers, cancellationToken).ConfigureAwait(false);
                await ConfigureBackendAsync(pythonRoot, launcherRoot, cancellationToken).ConfigureAwait(false);
                backend = Path.Combine(pythonRoot, "Scripts");
            }
            Report(ComponentInstallPhase.Verifying, "验证组件和必要依赖");
            // 显式指定暂存后端，避免用户已有 Python 掩盖候选环境的问题。
            var stagedWorkspace = dependenciesOnly ? currentWorkspace with { FontToolsDirectoryOverride = backend }
                : new ComponentConfiguration(stage, stage, storage.LogsDirectory, storage.CacheDirectory, backend);
            var component = ComponentCatalog.All.Single(c => c.Id == id);
            // 候选验证属于已有安装租约，关闭期间仍允许它收尾。
            var inspection = await checker.VerifyAsync(component, stagedWorkspace, cancellationToken).ConfigureAwait(false);
            if (inspection.State != ComponentState.Available) throw new IOException(inspection.Reason ?? "组件启动验证失败。");
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(candidate, target);
            Report(ComponentInstallPhase.Completed, "安装完成，重新检测");
            return new(target, logPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { Write("安装已取消"); throw; }
        catch (Exception ex) { Write("安装失败：" + ex); throw new ComponentInstallException(ex.Message, logPath, ex); }
        finally
        {
            try { Cleanup(stage, log); Cleanup(cache, log); }
            finally { log?.Dispose(); }
        }

        async Task<string> DownloadAsync(ComponentPackage package, string label, string fileName)
        {
            if (new Uri(package.Url).Scheme != Uri.UriSchemeHttps) throw new IOException("安装来源必须使用 HTTPS。");
            var archive = Path.Combine(cache, fileName);
            Report(ComponentInstallPhase.Downloading, "下载 " + label, 0, package.Size);
            using var response = await client.GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != package.Size) throw new IOException("下载文件大小与固定发布包不符。");
            await using (var from = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var to = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                var buffer = new byte[65536]; long downloaded = 0; int count;
                var nextUpdate = DateTime.UtcNow;
                while ((count = await from.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    downloaded += count;
                    if (downloaded > package.Size) throw new IOException("下载文件超出固定发布包大小。");
                    await to.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    if (DateTime.UtcNow >= nextUpdate)
                    {
                        Report(ComponentInstallPhase.Downloading, "下载 " + label, downloaded, package.Size);
                        nextUpdate = DateTime.UtcNow.AddMilliseconds(150);
                    }
                }
                if (downloaded != package.Size) throw new IOException("下载未完成，文件大小不符。");
                Report(ComponentInstallPhase.Downloading, "下载 " + label, downloaded, package.Size);
            }
            await using var file = File.OpenRead(archive);
            var digest = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
            if (!digest.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("下载文件 SHA-256 校验失败。");
            return archive;
        }
        void Report(ComponentInstallPhase phase, string message, long bytes = 0, long? total = null)
        {
            Write($"{DateTimeOffset.Now:O} {phase} {message} {bytes}/{total}");
            try { progress.Report(new(phase, message, bytes, total)); }
            catch (Exception ex) { Write("进度订阅者异常：" + ex.Message); }
        }
        void Write(string text)
        {
            try { log?.WriteLine(text); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
        }
    }

    private async Task<bool> CanReuseBackendAsync(string scripts, CancellationToken token)
    {
        foreach (var (name, argument) in new[] { ("pyftsubset.exe", "--help"), ("ttx.exe", "--version") })
        {
            var path = Path.Combine(scripts, name);
            if (!File.Exists(path)) return false;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                ComponentDirectories.EnsureNoLinks(path);
                var result = await backendRunner.RunAsync(new(path, [argument], MaxCapturedCharacters: 128 * 1024), timeout.Token).ConfigureAwait(false);
                if (result.ExitCode != 0 || result.CaptureLimitExceeded) return false;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { return false; }
        }
        return true;
    }

    private static async Task ConfigureBackendAsync(string pythonRoot, string launcherRoot, CancellationToken token)
    {
        var launcher = Path.Combine(launcherRoot, "pip", "_vendor", "distlib", "t64.exe");
        if (!File.Exists(Path.Combine(pythonRoot, "python.exe")) || !File.Exists(Path.Combine(pythonRoot, "python312.zip")) ||
            !File.Exists(Path.Combine(pythonRoot, "Lib", "site-packages", "fontTools", "__init__.py")) || !File.Exists(launcher))
            throw new IOException("独立 Python/fonttools 包不完整。");
        // _pth 隔离用户 site-packages 和 PYTHONPATH，不调用 pip 或改写系统环境。
        await File.WriteAllTextAsync(Path.Combine(pythonRoot, "python312._pth"), "python312.zip\n.\nLib/site-packages\n", new UTF8Encoding(false), token).ConfigureAwait(false);
        var scripts = Path.Combine(pythonRoot, "Scripts"); Directory.CreateDirectory(scripts);
        var stub = await File.ReadAllBytesAsync(launcher, token).ConfigureAwait(false);
        foreach (var (name, module) in new[] { ("pyftsubset", "fontTools.subset"), ("ttx", "fontTools.ttx") })
        {
            using var payload = new MemoryStream();
            using (var zip = new ZipArchive(payload, ZipArchiveMode.Create, true))
            using (var writer = new StreamWriter(zip.CreateEntry("__main__.py").Open(), new UTF8Encoding(false)))
                await writer.WriteAsync($"import sys\nfrom {module} import main\nsys.exit(main())\n".AsMemory(), token).ConfigureAwait(false);
            await using var output = new FileStream(Path.Combine(scripts, name + ".exe"), FileMode.CreateNew);
            await output.WriteAsync(stub, token).ConfigureAwait(false);
            // distlib 支持相对于启动器解析；提交/移动便携目录后不残留暂存绝对路径。
            await output.WriteAsync(Encoding.UTF8.GetBytes("#!\"<launcher_dir>\\..\\python.exe\"\n"), token).ConfigureAwait(false);
            await output.WriteAsync(payload.ToArray(), token).ConfigureAwait(false);
        }
        var license = Path.Combine(launcherRoot, "pip", "_vendor", "distlib", "LICENSE.txt");
        if (File.Exists(license)) File.Copy(license, Path.Combine(pythonRoot, "distlib-LICENSE.txt"));
    }

    private static async Task ExtractAsync(string archive, string root, ComponentPackage package, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 2000) throw new IOException("压缩包条目过多。");
        Directory.CreateDirectory(root);
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long expanded = 0;
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            var name = entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var destination = Path.GetFullPath(Path.Combine(root, name));
            if (Path.IsPathRooted(name) || name.Contains(':') || !destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !names.Add(destination) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new IOException("压缩包含越界、重复或链接路径。");
            expanded += entry.Length;
            if (expanded > package.MaxExpandedBytes) throw new IOException("解压体积超出限制。");
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(destination); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var from = entry.Open();
            await using var to = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[65536]; long written = 0; int bytes;
            while ((bytes = await from.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                written += bytes;
                if (written > entry.Length) throw new IOException("解压体积超出条目声明。");
                await to.WriteAsync(buffer.AsMemory(0, bytes), token).ConfigureAwait(false);
            }
            if (written != entry.Length) throw new IOException("解压文件大小不符。");
        }
    }

    private static void Cleanup(string path, StreamWriter? log)
    {
        try { if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) Directory.Delete(path, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { try { log?.WriteLine("临时目录清理失败：" + ex.Message); } catch (IOException) { } }
    }
}
