using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Application.Ports.Failures;

namespace MuxDeck.Application.Ports;

/// <summary>单任务业务边界 IToolReadiness。</summary>
public interface IToolReadiness
{
    /// <summary>验证任务所需工具和字体后端可运行性。</summary>
    /// <exception cref="ToolFailureException">工具退出码异常、输出解析失败或出现 Fatal 级诊断时抛出；Code 来自 ErrorCodes，Detail 保留原始原因。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task EnsureReadyAsync(bool needsFonts, ToolCallContext context, CancellationToken cancellationToken = default);
}
