using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Application.Ports.Failures;

namespace MuxDeck.Application.Ports;

/// <summary>检查任务需要的媒体工具及字体后端能力，缺少字体后端用独立错误码标识。</summary>
public interface IToolReadiness
{
    /// <summary>验证任务所需工具和字体后端可运行性。</summary>
    /// <exception cref="ToolFailureException">工具退出码异常、输出解析失败或出现 Fatal 级诊断时抛出；字体后端定位缺失使用 ErrorCodes.FontBackendNotFound，其他 Code 来自 ErrorCodes，Detail 保留原始原因。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task EnsureReadyAsync(bool needsFonts, ToolCallContext context, CancellationToken cancellationToken = default);
}
