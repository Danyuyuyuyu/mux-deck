using MuxDeck.Application;
using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using MuxDeck.Core.Muxing;
using MuxDeck.Desktop.ViewModels;
using MuxDeck.Domain;
using MuxDeck.Infrastructure;
using Xunit;

namespace MuxDeck.Architecture.Tests;

// 4b-2：导入冲突快照必须包含已排除轨道，不能直接用只含启用项的 ToSpec。
public sealed class DraftImportConflictTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "MuxDeck-4b2-conflict-" + Guid.NewGuid().ToString("N"));

    public DraftImportConflictTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task Excluded_imported_audio_still_conflicts_when_imported_again()
    {
        var audio = Path.Combine(root, "a.mka");
        await File.WriteAllTextAsync(audio, "x", TestContext.Current.CancellationToken);
        var prober = new FakeProber();
        var service = new SingleMuxService(
            new ToolConfiguration(Path.Combine(root, "tools"), Path.Combine(root, "logs"), Path.Combine(root, "cache")),
            prober, _ => throw new NotSupportedException(), pathFacts: SystemPathFacts.Instance,
            collectFacts: SystemPathFacts.Instance.Collect, slotPathFacts: SystemPathFacts.Instance);
        var model = new SingleMuxViewModel(service, new TrackColumnLayout());

        var first = await model.PrepareMediaInputAsync([new(audio, InputCandidateKind.LocalFile)]);
        Assert.True(first.Succeeded, first.Message);
        Assert.True(model.CommitMediaInput(first.Plan!).Succeeded);
        var row = Assert.Single(model.ExternalTracks);
        row.IsIncluded = false;
        Assert.False(row.IsIncluded);

        var second = await model.PrepareMediaInputAsync([new(audio, InputCandidateKind.LocalFile)]);

        Assert.False(second.Succeeded);
        Assert.Contains("文件中的轨道已加入", second.Message);
    }

    private sealed class FakeProber : IProbeMedia
    {
        public Task<MediaProbeResult> ProbeAsync(string path, ToolConfiguration tools,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaProbeResult(path,
                [new(new TrackId(2), TrackKind.Audio, "audio", "AAC", null, new LanguageTag(""), null, "",
                    new TrackFlags(false, false), null, null, null, true, true)], [], null));
    }
}
