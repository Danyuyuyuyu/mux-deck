using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Application.Ports.Failures;

namespace MuxDeck.Application.Ports;

/// <summary>单任务业务边界 IMuxEvidenceReader。</summary>
public interface IMuxEvidenceReader
{
    /// <summary>复制已验证的 SRT 输入到本任务。</summary>
    /// <exception cref="ToolFailureException">工具退出码异常、输出解析失败或出现 Fatal 级诊断时抛出；Code 来自 ErrorCodes，Detail 保留原始原因。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task<WorkFile> CopySubtitleAsync(ExternalTrackSpec track, int index, ToolCallContext context, CancellationToken cancellationToken = default);

    /// <summary>准备本任务提取证据目录。</summary>
    /// <exception cref="ToolFailureException">工具退出码异常、输出解析失败或出现 Fatal 级诊断时抛出；Code 来自 ErrorCodes，Detail 保留原始原因。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task<string> PrepareExtractionAsync(ToolCallContext context, CancellationToken cancellationToken = default);

    /// <summary>读取字体附件大小及内容摘要。</summary>
    /// <exception cref="ToolFailureException">工具退出码异常、输出解析失败或出现 Fatal 级诊断时抛出；Code 来自 ErrorCodes，Detail 保留原始原因。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task<MuxAttachmentEvidence> ReadAttachmentAsync(WorkFile file, CancellationToken cancellationToken = default);

    /// <summary>读取提取字幕的存在性、大小与全文。</summary>
    /// <exception cref="ToolFailureException">工具退出码异常、输出解析失败或出现 Fatal 级诊断时抛出；Code 来自 ErrorCodes，Detail 保留原始原因。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task<MuxSubtitleEvidence> ReadSubtitleAsync(WorkFile file, CancellationToken cancellationToken = default);

    /// <summary>读取音频原始负载内容摘要。</summary>
    /// <exception cref="ToolFailureException">工具退出码异常、输出解析失败或出现 Fatal 级诊断时抛出；Code 来自 ErrorCodes，Detail 保留原始原因。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task<string> ReadAudioPayloadAsync(WorkFile file, CancellationToken cancellationToken = default);
}
