using MuxDeck.Application;
using MuxDeck.Application.Drafts;
using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using MuxDeck.Core;
using MuxDeck.Core.Muxing;
using MuxDeck.Domain;
using MuxDeck.Infrastructure;
using Xunit;

namespace MuxDeck.Architecture.Tests;

// 4b-2：StartAsync 改走 MuxJobSpec 后，“请求 …”日志行必须与 621ee8b 的旧请求路径逐字相同。
public sealed class SingleMuxSpecEntryTests : IDisposable
{
    private const string Video = "C:\\in\\v.mkv";
    private const string Sc = "C:\\in\\sc.ass";
    private const string Tc = "C:\\in\\tc.ass";
    private const string Other = "C:\\in\\other.ass";
    private const string Audio = "C:\\in\\audio.mka";
    private const string Fonts = "C:\\fonts";
    private const string Output = "C:\\out";
    private static readonly ToolConfiguration Tools = new("tools", "logs", "cache");

    private readonly string root = Path.Combine(Path.GetTempPath(), "MuxDeck-4b2-spec-" + Guid.NewGuid().ToString("N"));

    public SingleMuxSpecEntryTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Theory]
    [InlineData("sc-only")]
    [InlineData("tc-only")]
    [InlineData("sc-tc")]
    [InlineData("excluded-tc")]
    [InlineData("external-audio")]
    [InlineData("multi-audio-container")]
    [InlineData("other-subtitle")]
    [InlineData("custom-output")]
    [InlineData("default-output")]
    [InlineData("audio-before-subtitle")]
    public async Task Spec_entry_writes_the_same_request_line_as_the_legacy_entry(string state)
    {
        var (request, spec) = State(state);
        var runner = new RecordingRunner();
        var service = CreateService(runner);

        var legacyLine = await RequestLineAsync(runner, () => service.RunAsync(request, Tools,
            cancellationToken: TestContext.Current.CancellationToken));
        var specLine = await RequestLineAsync(runner, () => service.RunSpecAsync(spec, Tools,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(legacyLine, specLine);
    }

    private (MuxRequest Request, MuxJobSpec Spec) State(string state)
    {
        var draft = new MuxDraft(SystemPathFacts.Instance);
        draft.SetVideoPath(Video);
        draft.SetFontsDirectory(Fonts);
        draft.SetOutputDirectory(Output);
        var subtitles = new List<SubtitleInput>();
        var audios = new List<AudioInput>();
        string? outputName = null;

        void AddSc() { draft.SetSubtitlePath(SubtitleSlot.Sc, Sc); subtitles.Add(new(Sc, "SC", "zh-Hans", true, false)); }
        void AddTc()
        {
            // 旧 ApplySubtitleDefaults：只有 TC 时它才是默认轨；与 SC 同时存在时 SC 默认。
            var isDefault = subtitles.All(item => item.Path != Sc);
            draft.SetSubtitlePath(SubtitleSlot.Tc, Tc);
            subtitles.Add(new(Tc, "TC", "zh-Hant", isDefault, false));
        }
        void AddOtherSubtitle()
        {
            draft.AddExternalTrack(new DraftExternalTrackSnapshot("sub", Other, TrackKind.Subtitle, new TrackId(1),
                "", new LanguageTag("und"), new TrackFlags(false, false)));
            subtitles.Add(new(Other, "", "und", false, false));
        }
        void AddAudio(int id = 2)
        {
            draft.AddExternalTrack(new DraftExternalTrackSnapshot("audio-" + id, Audio, TrackKind.Audio, new TrackId(id),
                "", new LanguageTag("und"), new TrackFlags(false, false)));
            audios.Add(new(Audio, id, "", "und", false, false));
        }

        switch (state)
        {
            case "sc-only": AddSc(); break;
            case "tc-only": AddTc(); break;
            case "sc-tc": AddSc(); AddTc(); break;
            case "excluded-tc":
                AddSc(); AddTc();
                draft.SetSubtitleIncluded(SubtitleSlot.Tc, false);
                subtitles.RemoveAt(1);
                break;
            case "external-audio": AddAudio(); break;
            case "multi-audio-container": AddAudio(2); AddAudio(3); break;
            case "other-subtitle": AddSc(); AddOtherSubtitle(); break;
            case "custom-output":
                AddSc(); outputName = "自定义 成品.mkv"; draft.SetOutputFileName(outputName);
                break;
            case "default-output": AddSc(); draft.SetOutputFileName(""); break;
            case "audio-before-subtitle": AddAudio(); AddSc(); AddOtherSubtitle(); break;
        }

        var request = new MuxRequest(Video, [.. subtitles], Fonts, Output, outputName) { Audios = [.. audios] };
        return (request, draft.ToSpec(Tools));
    }

    private SingleMuxService CreateService(RecordingRunner runner)
    {
        var workspace = new Workspace(root);
        return new SingleMuxService((ToolConfiguration)workspace, new NoProbe(),
            factory => { runner.Factory = factory; return runner; });
    }

    private static async Task<string> RequestLineAsync(RecordingRunner runner, Func<Task<MuxResult>> run)
    {
        await run();
        Assert.NotNull(runner.LogPath);
        var lines = await File.ReadAllLinesAsync(runner.LogPath!, TestContext.Current.CancellationToken);
        var line = Assert.Single(lines, value => value.Contains(" 请求 ", StringComparison.Ordinal));
        return line[line.IndexOf(" 请求 ", StringComparison.Ordinal)..];
    }

    private sealed class RecordingRunner : IRunSingleMux
    {
        public ITaskWorkspaceFactory? Factory { get; set; }
        public string? LogPath { get; private set; }

        public async Task<MuxJobResult> RunAsync(MuxJobSpec spec, IProgress<JobProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var workspace = await Factory!.CreateAsync(spec.Tools, cancellationToken);
            LogPath = workspace.Log.Path;
            progress?.Report(new JobProgress(JobStage.Validating, "INPUT_VALIDATED", null, ""));
            // 服务在 Report 内同步写入请求行；释放后日志保留，便于逐字比较。
            await workspace.DisposeAsync();
            return new MuxJobResult(MuxJobStatus.Succeeded, [], [], [], null, null);
        }
    }

    private sealed class NoProbe : IProbeMedia
    {
        public Task<MediaProbeResult> ProbeAsync(string path, ToolConfiguration tools,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
}
