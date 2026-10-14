namespace MuxDeck.Infrastructure.Components;

public sealed class ComponentUninstaller : IComponentUninstaller
{
    public async Task<ComponentRemovalResult> UninstallAsync(ComponentRemovalPlan plan, StoragePaths storage, IProgress<ComponentRemovalProgress> progress, CancellationToken cancellationToken)
        => await UninstallImplementationAsync(plan, storage, progress, cancellationToken).ConfigureAwait(false);
    private sealed class LegacyRemovalProgress(IProgress<ComponentRemovalProgress> progress) : IProgress<MuxDeck.Contracts.ComponentProgress>
    { public void Report(MuxDeck.Contracts.ComponentProgress value) => progress.Report(new(value.Message)); }

    private async Task<ComponentRemovalResult> UninstallImplementationAsync(ComponentRemovalPlan plan, StoragePaths storage, IProgress<ComponentRemovalProgress> progress, CancellationToken cancellationToken)
    {
        var logPath = Path.Combine(storage.LogsDirectory, "components", "uninstall-" + Guid.NewGuid().ToString("N") + ".log");
        StreamWriter? log = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            log = new StreamWriter(logPath) { AutoFlush = true };
            Write("卸载请求 " + plan.ComponentId + " " + plan.Kind + " " + plan.TargetDirectory);
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ComponentDirectories.Validate(plan, storage);
                ValidateTree(plan.TargetDirectory);
                cancellationToken.ThrowIfCancellationRequested();
                Report("正在卸载");
                // 实际删除一旦开始便完整收尾；关闭等待任务，不中途取消造成半成品。
                DeleteTree(plan.TargetDirectory);
            }, CancellationToken.None).ConfigureAwait(false);
            Write("卸载完成");
            return new(plan.TargetDirectory, logPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { Write("开始删除前已取消"); throw; }
        catch (Exception ex) { Write("卸载未完成 " + ex); throw new ComponentRemovalException("卸载未完成：" + ex.Message, logPath, ex); }
        finally
        {
            try { log?.Dispose(); }
            catch (IOException) { }
        }

        void ValidateTree(string directory)
        {
            CheckPath(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                CheckPath(entry);
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0) ValidateTree(entry);
                else
                {
                    if ((attributes & FileAttributes.ReadOnly) != 0) throw new IOException("文件为只读，请解除只读后重试：" + entry);
                    // 预检查明显占用/不可读文件，减少删除部分内容后才发现错误的情况。
                    using var file = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                }
            }
        }
        void DeleteTree(string directory)
        {
            CheckPath(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                CheckPath(entry);
                if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0) DeleteTree(entry);
                else { File.Delete(entry); Write("已删除 " + entry); }
            }
            CheckPath(directory);
            Directory.Delete(directory, recursive: false);
        }
        void CheckPath(string path)
        {
            if (!string.Equals(Path.GetFullPath(path), plan.TargetDirectory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                && !ComponentDirectories.IsInside(plan.TargetDirectory, path)) throw new IOException("拒绝删除确认目录以外的路径。");
            ComponentDirectories.EnsureNoLinks(path);
        }
        void Report(string message) { try { progress.Report(new(message)); } catch (Exception ex) { Write("进度订阅者异常 " + ex.Message); } }
        void Write(string message) { try { log?.WriteLine($"{DateTimeOffset.Now:O} {message}"); } catch (Exception ex) when (ex is IOException or ObjectDisposedException) { } }
    }
}
