using MuxDeck.Contracts;
using C = MuxDeck.Contracts;
using MuxDeck.Domain;

namespace MuxDeck.Infrastructure.Components;

/// <summary>Infrastructure 组件适配器；只把外部实现转换为 Application 契约，不保留 Core 过渡回绕。</summary>
public sealed class InfrastructureComponentManager(
    IComponentVerifier verifier,
    IComponentInstaller installer,
    IComponentUninstaller uninstaller) : IManageComponents
{
    public Task<EquatableArray<C.ComponentInspection>> InspectAsync(C.ComponentConfiguration configuration,
        CancellationToken cancellationToken = default) =>
        InspectAllAsync(configuration, cancellationToken);

    private async Task<EquatableArray<C.ComponentInspection>> InspectAllAsync(C.ComponentConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var values = await Task.WhenAll(ComponentCatalog.All.Select(component =>
            InspectAsync(component.Id, configuration, cancellationToken))).ConfigureAwait(false);
        return new(values);
    }

    public async Task<C.ComponentInspection> InspectAsync(string componentId, C.ComponentConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var definition = ComponentCatalog.All.Single(value => value.Id == componentId);
        return ToContract(await verifier.VerifyAsync(definition, configuration, cancellationToken).ConfigureAwait(false), componentId);
    }

    public async Task<C.ComponentOperationResult> InstallAsync(string componentId, C.ComponentConfiguration configuration,
        IProgress<C.ComponentProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await installer.InstallAsync(componentId, Storage(configuration),
                new ProgressAdapter<ComponentInstallProgress>(value => progress?.Report(new(ToPhase(value.Phase), value.Message, value.Bytes, value.TotalBytes))), cancellationToken).ConfigureAwait(false);
            return new(C.ComponentOperationStatus.Succeeded, result.Directory, new(result.LogPath), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { return new(C.ComponentOperationStatus.Cancelled, null, null, null); }
        catch (ComponentInstallException ex)
        { return new(C.ComponentOperationStatus.Failed, null, new(ex.LogPath), new(ErrorCodes.ComponentInstallFailed, ex.Message, ex.ToString())); }
    }

    public C.ComponentDirectoryQuery? QueryUninstallDirectory(string componentId, C.ComponentRemovalKind kind,
        C.ComponentConfiguration configuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = ComponentDirectories.TryPlan(Storage(configuration), componentId, (ComponentRemovalKind)kind);
        return plan is null ? null : new(componentId, kind, plan.ToolsDirectory, plan.TargetDirectory);
    }

    public Task<C.ComponentUninstallPlan> PrepareUninstallAsync(string componentId, C.ComponentRemovalKind kind,
        C.ComponentConfiguration configuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = ComponentDirectories.CreatePlan(Storage(configuration), componentId, (ComponentRemovalKind)kind);
        return Task.FromResult(new C.ComponentUninstallPlan(componentId, kind, plan.ToolsDirectory, plan.TargetDirectory));
    }

    public async Task<C.ComponentOperationResult> UninstallAsync(C.ComponentUninstallPlan plan, C.ComponentConfiguration configuration,
        IProgress<C.ComponentProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await uninstaller.UninstallAsync(new(plan.ComponentId, (ComponentRemovalKind)plan.Kind, plan.ToolsDirectory, plan.TargetDirectory), Storage(configuration),
                new ProgressAdapter<ComponentRemovalProgress>(value => progress?.Report(new(ComponentPhase.Uninstalling, value.Message))), cancellationToken).ConfigureAwait(false);
            return new(C.ComponentOperationStatus.Succeeded, result.Directory, new(result.LogPath), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { return new(C.ComponentOperationStatus.Cancelled, null, null, null); }
        catch (ComponentRemovalException ex)
        { return new(C.ComponentOperationStatus.Failed, null, new(ex.LogPath), new(ErrorCodes.ComponentUninstallFailed, ex.Message, ex.ToString())); }
    }

    private static StoragePaths Storage(C.ComponentConfiguration value) =>
        new(value.WorkspaceRoot, value.ToolsDirectory, Path.Combine(value.WorkspaceRoot, "config"), value.LogsDirectory, value.CacheDirectory);

    private static C.ComponentInspection ToContract(ComponentInspection value, string id) =>
        new(id, (C.ComponentState)value.State, new(value.Parts.Select(part => new C.ComponentPart(part.Name, part.Path,
            part.IsAvailable ? C.ComponentState.Found : C.ComponentState.Missing, null, Purpose(part.Purpose), Source(part.Source)))), value.Version, value.Reason);

    private static C.ComponentPartPurpose Purpose(string value) => value switch
    {
        "封装与轨道探测" => C.ComponentPartPurpose.PackagingAndProbe,
        "字幕、字体与章节提取" => C.ComponentPartPurpose.SubtitleFontChapterExtraction,
        "轨道与容器元数据修补" => C.ComponentPartPurpose.TrackMetadataRepair,
        "字幕渲染与预览" => C.ComponentPartPurpose.SubtitleRenderPreview,
        "字体子集化主工具" => C.ComponentPartPurpose.PrimaryFontSubsetting,
        "字体子集化备用工具" => C.ComponentPartPurpose.FallbackFontSubsetting,
        _ => C.ComponentPartPurpose.Unknown
    };

    private static C.ComponentPartSource Source(string value) => value switch
    {
        "Mux Deck" => C.ComponentPartSource.MuxDeck,
        "PATH" => C.ComponentPartSource.Path,
        "系统" => C.ComponentPartSource.System,
        _ => C.ComponentPartSource.Missing
    };

    private static ComponentPhase ToPhase(ComponentInstallPhase value) => Enum.Parse<ComponentPhase>(value.ToString());
    private sealed class ProgressAdapter<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
}
