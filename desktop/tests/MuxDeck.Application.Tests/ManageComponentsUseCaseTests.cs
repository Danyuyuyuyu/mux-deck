using MuxDeck.Contracts;
using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Application.Tests;

public sealed class ManageComponentsUseCaseTests
{
    private static readonly ComponentConfiguration Configuration = new("root", "tools", "logs", "cache", "fonttools");
    private static readonly ComponentUninstallPlan Plan = new("afs", ComponentRemovalKind.Dependencies, "tools", "tools/fonttools");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mutation_keeps_configuration_progress_result_and_lease_until_completion(bool uninstall)
    {
        var coordinator = new OperationCoordinator();
        var backend = new FakeComponents();
        var useCase = new ManageComponentsUseCase(backend, coordinator);
        var progress = new InlineProgress();
        using var cancellation = new CancellationTokenSource();
        var task = uninstall ? useCase.UninstallAsync(Plan, Configuration, progress, cancellation.Token)
            : useCase.InstallAsync("afs", Configuration, progress, cancellation.Token);
        Assert.Same(Configuration, backend.Configuration);
        Assert.Equal(cancellation.Token, backend.Token);
        Assert.Same(progress, backend.Progress);
        Assert.Equal(uninstall ? OperationKind.ComponentUninstall : OperationKind.ComponentInstall, Assert.Single(coordinator.State.ActiveOperations));
        var shutdown = coordinator.ShutdownAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(shutdown.IsCompleted);
        var result = new ComponentOperationResult(ComponentOperationStatus.Succeeded, "installed", new("log"), null);
        backend.Completion.SetResult(result);
        Assert.Same(result, await task);
        await shutdown;
        Assert.Empty(coordinator.State.ActiveOperations);
        if (uninstall) Assert.Same(Plan, backend.Plan); else Assert.Equal("afs", backend.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Busy_keeps_original_message_and_never_calls_backend(bool uninstall)
    {
        var coordinator = new OperationCoordinator();
        await using var lease = await coordinator.TryAcquireAsync(OperationKind.ComponentInstall, cancellationToken: TestContext.Current.CancellationToken);
        var backend = new FakeComponents();
        var useCase = new ManageComponentsUseCase(backend, coordinator);
        var result = await (uninstall ? useCase.UninstallAsync(Plan, Configuration, cancellationToken: TestContext.Current.CancellationToken) : useCase.InstallAsync("afs", Configuration, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(ComponentOperationStatus.Failed, result.Status);
        Assert.Equal(ErrorCodes.ComponentOperationBusy, result.Failure!.Code);
        Assert.Equal("已有组件安装或卸载正在进行。", result.Failure.Message);
        Assert.Null(backend.Configuration);
        Assert.Single(coordinator.State.ActiveOperations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_result_is_preserved_and_releases_lease(bool uninstall)
    {
        var coordinator = new OperationCoordinator();
        var backend = new FakeComponents();
        var result = new ComponentOperationResult(ComponentOperationStatus.Failed, null, new("failure.log"), new("CODE", "原错误", "原原因"));
        backend.Completion.SetResult(result);
        var useCase = new ManageComponentsUseCase(backend, coordinator);
        Assert.Same(result, await (uninstall ? useCase.UninstallAsync(Plan, Configuration, cancellationToken: TestContext.Current.CancellationToken) : useCase.InstallAsync("afs", Configuration, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Empty(coordinator.State.ActiveOperations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Backend_exception_is_preserved_and_releases_lease(bool uninstall)
    {
        var coordinator = new OperationCoordinator();
        var backend = new FakeComponents();
        var original = new InvalidOperationException("原异常");
        backend.Completion.SetException(original);
        var useCase = new ManageComponentsUseCase(backend, coordinator);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => uninstall ? useCase.UninstallAsync(Plan, Configuration, cancellationToken: TestContext.Current.CancellationToken) : useCase.InstallAsync("afs", Configuration, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Same(original, error);
        Assert.Empty(coordinator.State.ActiveOperations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_returns_cancelled_and_releases_lease(bool uninstall)
    {
        var coordinator = new OperationCoordinator();
        var backend = new FakeComponents();
        var useCase = new ManageComponentsUseCase(backend, coordinator);
        using var cancellation = new CancellationTokenSource();
        var task = uninstall ? useCase.UninstallAsync(Plan, Configuration, cancellationToken: cancellation.Token)
            : useCase.InstallAsync("afs", Configuration, cancellationToken: cancellation.Token);
        cancellation.Cancel();
        Assert.Equal(ComponentOperationStatus.Cancelled, (await task).Status);
        Assert.Empty(coordinator.State.ActiveOperations);
    }

    [Fact]
    public async Task Successful_commit_is_not_changed_by_late_cancellation()
    {
        var backend = new FakeComponents { IgnoreCancellation = true };
        var coordinator = new OperationCoordinator();
        var useCase = new ManageComponentsUseCase(backend, coordinator);
        using var cancellation = new CancellationTokenSource();
        var task = useCase.InstallAsync("afs", Configuration, cancellationToken: cancellation.Token);
        cancellation.Cancel();
        var result = new ComponentOperationResult(ComponentOperationStatus.Succeeded, "installed", new("log"), null);
        backend.Completion.SetResult(result);
        Assert.Same(result, await task);
        Assert.Empty(coordinator.State.ActiveOperations);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("one")]
    [InlineData("plan")]
    public async Task Inspections_and_plan_preparation_are_leased_and_keep_data(string operation)
    {
        var coordinator = new OperationCoordinator();
        var backend = new FakeComponents { Inspect = new("afs", ComponentState.DependencyMissing,
            [new("AFS", "path", ComponentState.Found, null, ComponentPartPurpose.Unknown, ComponentPartSource.MuxDeck)], "2.1.1", "缺后端") };
        backend.OnInspection = () => Assert.Equal(OperationKind.ComponentInspection, coordinator.State.ActiveOperations[^1]);
        var useCase = new ManageComponentsUseCase(backend, coordinator);
        await using var install = await coordinator.TryAcquireAsync(OperationKind.ComponentInstall, cancellationToken: TestContext.Current.CancellationToken);
        if (operation == "all") Assert.Same(backend.Inspect, Assert.Single(await useCase.InspectAsync(Configuration, cancellationToken: TestContext.Current.CancellationToken)));
        else if (operation == "one") Assert.Same(backend.Inspect, await useCase.InspectAsync("afs", Configuration, cancellationToken: TestContext.Current.CancellationToken));
        else Assert.Same(Plan, await useCase.PrepareUninstallAsync("afs", ComponentRemovalKind.Dependencies, Configuration, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Same(Configuration, backend.Configuration);
        Assert.Equal(OperationKind.ComponentInstall, Assert.Single(coordinator.State.ActiveOperations));
    }

    [Theory]
    [InlineData("all")]
    [InlineData("one")]
    [InlineData("plan")]
    public async Task Inspection_exception_releases_lease(string operation)
    {
        var coordinator = new OperationCoordinator();
        var backend = new FakeComponents { OnInspection = () => throw new InvalidOperationException("检测失败") };
        var useCase = new ManageComponentsUseCase(backend, coordinator);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (operation == "all") await useCase.InspectAsync(Configuration, cancellationToken: TestContext.Current.CancellationToken);
            else if (operation == "one") await useCase.InspectAsync("afs", Configuration, cancellationToken: TestContext.Current.CancellationToken);
            else await useCase.PrepareUninstallAsync("afs", ComponentRemovalKind.Dependencies, Configuration, cancellationToken: TestContext.Current.CancellationToken);
        });
        Assert.Empty(coordinator.State.ActiveOperations);
    }

    [Fact]
    public async Task Directory_query_forwards_while_idle_or_inspecting_and_hides_while_mutating_or_closing()
    {
        var coordinator = new OperationCoordinator();
        var backend = new FakeComponents { Query = new("afs", ComponentRemovalKind.Component, "tools", "tools/afs") };
        var useCase = new ManageComponentsUseCase(backend, coordinator);
        var token = TestContext.Current.CancellationToken;

        // 空闲：原样返回与 TryPlan 一致的目录。
        Assert.Same(backend.Query, useCase.QueryUninstallDirectory("afs", ComponentRemovalKind.Component, Configuration, token));

        // 检测中：与 3e 互斥矩阵一致，目录查询不受检测影响。
        await using (await coordinator.TryAcquireAsync(OperationKind.ComponentInspection, token))
            Assert.Same(backend.Query, useCase.QueryUninstallDirectory("afs", ComponentRemovalKind.Component, Configuration, token));

        // 安装中：隐藏候选，且不触碰后端。
        backend.QueryCalled = false;
        await using (await coordinator.TryAcquireAsync(OperationKind.ComponentInstall, token))
        {
            Assert.Null(useCase.QueryUninstallDirectory("afs", ComponentRemovalKind.Component, Configuration, token));
            Assert.False(backend.QueryCalled);
        }

        // 卸载中：隐藏候选。
        await using (await coordinator.TryAcquireAsync(OperationKind.ComponentUninstall, token))
            Assert.Null(useCase.QueryUninstallDirectory("afs", ComponentRemovalKind.Component, Configuration, token));

        // 关闭中：隐藏候选。
        var shutdown = coordinator.ShutdownAsync(token);
        Assert.Null(useCase.QueryUninstallDirectory("afs", ComponentRemovalKind.Component, Configuration, token));
        await shutdown;
        Assert.True(coordinator.State.IsShuttingDown);
    }

    private sealed class InlineProgress : IProgress<ComponentProgress>
    { public void Report(ComponentProgress value) { } }
    private sealed class FakeComponents : IManageComponents
    {
        public readonly TaskCompletionSource<ComponentOperationResult> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ComponentConfiguration? Configuration;
        public string? Id;
        public ComponentUninstallPlan? Plan;
        public CancellationToken Token;
        public IProgress<ComponentProgress>? Progress;
        public bool IgnoreCancellation;
        public Action? OnInspection;
        public ComponentInspection Inspect = new("afs", ComponentState.Missing, []);
        public ComponentDirectoryQuery? Query;
        public bool QueryCalled;
        public ComponentDirectoryQuery? QueryUninstallDirectory(string componentId, ComponentRemovalKind kind, ComponentConfiguration configuration, CancellationToken cancellationToken = default)
        { QueryCalled = true; Configuration = configuration; Id = componentId; return Query; }
        public Task<EquatableArray<ComponentInspection>> InspectAsync(ComponentConfiguration configuration, CancellationToken cancellationToken = default)
        { Configuration = configuration; OnInspection?.Invoke(); return Task.FromResult<EquatableArray<ComponentInspection>>([Inspect]); }
        public Task<ComponentInspection> InspectAsync(string componentId, ComponentConfiguration configuration, CancellationToken cancellationToken = default)
        { Configuration = configuration; Id = componentId; OnInspection?.Invoke(); return Task.FromResult(Inspect); }
        public Task<ComponentUninstallPlan> PrepareUninstallAsync(string componentId, ComponentRemovalKind kind, ComponentConfiguration configuration, CancellationToken cancellationToken = default)
        { Configuration = configuration; OnInspection?.Invoke(); return Task.FromResult(ManageComponentsUseCaseTests.Plan); }
        public Task<ComponentOperationResult> InstallAsync(string componentId, ComponentConfiguration configuration, IProgress<ComponentProgress>? progress = null, CancellationToken cancellationToken = default)
        { Id = componentId; return Run(configuration, progress, cancellationToken); }
        public Task<ComponentOperationResult> UninstallAsync(ComponentUninstallPlan plan, ComponentConfiguration configuration, IProgress<ComponentProgress>? progress = null, CancellationToken cancellationToken = default)
        { Plan = plan; return Run(configuration, progress, cancellationToken); }
        private Task<ComponentOperationResult> Run(ComponentConfiguration configuration, IProgress<ComponentProgress>? progress, CancellationToken token)
        { Configuration = configuration; Progress = progress; Token = token; return IgnoreCancellation ? Completion.Task : Completion.Task.WaitAsync(token); }
    }
}
