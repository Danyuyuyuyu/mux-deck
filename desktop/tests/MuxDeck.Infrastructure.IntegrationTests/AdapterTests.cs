using System.Text.Json;
using MuxDeck.Application.Ports;
using MuxDeck.Application.Ports.Failures;
using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Infrastructure.Muxing;
using Xunit;

namespace MuxDeck.Infrastructure.IntegrationTests;

public sealed class AdapterTests
{
    [Theory]
    [InlineData("probe")]
    [InlineData("subset")]
    [InlineData("mux")]
    [InlineData("tracks")]
    [InlineData("attachments")]
    [InlineData("inspector")]
    public async Task Adapters_pass_list_arguments_and_log_separate_utf8_streams(string adapter)
    {
        await using var fixture = await TestWorkspace.Create();
        var runner = new ChildToolRunner();
        await Invoke(adapter, runner, fixture, TestContext.Current.CancellationToken);
        Assert.NotEmpty(runner.Requests);
        foreach (var request in runner.Requests)
        {
            Assert.Equal(fixture.Workspace.WorkDirectory, request.WorkingDirectory);
            Assert.Equal(fixture.Workspace.WorkDirectory, request.Environment!["TEMP"]);
            Assert.Equal(fixture.Workspace.WorkDirectory, request.Environment["TMP"]);
        }
        var args = runner.Requests[0].Arguments;
        var work = fixture.Workspace.WorkDirectory;
        switch (adapter)
        {
            case "probe":
                Assert.Equal(new[] { "-J", fixture.Input }, args);
                break;
            case "subset":
                Assert.Equal(new[] { Path.Combine(work, "input-0", Path.GetFileName(fixture.Input)),
                    "--fonts", fixture.Root, "--output", Path.Combine(work, "afs-0"),
                    "--subset-backend", "PyFontTools", "--bin-path", Path.Combine(fixture.Root, "tools", "Scripts") }, args);
                Assert.Equal(new[] { "-q", "-t", "name", "-t", "cmap", "-t", "OS/2", "-o",
                    Path.Combine(work, "afs-0", "Test.ttf.xml"), Path.Combine(work, "afs-0", "Test.ttf") }, runner.Requests[1].Arguments);
                break;
            case "mux":
                Assert.Equal(new[] { "-o", fixture.Output, "--no-subtitles", "--no-attachments", fixture.Input,
                    "--sub-charset", "0:UTF-8", "--language", "0:zh-Hans", "--track-name", "0:中文 轨道",
                    "--default-track-flag", "0:1", "--forced-display-flag", "0:0", fixture.Input }, args);
                break;
            case "tracks":
                Assert.Equal(new[] { fixture.Input, "tracks", "--fullraw", "7:" + fixture.Output }, args);
                break;
            case "attachments":
                Assert.Equal(new[] { fixture.Input, "attachments", "4:" + fixture.Output }, args);
                break;
            case "inspector":
                Assert.Equal(new[] { "-q", "-t", "name", "-t", "cmap", "-t", "OS/2", "-o", args[8], fixture.Input }, args);
                break;
        }
        await fixture.Workspace.DisposeAsync();
        var log = await File.ReadAllTextAsync(fixture.Workspace.Log.Path, TestContext.Current.CancellationToken);
        Assert.Contains("stderr 中文 stderr", log);
        Assert.Contains(adapter == "probe" ? "stdout {" : "stdout 中文 stdout", log);
    }

