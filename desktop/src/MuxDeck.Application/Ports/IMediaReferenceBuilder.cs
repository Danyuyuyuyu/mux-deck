using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Application.Ports.Failures;

namespace MuxDeck.Application.Ports;

/// <summary>单任务业务边界 IMediaReferenceBuilder。</summary>
public interface IMediaReferenceBuilder
{
    /// <summary>使用同工具建立字幕 transport 参考容器。</summary>
    /// <exception cref="ToolFailureException">工具退出码异常、输出解析失败或出现 Fatal 级诊断时抛出；Code 来自 ErrorCodes，Detail 保留原始原因。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task<WorkFile> BuildSubtitleReferenceAsync(WorkFile subtitle, int index, ToolCallContext context, CancellationToken cancellationToken = default);

    /// <summary>使用同工具建立选定音频轨参考容器。</summary>
    /// <exception cref="ToolFailureException">工具退出码异常、输出解析失败或出现 Fatal 级诊断时抛出；Code 来自 ErrorCodes，Detail 保留原始原因。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task<WorkFile> BuildAudioReferenceAsync(ExternalTrackSpec audio, int index, ToolCallContext context, CancellationToken cancellationToken = default);
}
