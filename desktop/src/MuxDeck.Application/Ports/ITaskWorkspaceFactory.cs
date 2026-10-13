using MuxDeck.Contracts;

namespace MuxDeck.Application.Ports;

public interface ITaskWorkspaceFactory
{
    Task<ITaskWorkspace> CreateAsync(ToolConfiguration tools, CancellationToken cancellationToken = default);
}

// 每次任务独占，异步释放等待清理完成；日志保留，成功、失败和取消共用释放路径。
public interface ITaskWorkspace : IAsyncDisposable
{
    string WorkDirectory { get; }
    LogRef Log { get; }
    Task WriteLogAsync(string message, CancellationToken cancellationToken = default);
}