    [Theory]
    [InlineData("probe")]
    [InlineData("subset")]
    [InlineData("mux")]
    [InlineData("tracks")]
    [InlineData("attachments")]
    [InlineData("inspector")]
    public async Task Adapters_map_real_exit_failure_and_preserve_stderr(string adapter)
    {
        await using var fixture = await TestWorkspace.Create();
        var failure = await Assert.ThrowsAsync<ToolFailureException>(() => Invoke(adapter, new ChildToolRunner("exit"), fixture, TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.ToolExitFailed, failure.Code);
        Assert.Contains("退出码 7", failure.Message);
        Assert.Contains("工具失败详情", failure.Message);
        Assert.False(File.Exists(fixture.Output));
    }

    [Theory]
    [InlineData("probe")]
    [InlineData("subset")]
    [InlineData("mux")]
    [InlineData("tracks")]
    [InlineData("attachments")]
    [InlineData("inspector")]
    public async Task Adapters_preserve_cancellation_and_stop_real_process(string adapter)
    {
        await using var fixture = await TestWorkspace.Create();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ready = false;
        var runner = new ChildToolRunner("wait", line =>
        {
            if (line.Text == "READY") { ready = true; cancellation.Cancel(); }
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Invoke(adapter, runner, fixture, cancellation.Token));
        Assert.True(ready);
        Assert.False(File.Exists(fixture.Output));
    }

    [Fact]
    public async Task Afs_fatal_diagnostic_blocks_even_when_exit_is_zero()
    {
        await using var fixture = await TestWorkspace.Create();
        var failure = await Assert.ThrowsAsync<ToolFailureException>(() => Invoke("subset", new ChildToolRunner("fatal"), fixture, TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.ToolFatalDiagnostic, failure.Code);
        Assert.Contains("Missing font", failure.Message);
    }

    [Fact]
    public async Task Invalid_probe_json_preserves_original_exception()
    {
        await using var fixture = await TestWorkspace.Create();
        var failure = await Assert.ThrowsAsync<ToolFailureException>(() => Invoke("probe", new ChildToolRunner("invalid"), fixture, TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.ToolOutputInvalid, failure.Code);
        Assert.IsAssignableFrom<JsonException>(failure.InnerException);
    }

    [Theory]
    [InlineData("{\"tracks\":[],\"errors\":[\"bad\"]}", "mkvmerge 探测报告错误")]
    [InlineData("{\"tracks\":[],\"container\":{\"recognized\":false}}", "文件容器不可识别")]
    public void Probe_output_errors_have_one_failure_boundary(string json, string message)
    {
        var failure = Assert.Throws<ToolFailureException>(() => MkvmergeProber.Parse("video.mkv", json));
        Assert.Equal(ErrorCodes.ToolOutputInvalid, failure.Code);
        Assert.Contains(message, failure.Message);
        Assert.IsType<InvalidDataException>(failure.InnerException);
    }

    [Theory]
    [InlineData("{\"tracks\":{}}")]
    [InlineData("{\"tracks\":[],\"container\":{\"recognized\":\"yes\"}}")]
    public void Wrong_json_field_types_are_also_output_failures(string json)
    {
        var failure = Assert.Throws<ToolFailureException>(() => MkvmergeProber.Parse("video.mkv", json));
        Assert.Equal(ErrorCodes.ToolOutputInvalid, failure.Code);
        Assert.IsType<InvalidOperationException>(failure.InnerException);
    }

    [Fact]
    public async Task Ttx_disallows_dtd_and_preserves_xml_failure()
    {
        await using var fixture = await TestWorkspace.Create();
        var failure = await Assert.ThrowsAsync<ToolFailureException>(() => Invoke("inspector", new ChildToolRunner("xml-invalid"), fixture, TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.ToolOutputInvalid, failure.Code);
        Assert.IsType<System.Xml.XmlException>(failure.InnerException);
    }

    [Fact]
    public async Task Timeout_maps_to_failure_with_original_timeout_exception()
    {
        await using var fixture = await TestWorkspace.Create();
        var failure = await Assert.ThrowsAsync<ToolFailureException>(() => new ToolExecutor(new ChildToolRunner("wait")).RunAsync(
            "mkvmerge.exe", ["-J", fixture.Input], fixture.Workspace.WorkDirectory, null, TestContext.Current.CancellationToken, TimeSpan.FromMilliseconds(500)));
        Assert.Equal("MUX_TOOL_TIMEOUT", failure.Code);
        Assert.Equal("工具 mkvmerge.exe 超时（00:00:00.5000000）。", failure.Message);
        Assert.IsType<TimeoutException>(failure.InnerException);
    }

    [Fact]
    public void Mux_uses_real_audio_id_flags_and_deterministic_attachment_names()
    {
        var args = MkvmergeMuxer.BuildArguments(new("source.mkv",
            [new("audio.mka", TrackKind.Audio, new(7), "名字", new("zh-Hant"), new(false, true))],
            [new("a/font.otf"), new("b/font.ttf")], new("result.mkv")));
        Assert.Equal(new[] { "-o", "result.mkv", "--no-subtitles", "--no-attachments", "source.mkv",
            "--no-video", "--no-subtitles", "--no-attachments", "--no-chapters", "--no-global-tags", "--no-track-tags",
            "--audio-tracks", "7", "--language", "7:zh-Hant", "--track-name", "7:名字",
            "--default-track-flag", "7:0", "--forced-display-flag", "7:1", "audio.mka",
            "--attachment-name", "001-font.otf", "--attachment-mime-type", "font/otf", "--attach-file", "a/font.otf",
            "--attachment-name", "002-font.ttf", "--attachment-mime-type", "font/ttf", "--attach-file", "b/font.ttf" }, args);
    }

    private static async Task Invoke(string adapter, IMuxToolRunner runner, TestWorkspace fixture, CancellationToken token = default)
    {
        var context = fixture.Context;
        var catalog = new TestCatalog(fixture.Root);
        var subtitle = new ExternalTrackSpec(fixture.Input, TrackKind.Subtitle, null, "中文 轨道", new("zh-Hans"), new(true, false));
        switch (adapter)
        {
            case "probe":
                var probe = await new MkvmergeProber(runner, catalog).ProbeAsync(fixture.Input, context, token);
                var track = Assert.Single(probe.Tracks);
                Assert.Equal(7, track.Id.Value);
                Assert.Equal(TrackKind.Audio, track.Kind);
                Assert.Equal("audio", track.RawType);
                Assert.Equal(new LanguageTag("zh-Hans"), track.LanguageIetf);
                Assert.True(track.HasDefaultFlag);
                Assert.True(track.HasForcedFlag);
                Assert.Equal(new TrackFlags(true, false), track.Flags);
                Assert.Equal(48000, track.AudioSamplingFrequency);
                Assert.Equal(12345, probe.DurationNanoseconds);
                Assert.Equal(4, Assert.Single(probe.Attachments).Id);
                break;
            case "subset":
                var subset = await new AfsSubsetter(runner, catalog).SubsetAsync(new(subtitle, new(fixture.Root)), context, cancellationToken: token);
                Assert.Equal(await File.ReadAllTextAsync(fixture.Input, token), await File.ReadAllTextAsync(subset.CorrectedSubtitle.Path, token));
                Assert.Single(subset.Fonts);
                Assert.Empty(subset.Diagnostics);
                break;
            case "mux":
                var mux = await new MkvmergeMuxer(runner, catalog).MuxAsync(new(fixture.Input, [subtitle], [], new(fixture.Output)), context, cancellationToken: token);
                Assert.Equal(new WorkFile(fixture.Output), mux.Artifact);
                Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(mux.Artifact.Path, token));
                break;
            case "tracks":
                await new MkvextractExtractor(runner, catalog).ExtractTracksAsync(fixture.Input,
                    [new(new(7), new(fixture.Output), TrackExtractionFormat.FullRaw)], context, token);
                Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(fixture.Output, token));
                break;
            case "attachments":
                await new MkvextractExtractor(runner, catalog).ExtractAttachmentsAsync(fixture.Input,
                    [new(4, new(fixture.Output))], context, token);
                Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(fixture.Output, token));
                break;
            case "inspector":
                var font = await new TtxFontInspector(runner, catalog).InspectAsync(new(fixture.Input, 0), context, token);
                Assert.Equal(new[] { "TEST", "TEST REGULAR" }, font.Names);
                Assert.Equal(new[] { 65 }, font.Glyphs);
                Assert.False(font.Bold); Assert.False(font.Italic); Assert.Equal(400, font.Weight);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(adapter));
        }
    }
}

internal sealed class TestCatalog(string root) : IToolCatalog
{
    public Task<EquatableArray<ToolDescriptor>> ResolveAsync(EquatableArray<ToolCapability> capabilities,
        ToolConfiguration tools, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(capabilities.Select(capability => new ToolDescriptor(capability, capability switch
        {
            ToolCapability.ProbeMedia or ToolCapability.MuxMedia => "mkvmerge.exe",
            ToolCapability.ExtractMedia => "mkvextract.exe",
            ToolCapability.SubsetFonts => "AssFontSubset.Console.exe",
            ToolCapability.FontSubsetBackend => Path.Combine(root, "tools", "Scripts", "pyftsubset.exe"),
            ToolCapability.InspectFonts => Path.Combine(root, "tools", "Scripts", "ttx.exe"),
            _ => throw new ArgumentOutOfRangeException(nameof(capability))
        })).ToEquatableArray());
    }
}

internal sealed class TestWorkspace : IAsyncDisposable
{
    private TestWorkspace(string root, ITaskWorkspace workspace, ToolConfiguration tools)
    { Root = root; Workspace = workspace; Context = new(tools, workspace); }
    internal string Root { get; }
    internal ITaskWorkspace Workspace { get; }
    internal ToolCallContext Context { get; }
    internal string Input => Path.Combine(Root, "中文 输入 字幕.ass");
    internal string Output => Path.Combine(Workspace.WorkDirectory, "结果 输出.mkv");
    internal static async Task<TestWorkspace> Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "MuxDeck-Infrastructure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var tools = new ToolConfiguration(Path.Combine(root, "tools"), Path.Combine(root, "logs"), Path.Combine(root, "cache"));
        var workspace = await new FileTaskWorkspaceFactory().CreateAsync(tools);
        File.WriteAllText(Path.Combine(root, "中文 输入 字幕.ass"), ChildFixture.Ass);
        return new(root, workspace, tools);
    }
    public async ValueTask DisposeAsync()
    {
        // Windows 的终止信号先于 dotnet 宿主释放当前目录句柄；测试清理等待内核收尾。
        await Task.Delay(100, CancellationToken.None);
        await Workspace.DisposeAsync();
        Directory.Delete(Root, recursive: true);
    }
}
