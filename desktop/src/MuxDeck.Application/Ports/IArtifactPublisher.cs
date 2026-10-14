using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Application.Ports.Failures;

namespace MuxDeck.Application.Ports;

/// <summary>不覆盖发布，只有最终成品返回 OutputArtifact。</summary>
public interface IArtifactPublisher
{
    /// <summary>不覆盖发布，只有最终成品返回 OutputArtifact。</summary>
    /// <exception cref="ToolFailureException">目标已存在时使用 ErrorCodes.TargetAlreadyExists；准备、复制或提交成品的文件读写失败时使用 ErrorCodes.WorkspaceIoFailed，Detail 保留原始原因。</exception>
    /// <exception cref="OperationCanceledException">取消时抛出，不转换成 ToolFailureException。</exception>
    Task<OutputArtifact> PublishNoOverwriteAsync(WorkFile prepared, string destinationPath,
        CancellationToken cancellationToken = default);
}
