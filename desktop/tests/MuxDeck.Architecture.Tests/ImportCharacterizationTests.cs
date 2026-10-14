using System.Text;
using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using MuxDeck.Core.Muxing;
using MuxDeck.Desktop.ViewModels;
using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Architecture.Tests;

// 4a-3 特征测试：逐字锁定旧入口 PrepareMediaInputAsync / MediaInputError 的文案与计划字段。
// 迁移期间本文件一行都不能改；迁移后新用例必须产生完全相同的文案。
public sealed class ImportCharacterizationTests : IDisposable
{
    private const string ValidAss =
        "[Script Info]\nTitle: t\nScriptType: v4.00+\n\n" +
        "[V4+ Styles]\n" +
        "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n" +
        "Style: Default,Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,0,2,10,10,10,1\n\n" +
        "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
        "Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,你好\n";
    private const string ValidSrt = "1\n00:00:00,000 --> 00:00:01,000\n你好\n";

    private readonly string root = Path.Combine(Path.GetTempPath(), "MuxDeck-4a3-" + Guid.NewGuid().ToString("N"));

    public ImportCharacterizationTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    // ---------- MediaInputError ----------

    [Fact]
    public void Rejects_empty_selection() =>
        Assert.Equal("拖入本地视频、音频、ASS/SRT 或字体目录。", SingleMuxViewModel.MediaInputError([]));

    [Fact]
    public void Rejects_non_local_input() =>
        Assert.Equal("仅接受系统本地文件或文件夹，URL / 文本路径不受支持。",
            SingleMuxViewModel.MediaInputError([new("http://example.com/a.ass", InputCandidateKind.NonLocal)]));

    [Fact]
    public void Requires_absolute_path() =>
        Assert.Equal("需要完整的本地路径。",
            SingleMuxViewModel.MediaInputError([new("a.ass", InputCandidateKind.LocalFile)]));

    [Fact]
    public void Reports_missing_path()
    {
        var path = Path.Combine(root, "missing.ass");
        Assert.Equal("路径不存在或不可访问：" + path,
            SingleMuxViewModel.MediaInputError([new(path, InputCandidateKind.LocalFile)]));
    }

    [Fact]
    public void Rejects_unsupported_type()
    {
        var path = Write("a.txt", "x");
        Assert.Equal("不支持此文件类型：a.txt；支持视频、音频、ASS/SRT 和字体。",
            SingleMuxViewModel.MediaInputError([new(path, InputCandidateKind.LocalFile)]));
    }

    [Fact]
    public void Rejects_duplicate_paths()
    {
        var path = Write("dup.ass", ValidAss);
        Assert.Equal("同一文件不能重复导入。",
            SingleMuxViewModel.MediaInputError([new(path, InputCandidateKind.LocalFile), new(path, InputCandidateKind.LocalFile)]));
    }

    [Fact]
    public void Rejects_more_than_one_font_directory()
    {
        var first = NewDirectory("fonts-a");
        var second = NewDirectory("fonts-b");
        Assert.Equal("一次只能设置一个字体目录。",
            SingleMuxViewModel.MediaInputError([new(first, InputCandidateKind.LocalDirectory), new(second, InputCandidateKind.LocalDirectory)]));
    }

    // ---------- PrepareMediaInputAsync：锁定 ----------

    [Fact]
    public async Task Prepare_reports_locked_task()
    {
        var model = Create(new FakeProber());
        model.IsComponentUninstalling = true;
        Assert.Equal("当前任务已锁定，不能接纳输入。", (await Prepare(model, Write("v.mkv", "x"))).Message);
    }

    // ---------- 字幕 ----------

    [Fact]
    public async Task Prepare_reports_non_utf8_subtitle_with_original_decoder_message()
    {
        var bytes = new byte[] { 0x31, 0x0A, 0xFF, 0xFE, 0x0A };
        var path = WriteBytes("bad.srt", bytes);
        var prober = new FakeProber { Handler = _ => Probe(Subtitle("ASS", "S_TEXT/ASS")) };
        var result = await Prepare(Create(prober), path);
        // 框架消息随区域设置变化，锁定的是“前缀 + 原始解码消息”这一组合。
        Assert.Equal("无法识别拖入文件，整批未接纳：" + DecoderMessage(bytes), result.Message);
    }

