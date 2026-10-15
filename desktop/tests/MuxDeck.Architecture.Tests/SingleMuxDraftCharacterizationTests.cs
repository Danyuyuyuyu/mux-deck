using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using MuxDeck.Core.Muxing;
using MuxDeck.Desktop.ViewModels;
using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Architecture.Tests;

// 4b-1 特征测试：在未修改的生产代码上逐字冻结 SingleMuxViewModel 的草稿行为。
// 迁移期间本文件一行都不能改；4b-2 改为 MuxDraft 后，同样入参必须产生同样的修订号、错误码与 MuxJobSpec。
public sealed class SingleMuxDraftCharacterizationTests : IDisposable
{
    private const string ValidAss =
        "[Script Info]\nTitle: t\nScriptType: v4.00+\n\n" +
        "[V4+ Styles]\n" +
        "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n" +
        "Style: Default,Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,0,2,10,10,10,1\n\n" +
        "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
        "Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,你好\n";

    private readonly string root = Path.Combine(Path.GetTempPath(), "MuxDeck-4b1-" + Guid.NewGuid().ToString("N"));

    public SingleMuxDraftCharacterizationTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    // ---------- 修订号：逐项操作各增几次 ----------

    [Fact]
    public void Video_path_change_increments_revision_exactly_once()
    {
        var model = Create();
        var revision = model.DraftRevision;
        model.VideoPath = Write("v.mkv", "x");
        Assert.Equal(revision + 1, model.DraftRevision);
    }

