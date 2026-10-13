using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Application.Ports.Failures;

namespace MuxDeck.Application.Ports;

/// <summary>按业务能力查找工具。</summary>
/// <exception cref="ToolFailureException">工具退出码异常、输出解析失败或出现 Fatal 级诊断时抛出；Code 来自 ErrorCodes，Detail 保留原始原因。</exception>
/// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
public interface IToolCatalog
{
    /// <summary>按业务能力查找工具。</summary>
    /// <exception cref="ToolFailureException">工具退出码异常、输出解析失败或出现 Fatal 级诊断时抛出；Code 来自 ErrorCodes，Detail 保留原始原因。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task<EquatableArray<ToolDescriptor>> ResolveAsync(EquatableArray<ToolCapability> capabilities,
        ToolConfiguration tools, CancellationToken cancellationToken = default);
}
