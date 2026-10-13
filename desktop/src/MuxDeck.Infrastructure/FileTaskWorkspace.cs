using System.Text;
using MuxDeck.Application.Ports;
using MuxDeck.Contracts;

namespace MuxDeck.Infrastructure;

public sealed class FileTaskWorkspaceFactory(IClock? clock = null) : ITaskWorkspaceFactory
{
    public Task<ITaskWorkspace> CreateAsync(ToolConfiguration tools, CancellationToken cancellationToken = default) =>
        PortBoundary.Guard<ITaskWorkspace>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runId = (clock ?? new SystemClock()).UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
            var run = Path.Combine(tools.LogsDirectory, runId);
            Directory.CreateDirectory(run);
            var work = Path.Combine(tools.CacheDirectory, runId, "work");
            // 创建失败时也释放已打开的日志句柄。
            var result = new FileTaskWorkspace(work, Path.Combine(run, "task.log"), tools.CacheDirectory);
            try { Directory.CreateDirectory(work); return Task.FromResult<ITaskWorkspace>(result); }
            catch { result.CloseLog(); throw; }
        });
}

public sealed class FileTaskWorkspace : ITaskWorkspace
{
    private readonly string ownedRoot;
    private readonly StreamWriter writer;
    private readonly object gate = new();
    private bool disposed;
    internal FileTaskWorkspace(string work, string log, string cache)
    {
        WorkDirectory = work; Log = new(log);
        ownedRoot = Path.GetFullPath(cache).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        writer = new(log, append: false, new UTF8Encoding(false)) { AutoFlush = true };
    }
    public string WorkDirectory { get; }
    public LogRef Log { get; }
    public Task WriteLogAsync(string message, CancellationToken cancellationToken = default) => PortBoundary.Guard(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); writer.WriteLine(DateTimeOffset.Now.ToString("O") + " " + message); }
        return Task.FromResult(true);
    });
    // 旧入口在提交后和清理路径采用尽力日志，不能推翻已提交的成品或掩盖原始失败。
    public void Write(string text)
    {
        try { WriteLogAsync(text).GetAwaiter().GetResult(); }
        catch (MuxDeck.Application.Ports.Failures.ToolFailureException) { }
    }
    internal void CloseLog() { lock (gate) { disposed = true; writer.Dispose(); } }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        await PortBoundary.Guard(() =>
        {
            try
            {
                var resolved = Path.GetFullPath(WorkDirectory);
                if (!resolved.StartsWith(ownedRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                    || Path.GetFileName(resolved) != "work")
                    throw new IOException("拒绝清理任务目录以外的路径：" + resolved);
                if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
                return Task.FromResult(true);
            }
            catch (Exception ex) { Write("临时资源清理失败：" + ex.Message); throw; }
            finally { CloseLog(); }
        }).ConfigureAwait(false);
    }
}