    [Fact]
    public void Subtitle_slot_commit_increments_revision_exactly_once()
    {
        var model = Create();
        var sc = Write("sc.ass", ValidAss);
        var tc = Write("tc.ass", ValidAss);
        var revision = model.DraftRevision;
        Assert.True(model.CommitInput(model.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, sc)]).Plan!).Succeeded);
        Assert.Equal(revision + 1, model.DraftRevision);
        revision = model.DraftRevision;
        Assert.True(model.CommitInput(model.PrepareSubtitleInputs([Assignment(SubtitleSlot.Tc, tc)]).Plan!).Succeeded);
        Assert.Equal(revision + 1, model.DraftRevision);
    }

    [Fact]
    public void Fonts_directory_commit_increments_revision_exactly_once()
    {
        var model = Create();
        var revision = model.DraftRevision;
        Assert.True(model.CommitInput(model.PrepareFontsDirectory([new(NewDirectory("fonts"), InputCandidateKind.LocalDirectory)]).Plan!).Succeeded);
        Assert.Equal(revision + 1, model.DraftRevision);
    }

    [Fact]
    public void Output_directory_commit_increments_revision_exactly_once()
    {
        var model = Create();
        var revision = model.DraftRevision;
        Assert.True(model.CommitInput(model.PrepareOutputDirectory([new(NewDirectory("out"), InputCandidateKind.LocalDirectory)]).Plan!).Succeeded);
        Assert.Equal(revision + 1, model.DraftRevision);
    }

    [Fact]
    public void Output_file_name_assignment_increments_revision_exactly_once()
    {
        var model = Create();
        var revision = model.DraftRevision;
        model.OutputFileName = "成品.mkv";
        Assert.Equal(revision + 1, model.DraftRevision);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("default")]
    [InlineData("forced")]
    [InlineData("included")]
    public void Track_field_change_increments_revision_exactly_once(string field)
    {
        var model = Create();
        var revision = model.DraftRevision;
        switch (field)
        {
            case "name": model.ScTrack.Name = "简体"; break;
            case "default": model.ScTrack.IsDefault = !model.ScTrack.IsDefault; break;
            case "forced": model.ScTrack.IsForced = !model.ScTrack.IsForced; break;
            default: model.ScTrack.IsIncluded = !model.ScTrack.IsIncluded; break;
        }
        Assert.Equal(revision + 1, model.DraftRevision);
    }

    [Fact]
    public void Single_commit_changing_multiple_fields_increments_revision_once()
    {
        var model = Create();
        var sc = Write("sc.ass", ValidAss);
        var tc = Write("tc.ass", ValidAss);
        Assert.True(model.CommitInput(model.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, sc)]).Plan!).Succeeded);
        var revision = model.DraftRevision;
        // 一次接纳批次同时设置两个槽位并重算默认旗标，只算一次修订。
        Assert.True(model.CommitInput(model.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, sc), Assignment(SubtitleSlot.Tc, tc)]).Plan!, true).Succeeded);
        Assert.Equal(revision + 1, model.DraftRevision);
    }

    [Fact]
    public void Same_value_assignment_does_not_increment_revision()
    {
        var model = Create();
        var video = Write("v.mkv", "x");
        model.VideoPath = video;
        model.OutputFileName = "固定.mkv";
        model.ScTrack.Name = "固定名称";
        var revision = model.DraftRevision;
        model.VideoPath = video;
        model.ScSubtitle = model.ScSubtitle;
        model.TcSubtitle = model.TcSubtitle;
        model.FontsDirectory = model.FontsDirectory;
        model.OutputDirectory = model.OutputDirectory;
        model.OutputFileName = "固定.mkv";
        model.ScTrack.Name = "固定名称";
        model.ScTrack.IsIncluded = model.ScTrack.IsIncluded;
        Assert.Equal(revision, model.DraftRevision);
    }

    [Fact]
    public void Swapping_sc_and_tc_in_one_prepare_commit_increments_revision_exactly_once()
    {
        var model = Create();
        var sc = Write("简体.ass", ValidAss);
        var tc = Write("繁体.ass", ValidAss);
        Assert.True(model.CommitInput(model.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, sc), Assignment(SubtitleSlot.Tc, tc)]).Plan!).Succeeded);
        var revision = model.DraftRevision;
        Assert.True(model.CommitInput(model.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, tc), Assignment(SubtitleSlot.Tc, sc)]).Plan!, true).Succeeded);
        Assert.Equal(revision + 1, model.DraftRevision);
        Assert.Equal(tc, model.ScSubtitle);
        Assert.Equal(sc, model.TcSubtitle);
    }

    [Fact]
    public async Task Importing_one_batch_increments_revision_once()
    {
        var video = Write("v.mkv", "x");
        var subtitle = Write("影片.简体.ass", ValidAss);
        var fonts = NewDirectory("fonts");
        var prober = new FakeProber
        {
            Handler = path => path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)
                ? Probe(Video())
                : Probe(Subtitle("ASS", "S_TEXT/ASS"))
        };
        var model = Create(prober, withInputs: false);
        var result = await model.PrepareMediaInputAsync([
            new(video, InputCandidateKind.LocalFile), new(subtitle, InputCandidateKind.LocalFile), new(fonts, InputCandidateKind.LocalDirectory)]);
        Assert.True(result.Succeeded);
        var revision = model.DraftRevision;
        Assert.True(model.CommitMediaInput(result.Plan!).Succeeded);
        // 视频、字体目录与一条外加字幕同属一个批次，只增一次修订。
        Assert.Equal(revision + 1, model.DraftRevision);
    }

    // ---------- 编辑锁：运行中 / 取消中 / 关闭中 ----------

    [Fact]
    public async Task Prepare_commit_and_remove_return_locked_while_running_without_touching_revision()
    {
        var model = Create();
        await LockedAsync(model);
        try
        {
            AssertLocked(model, () => model.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, Write("late.ass", ValidAss))]));
            AssertLocked(model, () => model.PrepareVideoInput([new(Write("late.mkv", "x"), InputCandidateKind.LocalFile)]));
            AssertLocked(model, () => model.PrepareFontsDirectory([new(NewDirectory("late-fonts"), InputCandidateKind.LocalDirectory)]));
            AssertLocked(model, () => model.RemoveSubtitle(SubtitleSlot.Sc));
        }
        finally
        {
            runner.Release();
            Wait(model.CancelAndWaitAsync());
        }
    }

    [Fact]
    public async Task Prepare_returns_locked_while_cancelling_cleanup_waits()
    {
        var model = Create();
        await LockedAsync(model);
        model.CancelCommand.Execute(null);
        Assert.True(model.IsRunning && model.IsCancelling);
        var before = Snapshot(model);
        try
        {
            AssertLocked(model, () => model.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, Write("late.ass", ValidAss))]));
            AssertLocked(model, () => model.PrepareFontsDirectory([new(NewDirectory("late-fonts"), InputCandidateKind.LocalDirectory)]));
            AssertLocked(model, () => model.PrepareOutputDirectory([new(NewDirectory("late-out"), InputCandidateKind.LocalDirectory)]));
            Assert.Equal(before, Snapshot(model));
        }
        finally
        {
            runner.Release();
            Wait(model.CancelAndWaitAsync());
        }
    }

    [Fact]
    public async Task Prepare_and_commit_return_locked_while_closing_and_revision_is_unchanged()
    {
        var model = Create();
        _ = model.CancelAndWaitAsync();
        Assert.True(model.IsClosing);
        var before = Snapshot(model);
        AssertLocked(model, () => model.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, Write("late.ass", ValidAss))]));
        AssertLocked(model, () => model.PrepareOutputDirectory([new(NewDirectory("late-out"), InputCandidateKind.LocalDirectory)]));
        AssertLocked(model, () => model.CommitInput(model.PrepareVideoInput([new(Write("late.mkv", "x"), InputCandidateKind.LocalFile)]).Plan!));
        Assert.Equal(before, Snapshot(model));
        await Task.CompletedTask;
    }

    // ---------- 计划过期与替换确认 ----------

    [Fact]
    public void Committing_a_plan_after_the_draft_changed_returns_stale()
    {
        var model = Create();
        var video = Write("a.mkv", "x");
        var plan = model.PrepareVideoInput([new(video, InputCandidateKind.LocalFile)]).Plan!;
        model.OutputFileName = "changed.mkv";
        var before = Snapshot(model);
        var result = model.CommitInput(plan, true);
        Assert.Equal(InputAcceptanceError.Stale, result.Error);
        Assert.Equal("候选确认期间草稿已变化，请重新分配和确认。", result.Message);
        Assert.Equal(before, Snapshot(model));
    }

    [Fact]
    public void Committing_an_unconfirmed_video_replacement_returns_confirmation_required()
    {
        var model = Create(withInputs: false);
        var first = Write("a.mkv", "x");
        var second = Write("b.mkv", "x");
        Assert.True(Commit(model, model.PrepareVideoInput([new(first, InputCandidateKind.LocalFile)])));
        var plan = model.PrepareVideoInput([new(second, InputCandidateKind.LocalFile)]).Plan!;
        Assert.True(plan.RequiresReplacementConfirmation);
        var before = Snapshot(model);
        var result = model.CommitInput(plan);
        Assert.Equal(InputAcceptanceError.ConfirmationRequired, result.Error);
        Assert.Equal("替换已有视频或字幕需要明确确认。", result.Message);
        Assert.Equal(first, model.VideoPath);
        Assert.Equal(before, Snapshot(model));
        Assert.True(model.CommitInput(plan, true).Succeeded);
        Assert.Equal(second, model.VideoPath);
    }

    // ---------- 运行请求：8 种草稿状态的 MuxJobSpec ----------

    [Fact]
    public async Task Run_request_is_frozen_for_sc_only_draft()
    {
        var model = Create();
        var sc = Write("简体.ass", ValidAss);
        Assert.True(Commit(model, model.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, sc)])));
        var spec = await RunAndCaptureAsync(model);
        AssertSpec(spec, VideoPath, [new ExternalTrackSpec(sc, TrackKind.Subtitle, null, "SC", new("zh-Hans"), new(true, false))], [], FontsDirectory, new(OutputDirectory, null));
    }

    [Fact]
    public async Task Run_request_is_frozen_for_tc_only_draft()
    {
        var model = Create();
        var tc = Write("繁体.ass", ValidAss);
        Assert.True(Commit(model, model.PrepareSubtitleInputs([Assignment(SubtitleSlot.Tc, tc)])));
        var spec = await RunAndCaptureAsync(model);
        AssertSpec(spec, VideoPath, [new ExternalTrackSpec(tc, TrackKind.Subtitle, null, "TC", new("zh-Hant"), new(true, false))], [], FontsDirectory, new(OutputDirectory, null));
    }

    [Fact]
    public async Task Run_request_is_frozen_for_sc_and_tc_draft()
    {
        var model = Create();
        var sc = Write("简体.ass", ValidAss);
        var tc = Write("繁体.ass", ValidAss);
        Assert.True(Commit(model, model.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, sc), Assignment(SubtitleSlot.Tc, tc)])));
        var spec = await RunAndCaptureAsync(model);
        AssertSpec(spec, VideoPath, [
            new ExternalTrackSpec(sc, TrackKind.Subtitle, null, "SC", new("zh-Hans"), new(true, false)),
            new ExternalTrackSpec(tc, TrackKind.Subtitle, null, "TC", new("zh-Hant"), new(false, false))], [], FontsDirectory, new(OutputDirectory, null));
    }

    [Fact]
    public async Task Run_request_is_frozen_for_draft_with_excluded_tc()
    {
        var model = Create();
        var sc = Write("简体.ass", ValidAss);
        var tc = Write("繁体.ass", ValidAss);
        Assert.True(Commit(model, model.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, sc), Assignment(SubtitleSlot.Tc, tc)])));
        Assert.True(model.SetSubtitleIncluded(SubtitleSlot.Tc, false).Succeeded);
        var spec = await RunAndCaptureAsync(model);
        AssertSpec(spec, VideoPath, [new ExternalTrackSpec(sc, TrackKind.Subtitle, null, "SC", new("zh-Hans"), new(true, false))], [], FontsDirectory, new(OutputDirectory, null));
    }

    [Fact]
    public async Task Run_request_is_frozen_for_draft_with_external_audio()
    {
        var model = Create();
        var audio = Write("外加音轨.wav", "x");
        Assert.True(await Import(model, audio, "audio"));
        var spec = await RunAndCaptureAsync(model);
        AssertSpec(spec, VideoPath, [new ExternalTrackSpec(audio, TrackKind.Audio, new(2), "", new("und"), new(false, false))], [], FontsDirectory, new(OutputDirectory, null));
    }

    [Fact]
    public async Task Run_request_is_frozen_when_container_holds_multiple_audio_tracks()
    {
        var model = Create();
        var audio = Write("多音轨.wav", "x");
        // 同一容器的多条音频轨按文件去重，只保留首个可导入轨；其余候选不落位。
        Assert.True(await Import(model, audio, "audio", firstAudioId: 2, audioCount: 2));
        Assert.Single(model.ExternalTracks);
        var spec = await RunAndCaptureAsync(model);
        AssertSpec(spec, VideoPath, [new ExternalTrackSpec(audio, TrackKind.Audio, new(2), "", new("und"), new(false, false))], [], FontsDirectory, new(OutputDirectory, null));
    }

    [Fact]
    public async Task Run_request_is_frozen_for_draft_with_another_subtitle()
    {
        var model = Create();
        var sc = Write("简体.ass", ValidAss);
        var other = Write("其他字幕.ass", ValidAss);
        Assert.True(Commit(model, model.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, sc)])));
        Assert.True(await Import(model, other, "subtitles", firstAudioId: 1, codecId: "S_TEXT/ASS"));
        var spec = await RunAndCaptureAsync(model);
        var rows = model.ExternalTracks.Where(row => row.IsSubtitle && row.Id is not ("SC" or "TC")).ToArray();
        Assert.Equal([new ExternalTrackSpec(other, TrackKind.Subtitle, new(1), "", new("und"), new(false, false))],
            rows.Select(row => new ExternalTrackSpec(row.InputPath, TrackKind.Subtitle, new(row.OriginTrackId), row.Name, new(row.Language), new(row.IsDefault, row.IsForced))));
        AssertSpec(spec, VideoPath, [
            new ExternalTrackSpec(sc, TrackKind.Subtitle, null, "SC", new("zh-Hans"), new(true, false)),
            new ExternalTrackSpec(other, TrackKind.Subtitle, null, "", new("und"), new(false, false))], [], FontsDirectory, new(OutputDirectory, null));
    }

    [Fact]
    public async Task Run_request_is_frozen_for_draft_with_custom_output_file_name()
    {
        var model = Create();
        var sc = Write("简体.ass", ValidAss);
        Assert.True(Commit(model, model.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, sc)])));
        model.OutputFileName = "自定义 成品.mkv";
        var spec = await RunAndCaptureAsync(model);
        AssertSpec(spec, VideoPath, [new ExternalTrackSpec(sc, TrackKind.Subtitle, null, "SC", new("zh-Hans"), new(true, false))], [], FontsDirectory, new(OutputDirectory, "自定义 成品.mkv"));
    }

    [Fact]
    public async Task Run_request_is_frozen_for_draft_with_default_output_file_name()
    {
        var model = Create();
        var sc = Write("简体.ass", ValidAss);
        Assert.True(Commit(model, model.PrepareSubtitleInputs([Assignment(SubtitleSlot.Sc, sc)])));
        model.OutputFileName = "";
        var spec = await RunAndCaptureAsync(model);
        AssertSpec(spec, VideoPath, [new ExternalTrackSpec(sc, TrackKind.Subtitle, null, "SC", new("zh-Hans"), new(true, false))], [], FontsDirectory, new(OutputDirectory, null));
    }

    // ---------- 辅助 ----------

    private readonly RecordingRunner runner = new();
    private FakeProber prober = new();
    private string VideoPath => Path.Combine(root, "源 视频.MKV");
    private string FontsDirectory => Path.Combine(root, "字体目录");
    private string OutputDirectory => Path.Combine(root, "输出目录");

    private static void AssertLocked(SingleMuxViewModel model, Func<InputAcceptanceResult> action)
    {
        var before = Snapshot(model);
        var result = action();
        Assert.Equal(InputAcceptanceError.Locked, result.Error);
        Assert.Equal(before, Snapshot(model));
    }

    private static string Snapshot(SingleMuxViewModel model) => string.Join('|',
        model.DraftRevision, model.VideoPath, model.ScSubtitle, model.TcSubtitle, model.FontsDirectory,
        model.OutputDirectory, model.OutputFileName, model.ScTrack.Name, model.ScTrack.IsDefault,
        model.ScTrack.IsForced, model.ScTrack.IsIncluded, model.TcTrack.Name, model.TcTrack.IsDefault,
        model.TcTrack.IsForced, model.TcTrack.IsIncluded, model.ExternalTracks.Count);

    private static void AssertSpec(MuxJobSpec spec, string video, ExternalTrackSpec[] tracks,
        SourceTrackSelection[] sourceTracks, string fonts, OutputSpec output)
    {
        Assert.Equal(video, spec.VideoPath);
        Assert.Equal(tracks, spec.ExternalTracks.ToArray());
        // 旧入口没有可编辑的源轨选择，空数组表示继续沿用旧源轨策略。
        Assert.Equal(sourceTracks, spec.SourceTracks.ToArray());
        Assert.Equal(new FontSpec(fonts), spec.Fonts);
        Assert.Equal(output, spec.Output);
        Assert.Equal(new ToolConfiguration(ToolsDirectory, LogsDirectory, CacheDirectory), spec.Tools);
    }

    private static bool Commit(SingleMuxViewModel model, InputAcceptanceResult prepared, bool confirm = false) =>
        model.CommitInput(prepared.Plan!, confirm).Succeeded;

    private async Task<bool> Import(SingleMuxViewModel model, string path, string type, int firstAudioId = 2, int audioCount = 1,
        string language = "", string? codecId = null)
    {
        var tracks = Enumerable.Range(0, audioCount)
            .Select(index => Track(firstAudioId + index, type, type == "audio" ? "AAC" : "ASS",
                codecId ?? (type == "subtitles" ? "S_TEXT/ASS" : null), language))
            .ToArray();
        prober = new FakeProber { Handler = _ => Probe(tracks) };
        var result = await model.PrepareMediaInputAsync([new(path, InputCandidateKind.LocalFile)]);
        Assert.True(result.Succeeded, result.Message);
        var committed = model.CommitMediaInput(result.Plan!);
        Assert.True(committed.Succeeded, committed.Message);
        Assert.Contains(model.ExternalTracks, row => row.InputPath == path);
        return true;
    }

    private async Task<MuxJobSpec> RunAndCaptureAsync(SingleMuxViewModel model)
    {
        Assert.True(model.CanStart, model.InputHint);
        model.StartCommand.Execute(null);
        Assert.True(model.IsRunning);
        await model.RunTask;
        Assert.NotNull(runner.Spec);
        return runner.Spec!;
    }

    private async Task LockedAsync(SingleMuxViewModel model)
    {
        // 运行前先放一条启用的外加字幕，满足 CanStart 的输入条件。
        prober = MediaProber();
        var result = await model.PrepareMediaInputAsync([new(Write("锁定.ass", ValidAss), InputCandidateKind.LocalFile)]);
        Assert.True(result.Succeeded, result.Message);
        Assert.True(model.CommitMediaInput(result.Plan!).Succeeded);
        runner.Blocking = true;
        model.StartCommand.Execute(null);
        Assert.True(model.IsRunning);
        await Task.Yield();
    }

    private static void Wait(Task task)
    {
        if (!task.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("等待任务结束超时。");
    }

    private static string ToolsDirectory => "tools";
    private static string LogsDirectory => "logs";
    private static string CacheDirectory => "cache";

    private static SubtitleAssignment Assignment(SubtitleSlot slot, string path) => new(slot, new(path, InputCandidateKind.LocalFile));

    private static MediaTrackInfo Subtitle(string codec, string? codecId) => Track(1, "subtitles", codec, codecId);
    private static MediaTrackInfo Video() => Track(1, "video", "AVC");
    private static MediaTrackInfo Track(int id, string rawType, string codec = "x", string? codecId = null, string language = "") =>
        new(new TrackId(id), rawType switch
        {
            "video" => TrackKind.Video, "audio" => TrackKind.Audio, "subtitles" => TrackKind.Subtitle, _ => TrackKind.Unknown
        }, rawType, codec, codecId, new LanguageTag(language), null, "", new TrackFlags(false, false), null, null, null, true, true);

    private static MediaProbeResult Probe(params MediaTrackInfo[] tracks) => new("probe", [.. tracks], [], null);

    // 接纳按文件类型探测：ASS 是字幕轨，WAV/MKA 是音频轨，其余按视频处理。
    private static FakeProber MediaProber() => new()
    {
        Handler = path => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ass" or ".srt" => Probe(Subtitle("ASS", "S_TEXT/ASS")),
            ".wav" or ".mka" or ".m4a" or ".mp3" => Probe(Track(2, "audio", "AAC")),
            _ => Probe(Video())
        }
    };

    private SingleMuxViewModel Create(FakeProber? customProber = null, bool withInputs = true)
    {
        prober = customProber ?? MediaProber();
        var model = new SingleMuxViewModel(new SingleMuxService(
            new ToolConfiguration(ToolsDirectory, LogsDirectory, CacheDirectory),
            prober, _ => runner), new TrackColumnLayout());
        if (withInputs)
        {
            model.VideoPath = VideoPath;
            model.FontsDirectory = FontsDirectory;
            model.OutputDirectory = OutputDirectory;
        }
        return model;
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private string NewDirectory(string name)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeProber : IProbeMedia
    {
        public Func<string, MediaProbeResult> Handler { get; set; } = _ => Probe();
        public Task<MediaProbeResult> ProbeAsync(string path, ToolConfiguration tools, CancellationToken cancellationToken = default) =>
            Task.FromResult(Handler(path));
    }

    private sealed class RecordingRunner : IRunSingleMux
    {
        private readonly TaskCompletionSource runGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public MuxJobSpec? Spec { get; private set; }
        public bool Blocking { get; set; }
        public void Release() => runGate.TrySetResult();
        public async Task<MuxJobResult> RunAsync(MuxJobSpec spec, IProgress<JobProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Spec = spec;
            if (Blocking) await runGate.Task.ConfigureAwait(false);
            return new MuxJobResult(MuxJobStatus.Succeeded, [], [], [], null, null);
        }
    }
}
