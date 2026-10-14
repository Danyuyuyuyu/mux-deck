using MuxDeck.Application;
using MuxDeck.Contracts;
using MuxDeck.Domain;
using C = MuxDeck.Contracts;

namespace MuxDeck.Core.Components;

/// <summary>3e 过渡装配与旧模型转换；组件工具/文件实现按批准计划留到 4a 搬迁。</summary>
public sealed class ComponentManagement : IManageComponents
{
    // 仅供过渡期集中装配接入旧组件入口；4a-2 随旧入口迁移删除。
    public static void ConfigureLegacyCoordinator(IOperationCoordinator coordinator) => ComponentOperations.Configure(coordinator);
    private readonly IManageComponents useCase;

    public ComponentManagement(ComponentVerifier? verifier = null, ComponentInstaller? installer = null,
        ComponentUninstaller? uninstaller = null, IOperationCoordinator? coordinator = null)
        : this(verifier, installer, uninstaller, coordinator, false, null) { }

    internal ComponentManagement(ComponentVerifier? verifier, ComponentInstaller? installer,
        ComponentUninstaller? uninstaller, IOperationCoordinator? coordinator, bool legacyExceptions,
        ComponentDefinition? definition)
    {
        useCase = new ManageComponentsUseCase(new Backend(verifier ?? new(), installer ?? new(), uninstaller ?? new(),
            legacyExceptions, definition), coordinator ?? ComponentOperations.Coordinator);
    }

    public Task<EquatableArray<C.ComponentInspection>> InspectAsync(ComponentConfiguration configuration, CancellationToken cancellationToken = default) =>
        useCase.InspectAsync(configuration, cancellationToken);
    public Task<C.ComponentInspection> InspectAsync(string componentId, ComponentConfiguration configuration, CancellationToken cancellationToken = default) =>
        useCase.InspectAsync(componentId, configuration, cancellationToken);
    public Task<ComponentOperationResult> InstallAsync(string componentId, ComponentConfiguration configuration,
        IProgress<ComponentProgress>? progress = null, CancellationToken cancellationToken = default) =>
        useCase.InstallAsync(componentId, configuration, progress, cancellationToken);
    public Task<ComponentUninstallPlan> PrepareUninstallAsync(string componentId, C.ComponentRemovalKind kind,
        ComponentConfiguration configuration, CancellationToken cancellationToken = default) =>
        useCase.PrepareUninstallAsync(componentId, kind, configuration, cancellationToken);
    public Task<ComponentOperationResult> UninstallAsync(ComponentUninstallPlan plan, ComponentConfiguration configuration,
        IProgress<ComponentProgress>? progress = null, CancellationToken cancellationToken = default) =>
        useCase.UninstallAsync(plan, configuration, progress, cancellationToken);

    internal static ComponentConfiguration Configuration(Workspace workspace) => new(workspace.Root, workspace.ToolsDirectory,
        workspace.TaskLogsDirectory, workspace.TaskCacheDirectory, workspace.FontToolsDirectoryOverride);
    internal static ComponentConfiguration Configuration(StoragePaths storage) => new(storage.AppDataRoot, storage.ToolsDirectory,
        storage.LogsDirectory, storage.CacheDirectory);
    internal static ComponentInspection Legacy(C.ComponentInspection value) => new((ComponentState)value.State,
        value.Parts.Select(part => new ToolStatus(part.Name, part.Purpose, part.Path) { Source = part.Source }).ToArray(), value.Version, value.Reason);
    internal static ComponentRemovalPlan Legacy(ComponentUninstallPlan value) => new(value.ComponentId,
        (ComponentRemovalKind)value.Kind, value.ToolsDirectory, value.TargetDirectory);
    internal static ComponentUninstallPlan Contract(ComponentRemovalPlan value) => new(value.ComponentId,
        (C.ComponentRemovalKind)value.Kind, value.ToolsDirectory, value.TargetDirectory);
    internal static void RequireSuccess(ComponentOperationResult result, CancellationToken token)
    {
        if (result.Status == ComponentOperationStatus.Cancelled) throw new OperationCanceledException(token);
        if (result.Failure is { } failure) throw new IOException(failure.Message);
    }

