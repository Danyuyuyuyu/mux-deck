using MuxDeck.Contracts;

namespace MuxDeck.Application.Ports;

public interface IMediaProber
{
    Task<MediaProbeResult> ProbeAsync(string path, ToolCallContext context,
        CancellationToken cancellationToken = default);
}