    [Fact]
    public async Task Prepare_reports_empty_srt()
    {
        var path = Write("empty.srt", "");
        var prober = new FakeProber { Handler = _ => Probe(Subtitle("SubRip", "S_TEXT/UTF8")) };
        Assert.Equal("无法识别拖入文件，整批未接纳：SRT 没有有效时间轴：" + path, (await Prepare(Create(prober), path)).Message);
    }

    [Fact]
    public async Task Prepare_reports_srt_without_timeline()
    {
        var path = Write("plain.srt", "1\n没有时间轴\n");
        var prober = new FakeProber { Handler = _ => Probe(Subtitle("SubRip", "S_TEXT/UTF8")) };
        Assert.Equal("无法识别拖入文件，整批未接纳：SRT 没有有效时间轴：" + path, (await Prepare(Create(prober), path)).Message);
    }

    [Fact]
    public async Task Prepare_reports_ass_missing_required_sections()
    {
        var path = Write("broken.ass", "[Script Info]\nTitle: t\n");
        var prober = new FakeProber { Handler = _ => Probe(Subtitle("ASS", "S_TEXT/ASS")) };
        Assert.Equal("无法识别拖入文件，整批未接纳：ASS 缺少必要节：" + path, (await Prepare(Create(prober), path)).Message);
    }

    [Fact]
    public async Task Prepare_reports_probe_mismatch_for_subtitle()
    {
        var path = Write("s.ass", ValidAss);
        var prober = new FakeProber { Handler = _ => Probe(Subtitle("SubRip", "S_TEXT/UTF8")) };
        Assert.Equal("文件内容不是有效的 ASS/SRT：" + path, (await Prepare(Create(prober), path)).Message);
    }

    // ---------- 视频 / 音频 ----------

    [Fact]
    public async Task Prepare_rejects_more_than_one_source_video()
    {
        var prober = new FakeProber { Handler = _ => Probe(Video()) };
        var model = Create(prober);
        var first = Write("a.mkv", "x");
        var second = Write("b.mkv", "x");
        var result = await model.PrepareMediaInputAsync([new(first, InputCandidateKind.LocalFile), new(second, InputCandidateKind.LocalFile)]);
        Assert.Equal("单个封装一次只能接纳一个源视频。", result.Message);
    }

    [Fact]
    public async Task Prepare_rejects_audio_without_importable_track()
    {
        var path = Write("a.mp3", "x");
        var prober = new FakeProber { Handler = _ => Probe(Track(1, "audio", "AAC")) with { Tracks = [] } };
        Assert.Equal("文件没有可导入的音频轨：" + path, (await Prepare(Create(prober), path)).Message);
    }

    // ---------- 冲突 / 草稿变化 / 取消 ----------

    [Fact]
    public async Task Prepare_reports_track_already_added()
    {
        var path = Write("s.ass", ValidAss);
        var prober = new FakeProber { Handler = _ => Probe(Subtitle("ASS", "S_TEXT/ASS")) };
        var model = Create(prober);
        var first = await model.PrepareMediaInputAsync([new(path, InputCandidateKind.LocalFile)]);
        Assert.True(first.Succeeded);
        model.CommitMediaInput(first.Plan!);
        Assert.Equal("文件中的轨道已加入；请在属性栏更换已有文件。",
            (await model.PrepareMediaInputAsync([new(path, InputCandidateKind.LocalFile)])).Message);
    }

    [Fact]
    public async Task Prepare_reports_draft_changed_during_probe()
    {
        var path = Write("s.ass", ValidAss);
        var prober = new FakeProber();
        var model = Create(prober);
        prober.Handler = _ => { model.OutputFileName = "changed"; return Probe(Subtitle("ASS", "S_TEXT/ASS")); };
        Assert.Equal("识别期间任务或草稿已变化，整批输入保持原样。", (await Prepare(model, path)).Message);
    }

