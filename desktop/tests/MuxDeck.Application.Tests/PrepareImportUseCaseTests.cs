using MuxDeck.Application;
using MuxDeck.Application.Ports;
using MuxDeck.Application.Ports.Failures;
using MuxDeck.Contracts;
using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Application.Tests;

public sealed class PrepareImportUseCaseTests
{
    private const string Ass = "[Script Info]\nTitle: t\nScriptType: v4.00+\n\n" +
        "[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n" +
        "Style: Default,Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,0,2,10,10,10,1\n\n" +
        "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
        "Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,你好\n";

    [Fact]
    public async Task Every_batch_error_code_keeps_the_domain_path_fact()
    {
        var cases = new (string Code, InputPathFacts[] Facts, string? Path)[]
        {
            (ErrorCodes.ImportEmptySelection, [], null),
            (ErrorCodes.ImportNonLocal, [Fact("http://x/a.ass", InputLocationKind.NonLocal, extension: ".ass")], null),
            (ErrorCodes.ImportPathNotAbsolute, [Fact("a.ass", InputLocationKind.LocalFile, full: "a.ass", qualified: false, extension: ".ass")], "a.ass"),
            (ErrorCodes.ImportPathMissing, [Fact("C:/missing.ass", InputLocationKind.LocalFile, extension: ".ass", exists: false)], "C:/missing.ass"),
            (ErrorCodes.ImportTypeUnsupported, [Fact("C:/a.txt", InputLocationKind.LocalFile, extension: ".txt")], "C:/a.txt"),
            (ErrorCodes.ImportDuplicate, [Fact("C:/a.ass", InputLocationKind.LocalFile, extension: ".ass"), Fact("C:/b.ass", InputLocationKind.LocalFile, full: "C:/a.ass", extension: ".ass")], null),
            (ErrorCodes.ImportFontDirectories, [Fact("C:/fonts-a", InputLocationKind.LocalDirectory, directory: "C:/fonts-a"), Fact("C:/fonts-b", InputLocationKind.LocalDirectory, directory: "C:/fonts-b")], null)
        };
        foreach (var item in cases)
        {
            var result = await Create(new FactsPort(item.Facts)).PrepareAsync(Request(item.Facts.Select(f => new InputItem(f.Path, f.Kind))),
                Tools(), TestContext.Current.CancellationToken);
            Assert.Equal(item.Code, result.Failure!.Code);
            Assert.Equal(item.Path, result.Failure.Detail);
        }
    }

    [Fact]
    public async Task Ass_usage_failure_preserves_the_old_message_and_path()
    {
        var path = "C:/bad.ass";
        var facts = Fact(path, InputLocationKind.LocalFile, extension: ".ass");
        var reader = new ReaderPort { SubtitleText = Ass.Replace("Default,,0,0,0,,你好", "Missing,,0,0,0,,你好") };
        var result = await Create(new FactsPort([facts]), reader: reader, prober: new ProbePort(SubtitleProbe()))
            .PrepareAsync(Request([new(path, InputLocationKind.LocalFile)]), Tools(), TestContext.Current.CancellationToken);
        Assert.Equal(ErrorCodes.ImportAssUsage, result.Failure!.Code);
        Assert.Equal("字幕使用了未定义样式：Missing", result.Failure.Message);
        Assert.Equal(path, result.Failure.Detail);
    }

    [Fact]
    public async Task Tool_failure_keeps_code_message_and_detail()
    {
        var failure = new ToolFailureException(ErrorCodes.ToolOutputInvalid, "原始消息", "原始 detail");
        var path = "C:/bad.mkv";
        var facts = Fact(path, InputLocationKind.LocalFile, extension: ".mkv");
        var result = await Create(new FactsPort([facts]), prober: new ProbePort(failure))
            .PrepareAsync(Request([new(path, InputLocationKind.LocalFile)]), Tools(), TestContext.Current.CancellationToken);
        Assert.Equal(failure.Code, result.Failure!.Code);
        Assert.Equal(failure.Message, result.Failure.Message);
        Assert.Equal(failure.Detail, result.Failure.Detail);
    }

