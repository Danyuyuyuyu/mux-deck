using MuxDeck.Application.Ports;
using MuxDeck.Application.Ports.Failures;
using MuxDeck.Contracts;
using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Application.Tests;

public sealed class RunSingleMuxUseCaseTests
{
    public static TheoryData<JobStage> Stages => new()
    {
        JobStage.Validating, JobStage.Probing, JobStage.Subsetting, JobStage.Planning,
        JobStage.Muxing, JobStage.Checking, JobStage.Publishing
    };

    [Fact]
    public async Task All_seven_stages_publish_structured_checks_and_await_cleanup()
    {
        var ports = new FakePorts();
        var phases = new List<JobStage>();
        var result = await ports.UseCase.RunAsync(ports.Spec, new ProgressSink(p =>
        { if (p.StepCode == "PHASE") phases.Add(p.Stage); }), TestContext.Current.CancellationToken);
        Assert.Equal(MuxJobStatus.Succeeded, result.Status);
        Assert.Equal(new[] { JobStage.Validating, JobStage.Probing, JobStage.Subsetting, JobStage.Planning,
            JobStage.Muxing, JobStage.Checking, JobStage.Publishing, JobStage.Completed }, phases);
        Assert.Equal(new[] { MuxCheckCodes.Tracks, MuxCheckCodes.Tracks, MuxCheckCodes.Tracks,
            MuxCheckCodes.Attachments, MuxCheckCodes.Readable }, result.Checks.Select(c => c.Code));
        Assert.All(result.Checks, c => Assert.Equal(CheckStatus.Passed, c.Status));
        Assert.Equal("out/final.mkv", Assert.Single(result.Outputs).Path);
        Assert.Null(result.Failure);
        Assert.True(ports.Disposed);
        Assert.Empty(ports.ResidualFiles);
    }

