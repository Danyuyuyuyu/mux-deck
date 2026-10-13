using MuxDeck.Contracts;

namespace MuxDeck.Application.Ports;

public interface IFontSubsetter
{
    Task<FontSubsetResult> SubsetAsync(FontSubsetRequest request, ToolCallContext context,
        IProgress<JobProgress>? progress = null, CancellationToken cancellationToken = default);
}
