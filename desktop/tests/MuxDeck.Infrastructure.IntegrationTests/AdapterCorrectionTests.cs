using MuxDeck.Application.Ports;
using MuxDeck.Application.Ports.Failures;
using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Infrastructure.Muxing;
using Xunit;

namespace MuxDeck.Infrastructure.IntegrationTests;

public sealed class AdapterCorrectionTests
{
    [Theory]
    [InlineData("mapped", false)]
    [InlineData("mapped", true)]
    [InlineData("io", false)]
    [InlineData("io", true)]
    [InlineData("disposed", false)]
    [InlineData("disposed", true)]
    public async Task Failed_command_or_stream_log_does_not_fail_real_tool_call(string failure, bool streamOnly)
    {
        await using var fixture = await TestWorkspace.Create();
        var context = fixture.Context with { Workspace = new FailingLogWorkspace(fixture.Workspace, failure, streamOnly) };
        IMediaProber prober = new MkvmergeProber(new ChildToolRunner(), new TestCatalog(fixture.Root));
        var result = await prober.ProbeAsync(fixture.Input, context, TestContext.Current.CancellationToken);
        Assert.Equal(fixture.Input, result.Path);
        Assert.Equal(7, Assert.Single(result.Tracks).Id.Value);
        Assert.Equal(12345, result.DurationNanoseconds);
        Assert.Equal(4, Assert.Single(result.Attachments).Id);
    }

    [Fact]
    public async Task Failed_log_does_not_hide_real_fatal_diagnostic()
    {
        await using var fixture = await TestWorkspace.Create();
        var context = fixture.Context with { Workspace = new FailingLogWorkspace(fixture.Workspace, "mapped", true) };
        IFontSubsetter subsetter = new AfsSubsetter(new ChildToolRunner("fatal"), new TestCatalog(fixture.Root));
        var subtitle = new ExternalTrackSpec(fixture.Input, TrackKind.Subtitle, null, "字幕", new("und"), new(true, false));
        var failure = await Assert.ThrowsAsync<ToolFailureException>(() => subsetter.SubsetAsync(
            new(subtitle, new(fixture.Root)), context, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.ToolFatalDiagnostic, failure.Code);
        Assert.Contains("Missing font", failure.Message);
    }

    private sealed class FailingLogWorkspace(ITaskWorkspace inner, string failure, bool streamOnly) : ITaskWorkspace
    {
        public string WorkDirectory => inner.WorkDirectory;
        public LogRef Log => inner.Log;
        public Task WriteLogAsync(string message, CancellationToken cancellationToken = default)
        {
            if (streamOnly && !message.StartsWith("stdout ", StringComparison.Ordinal) &&
                !message.StartsWith("stderr ", StringComparison.Ordinal))
                return inner.WriteLogAsync(message, cancellationToken);
            Exception exception = failure switch
            {
                "mapped" => new ToolFailureException(ErrorCodes.WorkspaceIoFailed, "日志磁盘写入失败", "受控日志失败", new IOException("日志磁盘写入失败")),
                "io" => new IOException("日志磁盘写入失败"),
                "disposed" => new ObjectDisposedException("task.log"),
                _ => throw new ArgumentOutOfRangeException(nameof(failure))
            };
            return Task.FromException(exception);
        }
        // 外层 fixture 持有实际工作区；日志替身不重复释放资源。
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
