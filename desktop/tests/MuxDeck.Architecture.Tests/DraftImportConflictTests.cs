using System.Reflection;
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

    [Fact]
    public async Task Rejected_replacement_keeps_row_selection_and_does_not_report_success()
    {
        var audio = Path.Combine(root, "two-tracks.mka");
        await File.WriteAllTextAsync(audio, "x", TestContext.Current.CancellationToken);
        var model = Create(new FakeProber(2, 3));
        var prepared = await model.PrepareMediaInputAsync([new(audio, InputCandidateKind.LocalFile)]);
        Assert.True(prepared.Succeeded, prepared.Message);
        Assert.True(model.CommitMediaInput(prepared.Plan!).Succeeded);
        Assert.Equal(2, model.ExternalTracks.Count);
        var row = model.ExternalTracks[0];
        var selected = row;
        model.SelectedTrack = selected;
        var path = row.InputPath;

        // 用同一容器中的轨号 3 替换轨号 2，与已有轨号 3 冲突。
        var replaced = model.ReplaceImportedTrack(row,
            new ImportedTrack(audio, Media(3), ""));

        Assert.False(replaced);
        Assert.Equal(path, row.InputPath);
        Assert.Equal(selected, model.SelectedTrack);
        Assert.False(model.Notification.IsSuccess);
        Assert.Contains("文件中的轨道已加入", model.InteractionMessage);
        Assert.Equal(2, model.ExternalTracks.Count);
    }

    [Fact]
    public async Task Rejected_removal_keeps_the_row_and_path()
    {
        var audio = Path.Combine(root, "kept.mka");
        await File.WriteAllTextAsync(audio, "x", TestContext.Current.CancellationToken);
        var model = Create(new FakeProber(2));
        var prepared = await model.PrepareMediaInputAsync([new(audio, InputCandidateKind.LocalFile)]);
        Assert.True(prepared.Succeeded, prepared.Message);
        Assert.True(model.CommitMediaInput(prepared.Plan!).Succeeded);
        var row = Assert.Single(model.ExternalTracks);
        var path = row.InputPath;

        // 绕过 VM 从草稿移除，模拟界面持有的行已不在草稿中。
        var draft = (SingleMuxDraft)typeof(SingleMuxViewModel)
            .GetField("draft", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(model)!;
        Assert.True(draft.RemoveExternalTrack(row.Id).Succeeded);

        model.RemoveExternalTrack(row);

        Assert.Contains(row, model.ExternalTracks);
        Assert.Equal(path, row.InputPath);
    }

    private SingleMuxViewModel Create(IProbeMedia prober)
    {
        var service = new SingleMuxService(
            new ToolConfiguration(Path.Combine(root, "tools"), Path.Combine(root, "logs"), Path.Combine(root, "cache")),
            prober, _ => throw new NotSupportedException(), pathFacts: SystemPathFacts.Instance,
            collectFacts: SystemPathFacts.Instance.Collect, slotPathFacts: SystemPathFacts.Instance);
        return new SingleMuxViewModel(service, new TrackColumnLayout());
    }

    private static MediaTrack Media(int id) =>
        new(id, "audio", "AAC", null, "", null, "", false, false, null, null, null, true, true);

    private sealed class FakeProber(params int[] ids) : IProbeMedia
    {
        public Task<MediaProbeResult> ProbeAsync(string path, ToolConfiguration tools,
            CancellationToken cancellationToken = default)
        {
            var used = ids.Length == 0 ? [2] : ids;
            return Task.FromResult(new MediaProbeResult(path,
                [.. used.Select(id => new MediaTrackInfo(new TrackId(id), TrackKind.Audio, "audio", "AAC",
                    null, new LanguageTag(""), null, "", new TrackFlags(false, false), null, null, null, true, true))],
                [], null));
        }
    }
}
