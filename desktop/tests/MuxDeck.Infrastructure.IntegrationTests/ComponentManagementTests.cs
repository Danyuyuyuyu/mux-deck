using MuxDeck.Application;
using MuxDeck.Contracts;
using MuxDeck.Infrastructure.Components;
using MuxDeck.Infrastructure;
using Xunit;
using C = MuxDeck.Contracts;

namespace MuxDeck.Infrastructure.IntegrationTests;

public sealed class ComponentManagementTests
{
    private static IManageComponents CreateManager(IOperationCoordinator coordinator, ComponentVerifier? verifier = null)
    {
        verifier ??= new ComponentVerifier();
        return new ManageComponentsUseCase(new InfrastructureComponentManager(verifier, new ComponentInstaller(verifier: verifier), new ComponentUninstaller()), coordinator);
    }

    [Fact]
    public async Task Inspection_contract_preserves_legacy_tool_identity_source_version_and_reason()
    {
        using var fixture = new Fixture();
        var tools = Path.Combine(fixture.Root, "bin");
        Directory.CreateDirectory(Path.Combine(tools, "mkvtoolnix"));
        foreach (var name in new[] { "mkvmerge", "mkvextract", "mkvpropedit" }) File.WriteAllText(Path.Combine(tools, "mkvtoolnix", name + ".exe"), "fixture");
        var verifier = new ComponentVerifier(new VersionRunner());
        var definition = ComponentCatalog.All.Single(value => value.Id == "mkvtoolnix");
        var legacy = await verifier.VerifyAsync(definition, new(fixture.Root, tools, Path.Combine(fixture.Root, "logs"), Path.Combine(fixture.Root, "cache")), TestContext.Current.CancellationToken);
        IManageComponents manager = CreateManager(new OperationCoordinator(), verifier);
        var result = await manager.InspectAsync("mkvtoolnix", new(fixture.Root, tools, "logs", "cache"), TestContext.Current.CancellationToken);
        Assert.Equal(C.ComponentState.Available, result.State);
        Assert.Equal(legacy.Version, result.Version);
        Assert.Equal(legacy.Reason, result.Reason);
        Assert.Equal(legacy.Parts.Select(part => (part.Name, part.Path)), result.Parts.Select(part => (part.Name, part.Path)));
        Assert.All(result.Parts, part => Assert.Equal(C.ComponentState.Found, part.State));
    }

    [Fact]
    public async Task Uninstall_commit_finishes_during_shutdown_and_preserves_progress_and_log()
    {
        using var fixture = new Fixture();
        var coordinator = new OperationCoordinator();
        IManageComponents manager = CreateManager(coordinator);
        var plan = await manager.PrepareUninstallAsync("assfonts", C.ComponentRemovalKind.Component, fixture.Configuration, TestContext.Current.CancellationToken);
        Assert.Equal(fixture.Target, plan.TargetDirectory);
        var messages = new List<ComponentProgress>();
        Task? shutdown = null;
        var result = await manager.UninstallAsync(plan, fixture.Configuration, new ProgressSink(value =>
        {
            messages.Add(value);
            shutdown = coordinator.ShutdownAsync(TestContext.Current.CancellationToken);
            Assert.False(shutdown.IsCompleted);
        }), TestContext.Current.CancellationToken);
        Assert.Equal(ComponentOperationStatus.Succeeded, result.Status);
        Assert.Null(result.Failure);
        Assert.Equal(fixture.Target, result.Directory);
        Assert.False(Directory.Exists(fixture.Target));
        var progress = Assert.Single(messages);
        Assert.Equal(ComponentPhase.Uninstalling, progress.Phase);
        Assert.Equal("正在卸载", progress.Message);
        Assert.NotNull(result.Log);
        var log = await File.ReadAllTextAsync(result.Log.Path, TestContext.Current.CancellationToken);
        Assert.Contains("卸载请求 assfonts Component " + fixture.Target, log);
        Assert.Contains("卸载完成", log);
        Assert.NotNull(shutdown); await shutdown;
        Assert.True(coordinator.State.IsShuttingDown);
        Assert.Empty(coordinator.State.ActiveOperations);
    }

    [Fact]
    public async Task Invalid_uninstall_returns_structured_failure_with_original_message_and_log()
    {
        using var fixture = new Fixture();
        var coordinator = new OperationCoordinator();
        IManageComponents manager = CreateManager(coordinator);
        var forged = new ComponentUninstallPlan("assfonts", C.ComponentRemovalKind.Component, fixture.Tools, fixture.Root);
        var result = await manager.UninstallAsync(forged, fixture.Configuration, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ComponentOperationStatus.Failed, result.Status);
        Assert.Equal(ErrorCodes.ComponentUninstallFailed, result.Failure!.Code);
        Assert.Equal("卸载未完成：组件目录已变化，请重新确认卸载范围。", result.Failure.Message);
        Assert.NotNull(result.Log); Assert.True(File.Exists(result.Log.Path));
        Assert.True(File.Exists(Path.Combine(fixture.Target, "keep.txt")));
        Assert.Empty(coordinator.State.ActiveOperations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shared_coordinator_blocks_both_mutations_without_touching_files(bool uninstall)
    {
        using var fixture = new Fixture();
        var coordinator = new OperationCoordinator();
        IManageComponents first = CreateManager(coordinator);
        IManageComponents second = CreateManager(coordinator);
        await using var lease = await coordinator.TryAcquireAsync(OperationKind.ComponentInstall, TestContext.Current.CancellationToken);
        var result = uninstall
            ? await first.UninstallAsync(new("assfonts", C.ComponentRemovalKind.Component, fixture.Tools, fixture.Target), fixture.Configuration, cancellationToken: TestContext.Current.CancellationToken)
            : await second.InstallAsync("assfonts", fixture.Configuration, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ComponentOperationStatus.Failed, result.Status);
        Assert.Equal(ErrorCodes.ComponentOperationBusy, result.Failure!.Code);
        Assert.Equal("已有组件安装或卸载正在进行。", result.Failure.Message);
        Assert.Null(result.Log);
        Assert.True(File.Exists(Path.Combine(fixture.Target, "keep.txt")));
        Assert.False(Directory.Exists(fixture.Configuration.LogsDirectory));
    }

    private sealed class VersionRunner : IComponentProbeRunner
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessResult(0, Path.GetFileNameWithoutExtension(request.Executable) + " v101.0", ""));
    }
    private sealed class ProgressSink(Action<ComponentProgress> action) : IProgress<ComponentProgress>
    { public void Report(ComponentProgress value) => action(value); }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "MuxDeck-3e-" + Guid.NewGuid().ToString("N"));
        public string Tools => Path.Combine(Root, "tools");
        public string Target => Path.Combine(Tools, "assfonts");
        public ComponentConfiguration Configuration => new(Root, Tools, Path.Combine(Root, "logs"), Path.Combine(Root, "cache"));
        public Fixture() { Directory.CreateDirectory(Target); File.WriteAllText(Path.Combine(Target, "keep.txt"), "保留样本"); }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
