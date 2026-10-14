using System.Diagnostics;
using System.Text.Json;
using MuxDeck.Application.Ports;
using MuxDeck.Application.Ports.Failures;
using MuxDeck.Contracts;
using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Infrastructure.IntegrationTests;

public sealed class ProcessTests
{
    [Fact]
    public async Task Real_process_preserves_argument_boundaries_and_utf8_streams()
    {
        var args = new[] { "中文 空格", "quote\"value", ";echo", "a&b", "" };
        var lines = new List<ProcessLine>();
        var result = await new ProcessRunner().RunAsync(ChildFixture.Request("echo", args), new Sink(line => { lock (lines) lines.Add(line); }), TestContext.Current.CancellationToken);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(args, JsonSerializer.Deserialize<string[]>(result.StandardOutput.Trim()));
        Assert.Equal("中文 stderr", result.StandardError.Trim());
        Assert.Contains(lines, line => !line.IsError && line.Text.StartsWith("[", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.IsError && line.Text == "中文 stderr");
    }

    [Fact]
    public async Task Capture_limit_keeps_draining_both_streams_without_deadlock()
    {
        var result = await new ProcessRunner().RunAsync(ChildFixture.Request("large") with { MaxCapturedCharacters = 128 }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.CaptureLimitExceeded);
        Assert.Equal(128, result.StandardOutput.Length);
        Assert.Equal(128, result.StandardError.Length);
    }

    [Fact]
    public async Task Real_exit_code_is_not_lost_in_low_level_runner()
    {
        var result = await new ProcessRunner().RunAsync(ChildFixture.Request("exit"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains("工具失败详情", result.StandardError);
    }

    [Fact]
    public async Task Cancel_stops_parent_and_its_live_child_and_drains_pipes()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var childId = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = new ProcessRunner().RunAsync(ChildFixture.Request("tree"), new Sink(line =>
        {
            if (line.Text.StartsWith("CHILD:", StringComparison.Ordinal))
                childId.TrySetResult(int.Parse(line.Text[6..]));
        }), cancellation.Token);
        var id = await childId.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        using var child = Process.GetProcessById(id);
        Assert.False(child.HasExited);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        await child.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(child.HasExited);
    }
}

public sealed class WorkspaceTests
{
    [Fact]
    public async Task Dispose_waits_for_owned_work_cleanup_and_keeps_log()
    {
        await using var fixture = await TestWorkspace.Create();
        var workspace = fixture.Workspace;
        var work = workspace.WorkDirectory;
        File.WriteAllText(Path.Combine(work, "中间文件"), "bytes");
        await workspace.WriteLogAsync("检查日志", TestContext.Current.CancellationToken);
        await workspace.DisposeAsync();
        Assert.False(Directory.Exists(work));
        Assert.Contains("检查日志", await File.ReadAllTextAsync(workspace.Log.Path, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(fixture.Input));
        await workspace.DisposeAsync();
    }

    [Fact]
    public async Task Factory_maps_unwritable_directory_and_preserves_io_cause()
    {
        await using var fixture = await TestWorkspace.Create();
        var failure = await Assert.ThrowsAsync<ToolFailureException>(() => new FileTaskWorkspaceFactory().CreateAsync(
            fixture.Context.Tools with { LogsDirectory = fixture.Input }, TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.WorkspaceIoFailed, failure.Code);
        Assert.IsAssignableFrom<IOException>(failure.InnerException);
    }

    [Fact]
    public async Task Cancelled_factory_does_not_create_workspace()
    {
        await using var fixture = await TestWorkspace.Create();
        var directory = Path.Combine(fixture.Root, "cancelled");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FileTaskWorkspaceFactory().CreateAsync(
            fixture.Context.Tools with { LogsDirectory = directory }, cancellation.Token));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task Log_boundary_keeps_cancel_and_reports_disposed_writer_as_failure()
    {
        await using var fixture = await TestWorkspace.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Workspace.WriteLogAsync("取消", cancellation.Token));
        await fixture.Workspace.DisposeAsync();
        var failure = await Assert.ThrowsAsync<ToolFailureException>(() => fixture.Workspace.WriteLogAsync("已释放", TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.WorkspaceIoFailed, failure.Code);
        Assert.IsType<ObjectDisposedException>(failure.InnerException);
    }
}

public sealed class PublisherTests
{
    [Fact]
    public async Task Publish_copies_bytes_retains_source_and_removes_staging_file()
    {
        await using var fixture = await TestWorkspace.Create();
        var destination = Path.Combine(fixture.Root, "输出 目录", "成品.mkv");
        var result = await new NoOverwritePublisher().PublishNoOverwriteAsync(new(fixture.Input), destination, TestContext.Current.CancellationToken);
        Assert.Equal(new OutputArtifact(destination), result);
        Assert.Equal(await File.ReadAllBytesAsync(fixture.Input, TestContext.Current.CancellationToken), await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(destination)!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Existing_target_is_not_overwritten_and_conflict_has_specific_code(bool directory)
    {
        await using var fixture = await TestWorkspace.Create();
        var destination = Path.Combine(fixture.Root, "成品.mkv");
        if (directory) Directory.CreateDirectory(destination); else File.WriteAllText(destination, "已有文件");
        var source = await File.ReadAllBytesAsync(fixture.Input, TestContext.Current.CancellationToken);
        var failure = await Assert.ThrowsAsync<ToolFailureException>(() =>
            new NoOverwritePublisher().PublishNoOverwriteAsync(new(fixture.Input), destination, TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.TargetAlreadyExists, failure.Code);
        Assert.IsAssignableFrom<IOException>(failure.InnerException);
        if (directory) Assert.True(Directory.Exists(destination)); else Assert.Equal("已有文件", File.ReadAllText(destination));
        Assert.Equal(source, await File.ReadAllBytesAsync(fixture.Input, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.partial"));
    }

    [Fact]
    public async Task Cancel_does_not_publish_or_leave_partial_file()
    {
        await using var fixture = await TestWorkspace.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new NoOverwritePublisher().PublishNoOverwriteAsync(new(fixture.Input), fixture.Output, cancellation.Token));
        Assert.False(File.Exists(fixture.Output));
        Assert.Empty(Directory.GetFiles(fixture.Workspace.WorkDirectory, "*.partial"));
    }

    [Fact]
    public async Task Missing_source_maps_workspace_failure_and_keeps_original_cause()
    {
        await using var fixture = await TestWorkspace.Create();
        var failure = await Assert.ThrowsAsync<ToolFailureException>(() =>
            new NoOverwritePublisher().PublishNoOverwriteAsync(new(fixture.Input + ".missing"), fixture.Output, TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.WorkspaceIoFailed, failure.Code);
        Assert.IsType<FileNotFoundException>(failure.InnerException);
        Assert.False(File.Exists(fixture.Output));
        Assert.Empty(Directory.GetFiles(fixture.Workspace.WorkDirectory, "*.partial"));
    }

    [Fact]
    public async Task Concurrent_publications_commit_exactly_once_without_overwrite()
    {
        await using var fixture = await TestWorkspace.Create();
        var second = Path.Combine(fixture.Root, "second.mkv");
        File.WriteAllText(second, "second");
        async Task<bool> Publish(string source)
        {
            try { await new NoOverwritePublisher().PublishNoOverwriteAsync(new(source), fixture.Output, TestContext.Current.CancellationToken); return true; }
            catch (ToolFailureException ex) { Assert.Equal(ErrorCodes.TargetAlreadyExists, ex.Code); return false; }
        }
        var results = await Task.WhenAll(Publish(fixture.Input), Publish(second));
        Assert.Single(results, value => value);
        var actual = await File.ReadAllBytesAsync(fixture.Output, TestContext.Current.CancellationToken);
        var firstBytes = await File.ReadAllBytesAsync(fixture.Input, TestContext.Current.CancellationToken);
        var secondBytes = await File.ReadAllBytesAsync(second, TestContext.Current.CancellationToken);
        Assert.True(actual.SequenceEqual(firstBytes) || actual.SequenceEqual(secondBytes));
        Assert.Empty(Directory.GetFiles(fixture.Workspace.WorkDirectory, "*.partial"));
    }
}

public sealed class CatalogTests
{
    [Fact]
    public async Task Catalog_resolves_each_capability_using_explicit_bundle_and_backend()
    {
        await using var fixture = await TestWorkspace.Create();
        var tools = fixture.Context.Tools;
        var backend = Path.Combine(tools.ToolsDirectory, "fonttools", "Scripts");
        foreach (var relative in new[] { "mkvtoolnix/mkvmerge.exe", "mkvtoolnix/mkvextract.exe",
            "assfontsubset/AssFontSubset.Console.exe", "fonttools/Scripts/pyftsubset.exe", "fonttools/Scripts/ttx.exe" })
        {
            var path = Path.Combine(tools.ToolsDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "fixture");
        }
        var descriptors = await new ToolCatalog().ResolveAsync(Enum.GetValues<ToolCapability>().ToEquatableArray(), tools, TestContext.Current.CancellationToken);
        Assert.Equal(6, descriptors.Length);
        Assert.All(descriptors, descriptor => Assert.True(File.Exists(descriptor.ExecutablePath)));
        Assert.Equal(Path.Combine(backend, "ttx.exe"), descriptors.Single(item => item.Capability == ToolCapability.InspectFonts).ExecutablePath);
        Assert.Equal(Path.Combine(tools.ToolsDirectory, "mkvtoolnix/mkvmerge.exe").Replace('/', Path.DirectorySeparatorChar),
            descriptors.Single(item => item.Capability == ToolCapability.ProbeMedia).ExecutablePath);
    }

    [Fact]
    public async Task Explicit_missing_backend_is_not_hidden_by_machine_path()
    {
        await using var fixture = await TestWorkspace.Create();
        var failure = await Assert.ThrowsAsync<ToolFailureException>(() => new ToolCatalog().ResolveAsync(
            [ToolCapability.InspectFonts], fixture.Context.Tools with { FontToolsDirectoryOverride = Path.Combine(fixture.Root, "missing") }, TestContext.Current.CancellationToken));
        Assert.Equal("MUX_TOOL_NOT_FOUND", failure.Code);
        Assert.Equal("找不到工具：ttx", failure.Message);
        Assert.Contains("ttx", failure.Message);
    }

    [Fact]
    public async Task Catalog_preserves_cancel_without_reading_environment()
    {
        await using var fixture = await TestWorkspace.Create();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ToolCatalog().ResolveAsync(
            [ToolCapability.ProbeMedia], fixture.Context.Tools, cancellation.Token));
    }

    [Fact]
    public void Clock_reports_utc_within_observed_interval()
    {
        var before = DateTimeOffset.UtcNow;
        var actual = new SystemClock().UtcNow;
        var after = DateTimeOffset.UtcNow;
        Assert.InRange(actual, before, after);
        Assert.Equal(TimeSpan.Zero, actual.Offset);
    }
}
