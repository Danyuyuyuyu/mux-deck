using MuxDeck.Domain;

namespace MuxDeck.Contracts;

public interface IRunSingleMux
{
    Task<MuxJobResult> RunAsync(MuxJobSpec spec, IProgress<JobProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IProbeMedia
{
    Task<MediaProbeResult> ProbeAsync(string path, ToolConfiguration tools,
        CancellationToken cancellationToken = default);
}

public interface IPrepareImport
{
    Task<ImportResult> PrepareAsync(ImportRequest request, ToolConfiguration tools,
        CancellationToken cancellationToken = default);
}

public interface IManageComponents
{
    Task<EquatableArray<ComponentInspection>> InspectAsync(ComponentConfiguration configuration,
        CancellationToken cancellationToken = default);
    Task<ComponentOperationResult> InstallAsync(string componentId, ComponentConfiguration configuration,
        IProgress<ComponentProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<ComponentUninstallPlan> PrepareUninstallAsync(string componentId, ComponentRemovalKind kind,
        ComponentConfiguration configuration, CancellationToken cancellationToken = default);
    Task<ComponentOperationResult> UninstallAsync(ComponentUninstallPlan plan,
        ComponentConfiguration configuration, IProgress<ComponentProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
