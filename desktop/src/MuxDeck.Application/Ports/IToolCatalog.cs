using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Application.Ports.Failures;

namespace MuxDeck.Application.Ports;

/// <summary>按业务能力查找工具。</summary>
public interface IToolCatalog
{
    /// <summary>按业务能力查找工具。</summary>
    /// <exception cref="ToolFailureException">所需工具无法定位时抛出，Code 为 ErrorCodes.ToolNotFound，Message 与 Detail 保留原始原因。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task<EquatableArray<ToolDescriptor>> ResolveAsync(EquatableArray<ToolCapability> capabilities,
        ToolConfiguration tools, CancellationToken cancellationToken = default);
}
