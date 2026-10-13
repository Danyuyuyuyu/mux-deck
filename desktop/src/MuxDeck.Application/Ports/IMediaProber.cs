using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Application.Ports.Failures;

namespace MuxDeck.Application.Ports;

/// <summary>探测媒体并返回统一轨道事实。</summary>
/// <exception cref="ToolFailureException">工具退出码异常、输出解析失败或出现 Fatal 级诊断时抛出；Code 来自 ErrorCodes，Detail 保留原始原因。</exception>
/// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
public interface IMediaProber
{
    /// <summary>探测媒体并返回统一轨道事实。</summary>
    /// <exception cref="ToolFailureException">工具退出码异常、输出解析失败或出现 Fatal 级诊断时抛出；Code 来自 ErrorCodes，Detail 保留原始原因。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task<MediaProbeResult> ProbeAsync(string path, ToolCallContext context,
        CancellationToken cancellationToken = default);
}
