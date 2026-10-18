using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using MuxDeck.Core.Muxing;
using MuxDeck.Desktop.ViewModels;
using MuxDeck.Domain;
using MuxDeck.Infrastructure;
using Xunit;

namespace MuxDeck.Architecture.Tests;

// 4b-2：Domain 不再保存“源文件名”占位；Desktop 补全后的预览与 621ee8b 逐字一致。
public sealed class SingleMuxPreviewTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "MuxDeck-4b2-preview-" + Guid.NewGuid().ToString("N"));

    public SingleMuxPreviewTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Theory]
    [InlineData(false, "", "源文件名.muxed.mkv")]
    [InlineData(true, "", "源 视频.muxed.mkv")]
    [InlineData(true, "自定义 成品.mkv", "自定义 成品.mkv")]
    [InlineData(true, " ", "源 视频.muxed.mkv")]
    public async Task Preview_matches_the_frozen_vm_text(bool hasVideo, string requestedName, string expected)
    {
        var model = Create();
        if (hasVideo) model.VideoPath = Path.Combine(root, "源 视频.MKV");
        model.OutputFileName = requestedName;

        Assert.Equal(expected, model.ResolvedOutputFileName);
        await model.CancelAndWaitAsync();
    }

    private SingleMuxViewModel Create()
    {
        var service = new SingleMuxService(
            new ToolConfiguration(Path.Combine(root, "tools"), Path.Combine(root, "logs"), Path.Combine(root, "cache")),
            new NoProbe(), _ => throw new NotSupportedException(), pathFacts: SystemPathFacts.Instance,
            collectFacts: SystemPathFacts.Instance.Collect, slotPathFacts: SystemPathFacts.Instance);
        return new SingleMuxViewModel(service, new TrackColumnLayout());
    }

    private sealed class NoProbe : IProbeMedia
    {
        public Task<MediaProbeResult> ProbeAsync(string path, ToolConfiguration tools,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaProbeResult(path, [], [], null));
    }
}