    [Fact]
    public async Task Cancellation_is_rethrown_from_ports()
    {
        using var cancel = new CancellationTokenSource();
        var facts = Fact("C:/pending.mkv", InputLocationKind.LocalFile, extension: ".mkv");
        var port = new FactsPort([facts]) { Cancel = true };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(port)
            .PrepareAsync(Request([new(facts.Path, facts.Kind)]), Tools(), cancel.Token));
    }

    [Fact]
    public async Task Processing_errors_keep_their_paths_and_conflicts_are_rejected()
    {
        var video = Fact("C:/v.mkv", InputLocationKind.LocalFile, extension: ".mkv");
        var secondVideo = Fact("C:/v2.mkv", InputLocationKind.LocalFile, extension: ".mkv");
        var audio = Fact("C:/a.mp3", InputLocationKind.LocalFile, extension: ".mp3");
        var subtitle = Fact("C:/s.ass", InputLocationKind.LocalFile, extension: ".ass");
        var srt = Fact("C:/bad.srt", InputLocationKind.LocalFile, extension: ".srt");
        var tooManyVideos = await Create(new FactsPort([video, secondVideo]), prober: new ProbePort(new MediaProbeResult("v", [Track(1, TrackKind.Video, "AVC", "VIDEO")], [], null)))
            .PrepareAsync(Request([new(video.Path, video.Kind), new(secondVideo.Path, secondVideo.Kind)]), Tools(), TestContext.Current.CancellationToken);
        Assert.Equal(ErrorCodes.ImportSourceVideoCount, tooManyVideos.Failure!.Code);
        Assert.Null(tooManyVideos.Failure.Detail);
        var badAudio = await Create(new FactsPort([audio]), prober: new ProbePort(new MediaProbeResult("a", [], [], null)))
            .PrepareAsync(Request([new(audio.Path, audio.Kind)]), Tools(), TestContext.Current.CancellationToken);
        Assert.Equal(ErrorCodes.ImportAudioMissing, badAudio.Failure!.Code);
        Assert.Equal(audio.Path, badAudio.Failure.Detail);

        var badSubtitle = await Create(new FactsPort([subtitle]), reader: new ReaderPort { SubtitleText = Ass },
                prober: new ProbePort(new MediaProbeResult("s", [Track(1, TrackKind.Audio, "AAC", "AUDIO")], [], null)))
            .PrepareAsync(Request([new(subtitle.Path, subtitle.Kind)]), Tools(), TestContext.Current.CancellationToken);
        Assert.Equal(ErrorCodes.ImportSubtitleMismatch, badSubtitle.Failure!.Code);
        Assert.Equal(subtitle.Path, badSubtitle.Failure.Detail);

        var badTimeline = await Create(new FactsPort([srt]), reader: new ReaderPort { SubtitleText = "plain" },
                prober: new ProbePort(SubtitleProbe()))
            .PrepareAsync(Request([new(srt.Path, srt.Kind)]), Tools(), TestContext.Current.CancellationToken);
        Assert.Equal(ErrorCodes.ImportSubtitleTimeline, badTimeline.Failure!.Code);
        Assert.Equal(srt.Path, badTimeline.Failure.Detail);

        var current = new MuxJobSpec("C:/v.mkv", [new ExternalTrackSpec(subtitle.Path, TrackKind.Subtitle, new(1), "", default, default)], [], new("C:/fonts"), new("C:/out"), Tools());
        var conflict = await Create(new FactsPort([subtitle]), reader: new ReaderPort { SubtitleText = Ass },
                prober: new ProbePort(SubtitleProbe()))
            .PrepareAsync(new(Guid.NewGuid(), 1, [new(subtitle.Path, subtitle.Kind)], current), Tools(), TestContext.Current.CancellationToken);
        Assert.Equal(ErrorCodes.ImportTrackConflict, conflict.Failure!.Code);
        Assert.Null(conflict.Failure.Detail);
    }

    [Fact]
    public async Task Plan_contains_video_fonts_tracks_language_and_replacement_confirmation()
    {
        var video = Fact("C:/new.mkv", InputLocationKind.LocalFile, extension: ".mkv");
        var fonts = Fact("C:/fonts", InputLocationKind.LocalDirectory, extension: "", directory: "C:/fonts");
        var subtitle = Fact("C:/影片.简体.ass", InputLocationKind.LocalFile, extension: ".ass");
        var audio = Fact("C:/voice.mp3", InputLocationKind.LocalFile, extension: ".mp3");
        var reader = new ReaderPort { SubtitleText = Ass };
        var prober = new ProbePort(path => path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)
            ? new MediaProbeResult(path, [Track(1, TrackKind.Video, "AVC", "V")], [], null)
            : path.EndsWith(".ass", StringComparison.OrdinalIgnoreCase) ? SubtitleProbe()
            : new MediaProbeResult(path, [Track(2, TrackKind.Audio, "AAC", "AUDIO")], [], null));
        var current = new MuxJobSpec("C:/old.mkv", [], [], new("C:/old-fonts"), new("C:/out"), Tools());
        var result = await Create(new FactsPort([video, fonts, subtitle, audio]), reader, prober)
            .PrepareAsync(new(Guid.NewGuid(), 7, [new(video.Path, video.Kind), new(fonts.Path, fonts.Kind), new(subtitle.Path, subtitle.Kind), new(audio.Path, audio.Kind)], current),
                Tools(), TestContext.Current.CancellationToken);
        Assert.Equal(ImportStatus.Prepared, result.Status);
        var plan = result.Plan!;
        Assert.Equal(video.Path, plan.VideoPath);
        Assert.Equal(fonts.Directory, plan.Fonts!.Directory);
        Assert.Equal(2, plan.Tracks.Length);
        Assert.Contains(plan.Tracks, track => track.Path == subtitle.Path && track.Language.Value == "zh-Hans");
        Assert.Contains(plan.Tracks, track => track.Path == audio.Path);
        Assert.True(plan.RequiresReplacementConfirmation);
    }

    private static PrepareImportUseCase Create(FactsPort facts, ReaderPort? reader = null, ProbePort? prober = null) =>
        new(prober ?? new ProbePort(SubtitleProbe()), reader ?? new ReaderPort { SubtitleText = Ass }, facts);
    private static ToolConfiguration Tools() => new("tools", "logs", "cache");
    private static ImportRequest Request(IEnumerable<InputItem> inputs) => new(Guid.NewGuid(), 1, new EquatableArray<InputItem>(inputs), null);
    private static InputPathFacts Fact(string path, InputLocationKind kind, string? full = null, bool qualified = true,
        string? extension = null, string? directory = null, bool exists = true) => new(path, qualified, full ?? path, extension ?? Path.GetExtension(path),
        directory ?? "C:/", kind, exists && kind == InputLocationKind.LocalFile, exists && kind == InputLocationKind.LocalDirectory, false);
    private static MediaProbeResult SubtitleProbe() => new("s", [Track(1, TrackKind.Subtitle, "ASS", "S_TEXT/ASS")], [], null);
    private static MediaTrackInfo Track(int id, TrackKind kind, string codec, string codecId) =>
        new(new(id), kind, kind.ToString(), codec, codecId, default, null, "", default, null, null, null, true, true);

    private sealed class FactsPort(IReadOnlyList<InputPathFacts> values) : IPathFacts
    {
        public bool Cancel { get; init; }
        public Task<EquatableArray<InputPathFacts>> CollectAsync(IReadOnlyList<InputItem> items, CancellationToken cancellationToken = default)
        {
            if (Cancel) throw new OperationCanceledException(cancellationToken);
            return Task.FromResult(new EquatableArray<InputPathFacts>(values));
        }
    }

    private sealed class ProbePort : IProbeMedia
    {
        private readonly Func<string, MediaProbeResult> factory = _ => throw new InvalidOperationException();
        private readonly Exception? failure;
        public ProbePort(MediaProbeResult result) : this(_ => result) { }
        public ProbePort(Exception failure) { this.failure = failure; }
        public ProbePort(Func<string, MediaProbeResult> factory) => this.factory = factory;
        public Task<MediaProbeResult> ProbeAsync(string path, ToolConfiguration tools, CancellationToken cancellationToken = default) =>
            failure is { } ex ? Task.FromException<MediaProbeResult>(ex) : Task.FromResult(factory(path));
    }

    private sealed class ReaderPort : IMuxInputReader
    {
        public string? SubtitleText { get; init; }
        public Task<MuxVideoFacts> ReadVideoAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MuxFontDirectoryFacts> ReadFontDirectoryAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MuxOutputFacts> ReadOutputAsync(OutputSpec output, string videoPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MuxTrackFileFacts> ReadTrackAsync(ExternalTrackSpec track, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MuxTrackFileFacts(track, true, SubtitleText));
        public Task<bool> DestinationExistsAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
