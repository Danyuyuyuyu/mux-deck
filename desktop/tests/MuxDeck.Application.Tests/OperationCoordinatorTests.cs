using MuxDeck.Contracts;
using Xunit;

namespace MuxDeck.Application.Tests;

public sealed class OperationCoordinatorTests
{
    // 数据逐格来自任务文档的旧 Core 矩阵，不调用协调器的判定逻辑。
    public static TheoryData<OperationKind, OperationKind, bool> Matrix => new()
    {
        { OperationKind.SingleMux, OperationKind.SingleMux, true },
        { OperationKind.SingleMux, OperationKind.MediaImport, true },
        { OperationKind.SingleMux, OperationKind.ComponentInspection, true },
        { OperationKind.SingleMux, OperationKind.ComponentInstall, true },
        { OperationKind.SingleMux, OperationKind.ComponentUninstall, true },
        { OperationKind.MediaImport, OperationKind.SingleMux, true },
        { OperationKind.MediaImport, OperationKind.MediaImport, true },
        { OperationKind.MediaImport, OperationKind.ComponentInspection, true },
        { OperationKind.MediaImport, OperationKind.ComponentInstall, true },
        { OperationKind.MediaImport, OperationKind.ComponentUninstall, true },
        { OperationKind.ComponentInspection, OperationKind.SingleMux, true },
        { OperationKind.ComponentInspection, OperationKind.MediaImport, true },
        { OperationKind.ComponentInspection, OperationKind.ComponentInspection, true },
        { OperationKind.ComponentInspection, OperationKind.ComponentInstall, true },
        { OperationKind.ComponentInspection, OperationKind.ComponentUninstall, true },
        { OperationKind.ComponentInstall, OperationKind.SingleMux, true },
        { OperationKind.ComponentInstall, OperationKind.MediaImport, true },
        { OperationKind.ComponentInstall, OperationKind.ComponentInspection, true },
        { OperationKind.ComponentInstall, OperationKind.ComponentInstall, false },
        { OperationKind.ComponentInstall, OperationKind.ComponentUninstall, false },
        { OperationKind.ComponentUninstall, OperationKind.SingleMux, true },
        { OperationKind.ComponentUninstall, OperationKind.MediaImport, true },
        { OperationKind.ComponentUninstall, OperationKind.ComponentInspection, true },
        { OperationKind.ComponentUninstall, OperationKind.ComponentInstall, false },
        { OperationKind.ComponentUninstall, OperationKind.ComponentUninstall, false },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Admission_matches_all_twenty_five_existing_core_pairs(OperationKind active, OperationKind next, bool allowed)
    {
        var coordinator = new OperationCoordinator();
        await using var first = await coordinator.TryAcquireAsync(active, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(first);
        await using var second = await coordinator.TryAcquireAsync(next, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(allowed, second is not null);
        Assert.Equal(allowed ? 2 : 1, coordinator.State.ActiveOperations.Count);
    }

    [Fact]
    public async Task Exceptions_release_the_lease_and_allow_the_next_mutation()
    {
        var coordinator = new OperationCoordinator();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var lease = await coordinator.TryAcquireAsync(OperationKind.ComponentInstall, cancellationToken: TestContext.Current.CancellationToken);
            throw new InvalidOperationException("模拟失败");
        });
        Assert.Empty(coordinator.State.ActiveOperations);
        await using var next = await coordinator.TryAcquireAsync(OperationKind.ComponentUninstall, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(next);
    }

    [Fact]
    public async Task Cancellation_releases_lease_but_does_not_dispose_live_work_early()
    {
        var coordinator = new OperationCoordinator();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await using var lease = await coordinator.TryAcquireAsync(OperationKind.ComponentInstall, cancellation.Token);
            cancellation.Cancel();
            Assert.Single(coordinator.State.ActiveOperations);
            cancellation.Token.ThrowIfCancellationRequested();
        });
        Assert.Empty(coordinator.State.ActiveOperations);
        await using var next = await coordinator.TryAcquireAsync(OperationKind.ComponentInstall, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(next);
    }

    [Fact]
    public async Task Cancelled_acquisition_never_changes_state_or_notifies()
    {
        var coordinator = new OperationCoordinator();
        var notifications = 0;
        coordinator.StateChanged += (_, _) => notifications++;
        using var token = new CancellationTokenSource(); token.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await coordinator.TryAcquireAsync(OperationKind.SingleMux, token.Token));
        Assert.Empty(coordinator.State.ActiveOperations);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task Shutdown_waits_every_lease_and_refuses_every_new_kind()
    {
        var coordinator = new OperationCoordinator();
        var first = await coordinator.TryAcquireAsync(OperationKind.SingleMux, cancellationToken: TestContext.Current.CancellationToken);
        var second = await coordinator.TryAcquireAsync(OperationKind.MediaImport, cancellationToken: TestContext.Current.CancellationToken);
        var third = await coordinator.TryAcquireAsync(OperationKind.ComponentInspection, cancellationToken: TestContext.Current.CancellationToken);
        var shutdown = coordinator.ShutdownAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(coordinator.State.IsShuttingDown);
        Assert.False(shutdown.IsCompleted);
        foreach (var kind in Enum.GetValues<OperationKind>()) Assert.Null(await coordinator.TryAcquireAsync(kind, cancellationToken: TestContext.Current.CancellationToken));
        first!.Dispose(); Assert.False(shutdown.IsCompleted);
        await second!.DisposeAsync(); Assert.False(shutdown.IsCompleted);
        third!.Dispose(); await shutdown;
        Assert.Empty(coordinator.State.ActiveOperations);
        await coordinator.ShutdownAsync(cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Cancelling_shutdown_wait_does_not_reopen_coordinator_or_drop_leases()
    {
        var coordinator = new OperationCoordinator();
        var lease = await coordinator.TryAcquireAsync(OperationKind.ComponentInstall, cancellationToken: TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        var shutdown = coordinator.ShutdownAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => shutdown);
        Assert.True(coordinator.State.IsShuttingDown);
        Assert.Single(coordinator.State.ActiveOperations);
        Assert.Null(await coordinator.TryAcquireAsync(OperationKind.ComponentInspection, cancellationToken: TestContext.Current.CancellationToken));
        lease!.Dispose(); await coordinator.ShutdownAsync(cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task State_notifications_keep_order_snapshot_and_duplicate_lease_identity()
    {
        var coordinator = new OperationCoordinator();
        var states = new List<OperationState>();
        coordinator.StateChanged += (_, state) => states.Add(state);
        var a = await coordinator.TryAcquireAsync(OperationKind.ComponentInspection, cancellationToken: TestContext.Current.CancellationToken);
        var b = await coordinator.TryAcquireAsync(OperationKind.ComponentInspection, cancellationToken: TestContext.Current.CancellationToken);
        a!.Dispose(); await a.DisposeAsync();
        var shutdown = coordinator.ShutdownAsync(cancellationToken: TestContext.Current.CancellationToken);
        b!.Dispose(); await shutdown;
        Assert.Equal(5, states.Count);
        Assert.Equal(new OperationState(false, [OperationKind.ComponentInspection]), states[0]);
        Assert.Equal(new OperationState(false, [OperationKind.ComponentInspection, OperationKind.ComponentInspection]), states[1]);
        Assert.Equal(new OperationState(false, [OperationKind.ComponentInspection]), states[2]);
        Assert.Equal(new OperationState(true, [OperationKind.ComponentInspection]), states[3]);
        Assert.Equal(new OperationState(true, []), states[4]);
    }

    [Fact]
    public async Task Denial_and_repeated_shutdown_do_not_publish_spurious_changes()
    {
        var coordinator = new OperationCoordinator();
        var states = new List<OperationState>();
        coordinator.StateChanged += (_, state) => states.Add(state);
        var lease = await coordinator.TryAcquireAsync(OperationKind.ComponentInstall, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(await coordinator.TryAcquireAsync(OperationKind.ComponentUninstall, cancellationToken: TestContext.Current.CancellationToken));
        var shutdown = coordinator.ShutdownAsync(cancellationToken: TestContext.Current.CancellationToken);
        var repeated = coordinator.ShutdownAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, states.Count);
        lease!.Dispose(); await Task.WhenAll(shutdown, repeated);
        Assert.Equal(3, states.Count);
    }

    [Fact]
    public async Task Throwing_or_reentrant_subscribers_cannot_leak_or_reorder_leases()
    {
        var coordinator = new OperationCoordinator();
        var states = new List<OperationState>();
        coordinator.StateChanged += (_, _) => throw new InvalidOperationException("模拟订阅者失败");
        coordinator.StateChanged += (_, state) => { if (!state.IsShuttingDown) _ = coordinator.ShutdownAsync(cancellationToken: TestContext.Current.CancellationToken); };
        coordinator.StateChanged += (_, state) => states.Add(state);
        var lease = await coordinator.TryAcquireAsync(OperationKind.SingleMux, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(lease);
        Assert.Equal(new[] { false, true }, states.Select(state => state.IsShuttingDown));
        lease.Dispose(); await coordinator.ShutdownAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(coordinator.State.ActiveOperations);
    }

    [Fact]
    public async Task Concurrent_mutations_have_exactly_one_owner()
    {
        var coordinator = new OperationCoordinator();
        var leases = await Task.WhenAll(Enumerable.Range(0, 64).Select(index => Task.Run(async () =>
            await coordinator.TryAcquireAsync(index % 2 == 0 ? OperationKind.ComponentInstall : OperationKind.ComponentUninstall, cancellationToken: TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)));
        var winner = Assert.Single(leases, lease => lease is not null);
        Assert.Single(coordinator.State.ActiveOperations);
        winner!.Dispose(); Assert.Empty(coordinator.State.ActiveOperations);
    }

    [Fact]
    public async Task Unknown_operation_kind_is_rejected_without_state_change()
    {
        var coordinator = new OperationCoordinator();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await coordinator.TryAcquireAsync((OperationKind)999, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(coordinator.State.ActiveOperations);
    }
}
