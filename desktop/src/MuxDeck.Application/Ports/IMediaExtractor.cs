using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Application.Ports.Failures;

namespace MuxDeck.Application.Ports;

/// <summary>按明确轨号或附件号提取工作文件。</summary>
/// <exception cref="ToolFailureException">工具退出码异常、输出解析失败或出现 Fatal 级诊断时抛出；Code 来自 ErrorCodes，Detail 保留原始原因。</exception>
/// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
public interface IMediaExtractor
{
    /// <summary>按明确轨号或附件号提取工作文件。</summary>
    /// <exception cref="ToolFailureException">工具退出码异常、输出解析失败或出现 Fatal 级诊断时抛出；Code 来自 ErrorCodes，Detail 保留原始原因。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task ExtractTracksAsync(string sourcePath, EquatableArray<TrackExtraction> tracks,
        ToolCallContext context, CancellationToken cancellationToken = default);

    /// <summary>按附件 ID 提取到指定工作文件。</summary>
    /// <exception cref="ToolFailureException">工具退出码异常、输出解析失败或出现 Fatal 级诊断时抛出；Code 来自 ErrorCodes，Detail 保留原始原因。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task ExtractAttachmentsAsync(string sourcePath, EquatableArray<AttachmentExtraction> attachments,
        ToolCallContext context, CancellationToken cancellationToken = default);
}
