using MuxDeck.Application.Ports;
using MuxDeck.Application.Ports.Failures;
using MuxDeck.Contracts;

namespace MuxDeck.Infrastructure.Muxing;

public sealed class ToolReadiness(IMuxToolRunner? runner = null, IToolCatalog? catalog = null) : IToolReadiness
{
    public async Task EnsureReadyAsync(bool needsFonts, ToolCallContext context, CancellationToken cancellationToken = default)
    {
        var tools = catalog ?? new ToolCatalog();
        await tools.ResolveAsync([ToolCapability.ProbeMedia, ToolCapability.ExtractMedia], context.Tools, cancellationToken).ConfigureAwait(false);
        if (!needsFonts) return;
        await tools.ResolveAsync([ToolCapability.SubsetFonts], context.Tools, cancellationToken).ConfigureAwait(false);
        var descriptors = await tools.ResolveAsync([ToolCapability.FontSubsetBackend, ToolCapability.InspectFonts], context.Tools, cancellationToken).ConfigureAwait(false);
        await new ToolExecutor(runner).RunAsync(descriptors[0].ExecutablePath, ["--help"], context, cancellationToken, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
        await new ToolExecutor(runner).RunAsync(descriptors[1].ExecutablePath, ["--version"], context, cancellationToken, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
    }
}
