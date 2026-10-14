using MuxDeck.Application.Ports;
using MuxDeck.Application.Ports.Failures;
using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Infrastructure.Components;

namespace MuxDeck.Infrastructure;

public sealed class ToolCatalog : IToolCatalog
{
    public Task<EquatableArray<ToolDescriptor>> ResolveAsync(EquatableArray<ToolCapability> capabilities,
        ToolConfiguration tools, CancellationToken cancellationToken = default) => PortBoundary.Guard(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(capabilities.Select(capability =>
        {
            var (relative, executable) = capability switch
            {
                ToolCapability.ProbeMedia or ToolCapability.MuxMedia => ("mkvtoolnix/mkvmerge.exe", "mkvmerge"),
                ToolCapability.ExtractMedia => ("mkvtoolnix/mkvextract.exe", "mkvextract"),
                ToolCapability.SubsetFonts => ("assfontsubset/AssFontSubset.Console.exe", "AssFontSubset.Console"),
                ToolCapability.FontSubsetBackend => ("", "pyftsubset"),
                ToolCapability.InspectFonts => ("", "ttx"),
                _ => throw new ArgumentOutOfRangeException(nameof(capability))
            };
            var path = relative.Length > 0 ? ToolPathLookup.Find(tools.ToolsDirectory, relative, executable) :
                FontToolsLocator.Find(tools) is { } directory ? Path.Combine(directory, ToolPathLookup.Executable(executable)) : null;
            if (path is null) throw new ToolFailureException(ErrorCodes.ToolNotFound, "找不到工具：" + executable);
            return new ToolDescriptor(capability, path);
        }).ToEquatableArray());
    });

    internal static async Task<string> Require(IToolCatalog catalog, ToolCapability capability,
        ToolConfiguration tools, CancellationToken token) =>
        (await catalog.ResolveAsync([capability], tools, token).ConfigureAwait(false)).Single().ExecutablePath;
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