    private sealed class ForwardProgress<T>(Action<T> report) : IProgress<T>
    { public void Report(T value) => report(value); }

    private sealed class Backend(ComponentVerifier verifier, ComponentInstaller installer, ComponentUninstaller uninstaller,
        bool legacyExceptions, ComponentDefinition? definition) : IManageComponents
    {
        private static Workspace Workspace(ComponentConfiguration value) => new(value.WorkspaceRoot)
        { ToolsDirectoryOverride = value.ToolsDirectory, FontToolsDirectoryOverride = value.FontToolsDirectoryOverride };
        private static StoragePaths Storage(ComponentConfiguration value) => new(value.WorkspaceRoot, value.ToolsDirectory,
            Path.Combine(value.WorkspaceRoot, "config"), value.LogsDirectory, value.CacheDirectory);
        public async Task<EquatableArray<C.ComponentInspection>> InspectAsync(ComponentConfiguration configuration, CancellationToken cancellationToken = default) =>
            new(await Task.WhenAll(ComponentCatalog.All.Select(component => InspectAsync(component.Id, configuration, cancellationToken))).ConfigureAwait(false));
        public async Task<C.ComponentInspection> InspectAsync(string componentId, ComponentConfiguration configuration, CancellationToken cancellationToken = default)
        {
            var component = definition is not null && definition.Id == componentId ? definition : ComponentCatalog.All.Single(value => value.Id == componentId);
            var result = await verifier.VerifyCoreAsync(component, Workspace(configuration), cancellationToken).ConfigureAwait(false);
            return new(componentId, (C.ComponentState)result.State,
                new(result.Parts.Select(part => new ComponentPart(part.Name, part.Path,
                    part.IsAvailable ? C.ComponentState.Found : C.ComponentState.Missing, null, part.Purpose, part.Source))), result.Version, result.Reason);
        }
        public async Task<ComponentOperationResult> InstallAsync(string componentId, ComponentConfiguration configuration,
            IProgress<ComponentProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await installer.InstallCoreAsync(componentId, Storage(configuration),
                    new ForwardProgress<ComponentInstallProgress>(value => progress?.Report(new(Enum.Parse<ComponentPhase>(value.Phase.ToString()), value.Message, value.Bytes, value.TotalBytes))), cancellationToken).ConfigureAwait(false);
                return new(ComponentOperationStatus.Succeeded, result.Directory, new(result.LogPath), null);
            }
            catch (ComponentInstallException ex) when (!legacyExceptions)
            { return new(ComponentOperationStatus.Failed, null, new(ex.LogPath), new(ErrorCodes.ComponentInstallFailed, ex.Message, ex.ToString())); }
        }
        public Task<ComponentUninstallPlan> PrepareUninstallAsync(string componentId, C.ComponentRemovalKind kind,
            ComponentConfiguration configuration, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Contract(ComponentDirectories.CreatePlanCore(Storage(configuration), componentId, (ComponentRemovalKind)kind)));
        }
        public async Task<ComponentOperationResult> UninstallAsync(ComponentUninstallPlan plan, ComponentConfiguration configuration,
            IProgress<ComponentProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await uninstaller.UninstallCoreAsync(Legacy(plan), Storage(configuration),
                    new ForwardProgress<ComponentRemovalProgress>(value => progress?.Report(new(ComponentPhase.Uninstalling, value.Message))), cancellationToken).ConfigureAwait(false);
                return new(ComponentOperationStatus.Succeeded, result.Directory, new(result.LogPath), null);
            }
            catch (ComponentRemovalException ex) when (!legacyExceptions)
            { return new(ComponentOperationStatus.Failed, null, new(ex.LogPath), new(ErrorCodes.ComponentUninstallFailed, ex.Message, ex.ToString())); }
        }
    }
}
