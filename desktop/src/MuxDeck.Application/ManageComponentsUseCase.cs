using MuxDeck.Contracts;
using MuxDeck.Domain;

namespace MuxDeck.Application;

/// <summary>组件检测、安装和卸载的租约生命周期；配置只来自每次调用的快照。</summary>
public sealed class ManageComponentsUseCase(IManageComponents components, IOperationCoordinator coordinator) : IManageComponents
{
    public async Task<EquatableArray<ComponentInspection>> InspectAsync(ComponentConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await coordinator.TryAcquireAsync(OperationKind.ComponentInspection, cancellationToken).ConfigureAwait(false);
        if (lease is null) throw new InvalidOperationException("操作协调器正在关闭。");
        return await components.InspectAsync(configuration, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ComponentInspection> InspectAsync(string componentId, ComponentConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await coordinator.TryAcquireAsync(OperationKind.ComponentInspection, cancellationToken).ConfigureAwait(false);
        if (lease is null) throw new InvalidOperationException("操作协调器正在关闭。");
        return await components.InspectAsync(componentId, configuration, cancellationToken).ConfigureAwait(false);
    }

    public Task<ComponentOperationResult> InstallAsync(string componentId, ComponentConfiguration configuration,
        IProgress<ComponentProgress>? progress = null, CancellationToken cancellationToken = default) =>
        MutateAsync(OperationKind.ComponentInstall, () => components.InstallAsync(componentId, configuration, progress, cancellationToken), cancellationToken);

    public async Task<ComponentUninstallPlan> PrepareUninstallAsync(string componentId, ComponentRemovalKind kind,
        ComponentConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await using var lease = await coordinator.TryAcquireAsync(OperationKind.ComponentInspection, cancellationToken).ConfigureAwait(false);
        if (lease is null) throw new InvalidOperationException("操作协调器正在关闭。");
        return await components.PrepareUninstallAsync(componentId, kind, configuration, cancellationToken).ConfigureAwait(false);
    }

    public Task<ComponentOperationResult> UninstallAsync(ComponentUninstallPlan plan, ComponentConfiguration configuration,
        IProgress<ComponentProgress>? progress = null, CancellationToken cancellationToken = default) =>
        MutateAsync(OperationKind.ComponentUninstall, () => components.UninstallAsync(plan, configuration, progress, cancellationToken), cancellationToken);

    private async Task<ComponentOperationResult> MutateAsync(OperationKind kind, Func<Task<ComponentOperationResult>> action, CancellationToken token)
    {
        try
        {
            await using var lease = await coordinator.TryAcquireAsync(kind, token).ConfigureAwait(false);
            if (lease is null) return new(ComponentOperationStatus.Failed, null, null,
                new(ErrorCodes.ComponentOperationBusy, "已有组件安装或卸载正在进行。"));
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { return new(ComponentOperationStatus.Cancelled, null, null, null); }
    }
}
