using MuxDeck.Application;
using MuxDeck.Application.Ports;
using MuxDeck.Application.Ports.Failures;
using MuxDeck.Contracts;
using Xunit;

namespace MuxDeck.Application.Tests;

public sealed class ProbeMediaUseCaseTests
{
    [Fact]
    public async Task Concurrent_probes_keep_their_own_configuration_and_return_the_complete_port_result()
    {
        var port = new ProbePort();
        IProbeMedia useCase = new ProbeMediaUseCase(port);
        var firstTools = new ToolConfiguration("first-tools", "first-logs", "first-cache", "first-fonttools");
        var first = useCase.ProbeAsync("first.mkv", firstTools, TestContext.Current.CancellationToken);
        var secondTools = firstTools with { ToolsDirectory = "second-tools" };
        var second = useCase.ProbeAsync("second.mkv", secondTools, TestContext.Current.CancellationToken);
        port.Complete("second.mkv");
        port.Complete("first.mkv");
        Assert.Same(port.Results["first.mkv"], await first);
        Assert.Same(port.Results["second.mkv"], await second);
        Assert.Same(firstTools, port.Tools["first.mkv"]);
        Assert.Same(secondTools, port.Tools["second.mkv"]);
    }

    [Fact]
    public async Task Port_failure_keeps_its_code_detail_and_identity()
    {
        var failure = new ToolFailureException(ErrorCodes.ToolOutputInvalid, "原始消息", "原始 JSON");
        var port = new ProbePort { Failure = failure };
        var actual = await Assert.ThrowsAsync<ToolFailureException>(() => new ProbeMediaUseCase(port)
            .ProbeAsync("bad.mkv", new("tools", "logs", "cache"), TestContext.Current.CancellationToken));
        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task Cancellation_reaches_the_readonly_port_without_a_task_workspace()
    {
        using var lifetime = new CancellationTokenSource();
        var port = new ProbePort();
        var probe = new ProbeMediaUseCase(port).ProbeAsync("pending.mkv", new("tools", "logs", "cache"), lifetime.Token);
        lifetime.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe);
        Assert.Equal(lifetime.Token, port.Token);
    }

    private sealed class ProbePort : IMediaProber
    {
        private readonly Dictionary<string, TaskCompletionSource<MediaProbeResult>> pending = [];
        public Dictionary<string, ToolConfiguration> Tools { get; } = [];
        public Dictionary<string, MediaProbeResult> Results { get; } = [];
        public Exception? Failure { get; init; }
        public CancellationToken Token { get; private set; }
        public Task<MediaProbeResult> ProbeAsync(string path, ToolCallContext context, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("只读探测不能要求任务工作区。");
        public Task<MediaProbeResult> ProbeReadOnlyAsync(string path, ToolConfiguration tools, CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            Tools.Add(path, tools);
            if (Failure is { } failure) return Task.FromException<MediaProbeResult>(failure);
            var completion = new TaskCompletionSource<MediaProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Add(path, completion);
            return completion.Task.WaitAsync(cancellationToken);
        }
        public void Complete(string path)
        {
            var result = new MediaProbeResult(path, [], [], 123456789);
            Results.Add(path, result);
            pending[path].SetResult(result);
        }
    }
}
