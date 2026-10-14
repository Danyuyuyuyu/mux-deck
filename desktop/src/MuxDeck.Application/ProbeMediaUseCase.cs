using MuxDeck.Application.Ports;
using MuxDeck.Contracts;

namespace MuxDeck.Application;

// 只读探测消费调用方配置快照，与单任务流程复用同一个探测端口。
public sealed class ProbeMediaUseCase(IMediaProber prober) : IProbeMedia
{
    public Task<MediaProbeResult> ProbeAsync(string path, ToolConfiguration tools,
        CancellationToken cancellationToken = default) => prober.ProbeReadOnlyAsync(path, tools, cancellationToken);
}