    [Theory, MemberData(nameof(Stages))]
    public async Task Failure_reports_actual_stage_and_retains_port_code(JobStage stage)
    {
        var ports = new FakePorts { FailureStage = stage };
        var result = await ports.UseCase.RunAsync(ports.Spec, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(MuxJobStatus.Failed, result.Status);
        Assert.Equal(stage, result.Failure!.Stage);
        Assert.Equal(stage == JobStage.Planning ? ErrorCodes.PlanInvalid : ErrorCodes.ToolExitFailed, result.Failure.Code);
        Assert.Empty(result.Outputs);
        Assert.True(ports.Disposed);
        Assert.Empty(ports.ResidualFiles);
    }

    [Theory, MemberData(nameof(Stages))]
    public async Task Cancellation_returns_cancelled_and_awaits_cleanup(JobStage stage)
    {
        var ports = new FakePorts();
        using var cancel = new CancellationTokenSource();
        var result = await ports.UseCase.RunAsync(ports.Spec, new ProgressSink(p =>
        { if (p.Stage == stage) cancel.Cancel(); }), cancel.Token);
        Assert.Equal(MuxJobStatus.Cancelled, result.Status);
        Assert.Equal(stage, result.Failure!.Stage);
        Assert.Equal(ErrorCodes.Cancelled, result.Failure.Code);
        Assert.True(ports.Disposed);
        Assert.Empty(ports.ResidualFiles);
        Assert.Empty(result.Outputs);
    }

    [Theory]
    [InlineData(JobStage.Validating)]
    [InlineData(JobStage.Probing)]
    [InlineData(JobStage.Subsetting)]
    [InlineData(JobStage.Muxing)]
    [InlineData(JobStage.Checking)]
    [InlineData(JobStage.Publishing)]
    public async Task Unexpected_port_exceptions_also_release_workspace(JobStage stage)
    {
        var ports = new FakePorts { FailureStage = stage, Unexpected = true };
        var result = await ports.UseCase.RunAsync(ports.Spec, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(MuxJobStatus.Failed, result.Status);
        Assert.Equal(stage, result.Failure!.Stage);
        Assert.Equal(ErrorCodes.UnexpectedFailure, result.Failure.Code);
        Assert.True(ports.Disposed);
        Assert.Empty(ports.ResidualFiles);
    }

    [Theory]
    [InlineData(false, JobStage.Validating)]
    [InlineData(true, JobStage.Publishing)]
    public async Task Existing_or_racing_target_is_never_overwritten(bool race, JobStage stage)
    {
        var ports = new FakePorts { ExistingTarget = !race, RacingTarget = race };
        var result = await ports.UseCase.RunAsync(ports.Spec, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(MuxJobStatus.Failed, result.Status);
        Assert.Equal(ErrorCodes.TargetAlreadyExists, result.Failure!.Code);
        Assert.Equal(stage, result.Failure.Stage);
        Assert.Equal("original", ports.TargetContents);
        Assert.Empty(result.Outputs);
        Assert.True(ports.Disposed);
        Assert.Empty(ports.ResidualFiles);
    }

    [Fact]
    public async Task Cancellation_after_commit_does_not_change_success()
    {
        var ports = new FakePorts();
        using var cancel = new CancellationTokenSource();
        ports.AfterCommit = cancel.Cancel;
        var result = await ports.UseCase.RunAsync(ports.Spec, cancellationToken: cancel.Token);
        Assert.True(cancel.IsCancellationRequested);
        Assert.Equal(MuxJobStatus.Succeeded, result.Status);
        Assert.Single(result.Outputs);
        Assert.True(ports.Disposed);
    }

    [Fact]
    public async Task Replacing_configuration_mid_run_only_changes_next_task()
    {
        var ports = new FakePorts();
        var nextSpec = ports.Spec;
        var first = await ports.UseCase.RunAsync(nextSpec, new ProgressSink(p =>
        {
            if (p.Stage == JobStage.Probing)
                nextSpec = nextSpec with { Tools = new("new-tools", "new-logs", "new-cache"),
                    Output = new("next-out", "next.mkv") };
        }), TestContext.Current.CancellationToken);
        Assert.Equal("out/final.mkv", Assert.Single(first.Outputs).Path);
        Assert.All(ports.ObservedTools, tools => Assert.Equal(ports.Spec.Tools, tools));
        ports.ObservedTools.Clear();
        var next = await ports.UseCase.RunAsync(nextSpec, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("next-out/next.mkv", Assert.Single(next.Outputs).Path.Replace('\\', '/'));
        Assert.All(ports.ObservedTools, tools => Assert.Equal(nextSpec.Tools, tools));
        Assert.True(ports.Disposed);
    }

    [Fact]
    public async Task Domain_track_failure_has_checking_stage_and_central_code()
    {
        var ports = new FakePorts { WrongOutputLanguage = true };
        var result = await ports.UseCase.RunAsync(ports.Spec, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(JobStage.Checking, result.Failure!.Stage);
        Assert.Equal(ErrorCodes.TrackQcFailed, result.Failure.Code);
        Assert.Equal(TrackQcFailureKind.SubtitleMetadata, System.Text.Json.JsonSerializer.Deserialize<TrackQcFailure>(result.Failure.Detail!)!.Kind);
        Assert.True(ports.Disposed);
    }

    [Fact]
    public async Task Input_rule_failure_has_validating_stage_and_central_code()
    {
        var ports = new FakePorts();
        var result = await ports.UseCase.RunAsync(ports.Spec with { ExternalTracks = [] }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(JobStage.Validating, result.Failure!.Stage);
        Assert.Equal(ErrorCodes.InputEmpty, result.Failure.Code);
        Assert.True(ports.Disposed);
    }

    private sealed class ProgressSink(Action<JobProgress> action) : IProgress<JobProgress>
    { public void Report(JobProgress value) => action(value); }

    // 所有边界都是内存事实；不读写文件，测试仅验证公开用例的结果与资源归属。
    private sealed class FakePorts : IMuxInputReader, IToolReadiness, IMediaProber, IFontSubsetter,
        IMuxer, IMediaExtractor, IMediaReferenceBuilder, IMuxEvidenceReader, IArtifactPublisher,
        ITaskWorkspaceFactory, ITaskWorkspace
    {
        public MuxJobSpec Spec { get; } = new("video.mkv",
            [new("sub.srt", TrackKind.Subtitle, null, "SC", new("zh-Hans"), new(true, false))],
            [], new("fonts"), new("out", "final.mkv"), new("tools", "logs", "cache"));
        public RunSingleMuxUseCase UseCase => new(this, this, this, this, this, this, this, this, this, this);
        public JobStage? FailureStage { get; init; }
        public bool Unexpected { get; init; }
        public bool ExistingTarget { get; init; }
        public bool RacingTarget { get; init; }
        public bool WrongOutputLanguage { get; init; }
        public bool Disposed { get; private set; }
        public List<string> ResidualFiles { get; } = [];
        public List<ToolConfiguration> ObservedTools { get; } = [];
        public string TargetContents { get; private set; } = "original";
        public Action? AfterCommit { get; set; }
        public string WorkDirectory => "work";
        public LogRef Log => new("logs/task.log");
        private void Enter(JobStage stage, ToolCallContext? context = null)
        {
            if (context is not null) ObservedTools.Add(context.Tools);
            if (FailureStage != stage || stage == JobStage.Planning) return;
            if (Unexpected) throw new InvalidOperationException("unexpected");
            throw new ToolFailureException(ErrorCodes.ToolExitFailed, "port failure", "original detail");
        }
        public Task<ITaskWorkspace> CreateAsync(ToolConfiguration tools, CancellationToken cancellationToken = default)
        { Disposed = false; ResidualFiles.Add("work"); ObservedTools.Add(tools); return Task.FromResult<ITaskWorkspace>(this); }
        public async ValueTask DisposeAsync() { await Task.Yield(); ResidualFiles.Clear(); Disposed = true; }
        public Task WriteLogAsync(string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<MuxVideoFacts> ReadVideoAsync(string path, CancellationToken cancellationToken = default)
        { Enter(JobStage.Validating); return Task.FromResult(new MuxVideoFacts(path, true)); }
        public Task<MuxFontDirectoryFacts> ReadFontDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MuxFontDirectoryFacts(path, true));
        public Task<MuxOutputFacts> ReadOutputAsync(OutputSpec output, string videoPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MuxOutputFacts(output, true, false, false));
        public Task<MuxTrackFileFacts> ReadTrackAsync(ExternalTrackSpec track, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MuxTrackFileFacts(track, true, "1\n00:00:00,000 --> 00:00:01,000\nHello\n"));
        public Task<bool> DestinationExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(ExistingTarget);
        public Task EnsureReadyAsync(bool needsFonts, ToolCallContext context, CancellationToken cancellationToken = default)
        { ObservedTools.Add(context.Tools); return Task.CompletedTask; }
        private static MediaTrackInfo Track(int id, TrackKind kind, string codec, string? codecId, string name = "", string lang = "und") =>
            new(new(id), kind, kind == TrackKind.Video ? "video" : "subtitles", codec, codecId, new(lang), new LanguageTag(lang),
                name, new(kind == TrackKind.Subtitle, false), null, null, null, true, true);
        public Task<MediaProbeResult> ProbeAsync(string path, ToolCallContext context, CancellationToken cancellationToken = default)
        {
            var output = path.EndsWith("result.mkv", StringComparison.Ordinal);
            Enter(output ? JobStage.Checking : JobStage.Probing, context);
            EquatableArray<MediaTrackInfo> tracks = output
                ? [Track(0, TrackKind.Video, "AVC", "V_MPEG4"), Track(1, TrackKind.Subtitle, "SubRip", "S_TEXT/UTF8", "SC", WrongOutputLanguage ? "en" : "zh-Hans")]
                : [Track(0, TrackKind.Video, "AVC", "V_MPEG4")];
            return Task.FromResult(new MediaProbeResult(path, tracks, [], null));
        }
        public Task<FontSubsetResult> SubsetAsync(FontSubsetRequest request, ToolCallContext context,
            IProgress<JobProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FontSubsetResult(new("work/sub.ass"), [], []));
        public Task<WorkFile> CopySubtitleAsync(ExternalTrackSpec track, int index, ToolCallContext context, CancellationToken cancellationToken = default)
        { Enter(JobStage.Subsetting, context); return Task.FromResult(new WorkFile(FailureStage == JobStage.Planning ? "" : "work/sub.srt")); }
        public Task<MediaMuxResult> MuxAsync(MediaMuxRequest request, ToolCallContext context,
            IProgress<JobProgress>? progress = null, CancellationToken cancellationToken = default)
        { Enter(JobStage.Muxing, context); return Task.FromResult(new MediaMuxResult(request.TemporaryOutput, [])); }
        public Task ExtractTracksAsync(string sourcePath, EquatableArray<TrackExtraction> tracks, ToolCallContext context,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ExtractAttachmentsAsync(string sourcePath, EquatableArray<AttachmentExtraction> attachments, ToolCallContext context,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<WorkFile> BuildSubtitleReferenceAsync(WorkFile subtitle, int index, ToolCallContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkFile("work/reference.mks"));
        public Task<WorkFile> BuildAudioReferenceAsync(ExternalTrackSpec audio, int index, ToolCallContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkFile("work/reference.mka"));
        public Task<string> PrepareExtractionAsync(ToolCallContext context, CancellationToken cancellationToken = default) => Task.FromResult("work/extracted");
        public Task<MuxAttachmentEvidence> ReadAttachmentAsync(WorkFile file, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MuxAttachmentEvidence(10, "hash"));
        public Task<MuxSubtitleEvidence> ReadSubtitleAsync(WorkFile file, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MuxSubtitleEvidence(true, 10, "Hello\n"));
        public Task<string> ReadAudioPayloadAsync(WorkFile file, CancellationToken cancellationToken = default) => Task.FromResult("hash");
        public Task<OutputArtifact> PublishNoOverwriteAsync(WorkFile prepared, string destinationPath, CancellationToken cancellationToken = default)
        {
            Enter(JobStage.Publishing);
            if (RacingTarget) throw new ToolFailureException(ErrorCodes.TargetAlreadyExists, "conflict");
            TargetContents = "published";
            AfterCommit?.Invoke();
            return Task.FromResult(new OutputArtifact(destinationPath.Replace('\\', '/')));
        }
    }
}
