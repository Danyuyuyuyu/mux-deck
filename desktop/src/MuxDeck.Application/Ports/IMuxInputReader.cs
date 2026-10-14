using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Application.Ports.Failures;

namespace MuxDeck.Application.Ports;

/// <summary>收集单任务输入、输出目标的文件事实与字幕原文，供 Domain 校验，不调用外部工具。</summary>
public interface IMuxInputReader
{
    /// <summary>读取源视频存在性与规范路径。</summary>
    /// <exception cref="ToolFailureException">路径过长或输入字幕读写失败时抛出，Code 为 ErrorCodes.WorkspaceIoFailed，Detail 保留原始原因；文件不存在作为事实返回。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task<MuxVideoFacts> ReadVideoAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>读取字体目录存在性与规范路径。</summary>
    /// <exception cref="ToolFailureException">路径过长或输入字幕读写失败时抛出，Code 为 ErrorCodes.WorkspaceIoFailed，Detail 保留原始原因；文件不存在作为事实返回。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task<MuxFontDirectoryFacts> ReadFontDirectoryAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>读取输出路径与平台文件名事实。</summary>
    /// <exception cref="ToolFailureException">路径过长或输入字幕读写失败时抛出，Code 为 ErrorCodes.WorkspaceIoFailed，Detail 保留原始原因；文件不存在作为事实返回。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task<MuxOutputFacts> ReadOutputAsync(OutputSpec output, string videoPath, CancellationToken cancellationToken = default);

    /// <summary>读取外加轨道路径与字幕原文。</summary>
    /// <exception cref="ToolFailureException">路径过长或输入字幕读写失败时抛出，Code 为 ErrorCodes.WorkspaceIoFailed，Detail 保留原始原因；文件不存在作为事实返回。字幕结构检查失败使用 ErrorCodes.ToolOutputInvalid。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task<MuxTrackFileFacts> ReadTrackAsync(ExternalTrackSpec track, CancellationToken cancellationToken = default);

    /// <summary>检查不覆盖发布目标是否已存在。</summary>
    /// <exception cref="ToolFailureException">路径过长或输入字幕读写失败时抛出，Code 为 ErrorCodes.WorkspaceIoFailed，Detail 保留原始原因；文件不存在作为事实返回。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task<bool> DestinationExistsAsync(string path, CancellationToken cancellationToken = default);
}
