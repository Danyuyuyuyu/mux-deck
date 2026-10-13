using MuxDeck.Contracts;
using MuxDeck.Domain;

namespace MuxDeck.Application.Ports;

public interface IToolCatalog
{
    Task<EquatableArray<ToolDescriptor>> ResolveAsync(EquatableArray<ToolCapability> capabilities,
        ToolConfiguration tools, CancellationToken cancellationToken = default);
}
