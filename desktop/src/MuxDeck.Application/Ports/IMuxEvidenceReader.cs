using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Application.Ports.Failures;

namespace MuxDeck.Application.Ports;

/// <summary>管理本任务的字幕副本与提取证据，读取大小、字幕全文及 SHA-256，不决定 QC 成败。</summary>
public interface IMuxEvidenceReader
{
    /// <summary>复制已验证的 SRT 输入到本任务。</summary>
    /// <exception cref="ToolFailureException">字幕复制、证据目录创建或文件读写失败时抛出，Code 为 ErrorCodes.WorkspaceIoFailed，Detail 保留原始原因。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task<WorkFile> CopySubtitleAsync(ExternalTrackSpec track, int index, ToolCallContext context, CancellationToken cancellationToken = default);

    /// <summary>准备本任务提取证据目录。</summary>
    /// <exception cref="ToolFailureException">字幕复制、证据目录创建或文件读写失败时抛出，Code 为 ErrorCodes.WorkspaceIoFailed，Detail 保留原始原因。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task<string> PrepareExtractionAsync(ToolCallContext context, CancellationToken cancellationToken = default);

    /// <summary>读取字体附件大小及内容摘要。</summary>
    /// <exception cref="ToolFailureException">字幕复制、证据目录创建或文件读写失败时抛出，Code 为 ErrorCodes.WorkspaceIoFailed，Detail 保留原始原因。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task<MuxAttachmentEvidence> ReadAttachmentAsync(WorkFile file, CancellationToken cancellationToken = default);

    /// <summary>读取提取字幕的存在性、大小与全文。</summary>
    /// <exception cref="ToolFailureException">字幕复制、证据目录创建或文件读写失败时抛出，Code 为 ErrorCodes.WorkspaceIoFailed，Detail 保留原始原因。字幕结构检查失败使用 ErrorCodes.ToolOutputInvalid。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task<MuxSubtitleEvidence> ReadSubtitleAsync(WorkFile file, CancellationToken cancellationToken = default);

    /// <summary>读取音频原始负载内容摘要。</summary>
    /// <exception cref="ToolFailureException">字幕复制、证据目录创建或文件读写失败时抛出，Code 为 ErrorCodes.WorkspaceIoFailed，Detail 保留原始原因。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task<string> ReadAudioPayloadAsync(WorkFile file, CancellationToken cancellationToken = default);
}
