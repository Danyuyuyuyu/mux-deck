using MuxDeck.Contracts;

namespace MuxDeck.Application.Ports;

public interface IMuxer
{
    Task<MediaMuxResult> MuxAsync(MediaMuxRequest request, ToolCallContext context,
        IProgress<JobProgress>? progress = null, CancellationToken cancellationToken = default);
}