    [Fact]
    public async Task Prepare_reports_cancelled_probe()
    {
        var path = Write("s.ass", ValidAss);
        var prober = new FakeProber { Block = true, Handler = _ => Probe(Subtitle("ASS", "S_TEXT/ASS")) };
        var model = Create(prober);
        var task = model.PrepareMediaInputAsync([new(path, InputCandidateKind.LocalFile)]);
        await prober.Entered.Task;
        await model.CancelAndWaitAsync();
        Assert.Equal("识别已取消，原输入保持。", (await task).Message);
    }

    // ---------- 成功 ----------

    [Fact]
    public async Task Prepare_builds_plan_with_video_fonts_tracks_and_language()
    {
        var video = Write("v.mkv", "x");
        var fonts = NewDirectory("fonts");
        var subtitle = Write("影片.简体.ass", ValidAss);
        var prober = new FakeProber
        {
            Handler = path => path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)
                ? Probe(Video(), Track(2, "audio", "AAC"))
                : Probe(Subtitle("ASS", "S_TEXT/ASS"))
        };
        var result = await Create(prober).PrepareMediaInputAsync([
            new(video, InputCandidateKind.LocalFile), new(subtitle, InputCandidateKind.LocalFile), new(fonts, InputCandidateKind.LocalDirectory)]);
        Assert.True(result.Succeeded);
        Assert.Equal("已识别，等待接纳。", result.Message);
        var plan = result.Plan!;
        Assert.Equal(video, plan.Video);
        Assert.Equal(fonts, plan.Fonts);
        var track = Assert.Single(plan.Tracks);
        Assert.Equal(subtitle, track.Path);
        Assert.Equal("zh-Hans", track.Language);
        Assert.Equal("subtitles", track.Media.Type);
    }

    // ---------- 辅助 ----------

    private static string DecoderMessage(byte[] bytes)
    {
        try { _ = new UTF8Encoding(false, true).GetString(bytes); return ""; }
        catch (Exception ex) { return ex.Message; }
    }

    private static MediaTrackInfo Subtitle(string codec, string? codecId) => Track(1, "subtitles", codec, codecId);
    private static MediaTrackInfo Video() => Track(1, "video", "AVC");
    private static MediaTrackInfo Track(int id, string rawType, string codec = "x", string? codecId = null, string language = "") =>
        new(new TrackId(id), rawType switch
        {
            "video" => TrackKind.Video, "audio" => TrackKind.Audio, "subtitles" => TrackKind.Subtitle, _ => TrackKind.Unknown
        }, rawType, codec, codecId, new LanguageTag(language), null, "", new TrackFlags(false, false), null, null, null, true, true);

    private static MediaProbeResult Probe(params MediaTrackInfo[] tracks) => new("probe", [.. tracks], [], null);

    private static Task<MediaImportResult> Prepare(SingleMuxViewModel model, params string[] paths) =>
        model.PrepareMediaInputAsync([.. paths.Select(path => new InputCandidate(path, InputCandidateKind.LocalFile))]);

    private string Write(string name, string content)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private string WriteBytes(string name, byte[] content)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, content);
        return path;
    }

    private string NewDirectory(string name)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private SingleMuxViewModel Create(IProbeMedia prober) =>
        new SingleMuxViewModel(new SingleMuxService(
            new ToolConfiguration(Path.Combine(root, "tools"), Path.Combine(root, "logs"), Path.Combine(root, "cache")),
            prober, _ => throw new NotSupportedException()), new TrackColumnLayout());

    private sealed class FakeProber : IProbeMedia
    {
        public Func<string, MediaProbeResult> Handler { get; set; } = _ => Probe();
        public bool Block { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<MediaProbeResult> ProbeAsync(string path, ToolConfiguration tools, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            if (Block) await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return Handler(path);
        }
    }
}
