using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Application.Ports.Failures;

namespace MuxDeck.Application.Ports;

/// <summary>创建本次任务独占工作区。</summary>
public interface ITaskWorkspaceFactory
{
    /// <summary>创建本次任务独占工作区。</summary>
    /// <exception cref="ToolFailureException">工具退出码异常、输出解析失败或出现 Fatal 级诊断时抛出；Code 来自 ErrorCodes，Detail 保留原始原因。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task<ITaskWorkspace> CreateAsync(ToolConfiguration tools, CancellationToken cancellationToken = default);
}

/// <summary>日志保留；成功、失败、取消与异常路径都异步释放工作区并等待清理完成。</summary>
public interface ITaskWorkspace : IAsyncDisposable
{
    /// <summary>只读工作目录字符串，不查询环境。</summary>
    string WorkDirectory { get; }

    /// <summary>任务日志引用，清理中保留。</summary>
    LogRef Log { get; }

    /// <summary>写入任务日志。</summary>
    /// <exception cref="ToolFailureException">工具退出码异常、输出解析失败或出现 Fatal 级诊断时抛出；Code 来自 ErrorCodes，Detail 保留原始原因。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task WriteLogAsync(string message, CancellationToken cancellationToken = default);
}
