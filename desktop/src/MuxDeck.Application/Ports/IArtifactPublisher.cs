using MuxDeck.Contracts;

namespace MuxDeck.Application.Ports;

public interface IArtifactPublisher
{
    // 同目录暂存与不覆盖提交由适配器负责；竞态冲突失败，不把半成品作为成功产物。
    Task<OutputArtifact> PublishNoOverwriteAsync(OutputArtifact prepared, string destinationPath,
        CancellationToken cancellationToken = default);
}
