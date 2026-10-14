using System.Text.Json;
using System.Text.RegularExpressions;
using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Application.Ports;
using MuxDeck.Application.Ports.Failures;

namespace MuxDeck.Application;

// 每个阶段仅消费不可变快照；实例字段只有端口，不保存运行配置或任务状态。
public sealed partial class RunSingleMuxUseCase(
    IMuxInputReader inputs, IToolReadiness readiness, IMediaProber prober, IFontSubsetter subsetter,
    IMuxer muxer, IMediaExtractor extractor, IMediaReferenceBuilder references,
    IMuxEvidenceReader evidence, IArtifactPublisher publisher, ITaskWorkspaceFactory workspaces) : IRunSingleMux
{
    private sealed record Validated(MuxJobSpec Spec, string Destination, ToolCallContext Context);
    private sealed record Probed(Validated Input, MediaProbeResult Source, EquatableArray<MediaProbeResult> AudioSources);
    private sealed record Subsetted(Probed Input, EquatableArray<ExternalTrackSpec> Tracks, EquatableArray<WorkFile> Fonts);
    private sealed record Planned(Subsetted Input, MediaMuxRequest Request);
    private sealed record Muxed(Planned Input, WorkFile Artifact);
    private sealed record Checked(Muxed Input);

    public async Task<MuxJobResult> RunAsync(MuxJobSpec spec, IProgress<JobProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stage = JobStage.Validating;
        var checks = new List<CheckResult>();
        var diagnostics = new List<Diagnostic>();
        ITaskWorkspace? workspace = null;
        var committed = false;
        try
        {
            ArgumentNullException.ThrowIfNull(spec);
            // 集合和值类型已经不可变；复制 record 确认本次提交独占配置值。
            spec = spec with { Tools = spec.Tools with { }, Output = spec.Output with { }, Fonts = spec.Fonts with { } };
            workspace = await workspaces.CreateAsync(spec.Tools, cancellationToken).ConfigureAwait(false);
            var context = new ToolCallContext(spec.Tools, workspace) { DiagnosticProgress = new InlineProgress<Diagnostic>(MergeDiagnostic) };
            Phase(JobStage.Validating);
            var validated = await ValidateAsync(spec, context, cancellationToken).ConfigureAwait(false);
            Phase(JobStage.Probing);
            var probed = await ProbeAsync(validated, cancellationToken).ConfigureAwait(false);
            Phase(JobStage.Subsetting);
            var subsetted = await SubsetAsync(probed, checks, diagnostics, progress, cancellationToken).ConfigureAwait(false);
            Phase(JobStage.Planning);
            var planned = Plan(subsetted);
            Phase(JobStage.Muxing);
            var result = await muxer.MuxAsync(planned.Request, context, cancellationToken: cancellationToken).ConfigureAwait(false);
            var muxed = new Muxed(planned, result.Artifact);
            foreach (var item in result.Diagnostics) MergeDiagnostic(item);
            Phase(JobStage.Checking);
            var @checked = await CheckAsync(muxed, checks, cancellationToken).ConfigureAwait(false);
            Phase(JobStage.Publishing);
            var artifact = await publisher.PublishNoOverwriteAsync(@checked.Input.Artifact, validated.Destination, cancellationToken).ConfigureAwait(false);
            committed = true;
            // 发布端口成功返回就是提交点；之后只报告完成，不再读取取消令牌。
            Report(new(JobStage.Completed, "PHASE", 100, ""));
            return new(MuxJobStatus.Succeeded, [artifact], new(checks), new(diagnostics), null, workspace.Log);
        }
        catch (OperationCanceledException)
        {
            return new(MuxJobStatus.Cancelled, [], new(checks), new(diagnostics),
                new(ErrorCodes.Cancelled, stage, null), workspace?.Log);
        }
        catch (ToolFailureException ex)
        {
            return new(MuxJobStatus.Failed, [], new(checks), new(diagnostics),
                new(ex.Code, stage, ex.Message, ex.Detail), workspace?.Log);
        }
        catch (Exception ex)
        {
            return new(MuxJobStatus.Failed, [], new(checks), new(diagnostics),
                new(ErrorCodes.UnexpectedFailure, stage, ex.Message, ex.ToString()), workspace?.Log);
        }
        finally
        {
            if (workspace is not null)
                try { await workspace.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex)
                {
                    // 清理异常不覆盖原始失败、取消或已提交成品；端口保留自己的原始日志。
                    if (!committed) await LogSafely(workspace, "CLEANUP " + ex).ConfigureAwait(false);
                }
        }

        void Phase(JobStage next)
        {
            stage = next;
            cancellationToken.ThrowIfCancellationRequested();
            Report(new(next, "PHASE", null, ""));
            cancellationToken.ThrowIfCancellationRequested();
        }
        void Report(JobProgress value)
        {
            try { progress?.Report(value); }
            catch (Exception) { }
        }
        void MergeDiagnostic(Diagnostic item)
        {
            var index = diagnostics.FindIndex(existing => existing.Code == item.Code && existing.Message == item.Message);
            if (index < 0) diagnostics.Add(item);
            else diagnostics[index] = diagnostics[index] with { Occurrences = checked(diagnostics[index].Occurrences + item.Occurrences) };
        }
    }

    private async Task<Validated> ValidateAsync(MuxJobSpec spec, ToolCallContext context, CancellationToken token)
    {
        Require(!spec.ExternalTracks.IsEmpty, ErrorCodes.InputEmpty);
        Require(spec.SourceTracks.IsEmpty, ErrorCodes.SourceSelectionUnsupported);
        var video = await inputs.ReadVideoAsync(spec.VideoPath, token).ConfigureAwait(false);
        Require(video.Exists, ErrorCodes.VideoMissing, video.Path);
        var fonts = await inputs.ReadFontDirectoryAsync(spec.Fonts.Directory, token).ConfigureAwait(false);
        Require(!spec.ExternalTracks.Any(IsAss) || fonts.Exists, ErrorCodes.FontsDirectoryMissing, fonts.Path);
        var output = await inputs.ReadOutputAsync(spec.Output, video.Path, token).ConfigureAwait(false);
        Require(!output.SameSourceDirectory, ErrorCodes.OutputDirectoryInvalid);
        Require(output.NameValid, ErrorCodes.OutputNameInvalid);
        Require(!output.OverwritesSource, ErrorCodes.OutputOverwritesSource);
        var tracks = new List<ExternalTrackSpec>();
        foreach (var track in spec.ExternalTracks)
        {
            var facts = await inputs.ReadTrackAsync(track, token).ConfigureAwait(false);
            var normalized = facts.Track;
            if (track.Kind == TrackKind.Subtitle)
            {
                Require(facts.Exists && InputRules.IsSubtitleExtension(Path.GetExtension(normalized.Path)), ErrorCodes.SubtitleInputInvalid, normalized.Path);
                try
                {
                    if (IsAss(normalized)) AssDocument.ReadUsage(facts.SubtitleText!);
                    else Require(SrtTimeline().IsMatch(facts.SubtitleText!), ErrorCodes.SubtitleTextInvalid, new { Path = normalized.Path, Kind = "srt" });
                }
                catch (MediaRuleException ex) { throw new ToolFailureException(ErrorCodes.SubtitleTextInvalid, ex.Message); }
                Require(ValidLanguage(normalized.Language.Value), ErrorCodes.SubtitleLanguageInvalid);
                Require(normalized.Name is not null && !normalized.Name.Contains('\0'), ErrorCodes.SubtitleNameInvalid);
            }
            else
            {
                Require(track.Kind == TrackKind.Audio && facts.Exists &&
                    (InputRules.IsAudioExtension(Path.GetExtension(normalized.Path)) || InputRules.IsVideoExtension(Path.GetExtension(normalized.Path))) &&
                    normalized.SourceTrackId is { Value: >= 0 }, ErrorCodes.AudioInputInvalid);
                Require(ValidLanguage(normalized.Language.Value) && normalized.Name is not null && !normalized.Name.Contains('\0'), ErrorCodes.AudioMetadataInvalid);
            }
            tracks.Add(normalized);
        }
        var snapshot = spec with { VideoPath = video.Path, Fonts = new(fonts.Path), Output = output.Output, ExternalTracks = new(tracks) };
        await LogSafely(context.Workspace, "SPEC " + JsonSerializer.Serialize(snapshot)).ConfigureAwait(false);
        var destination = Path.Combine(snapshot.Output.Directory, snapshot.Output.FileName!);
        Require(!await inputs.DestinationExistsAsync(destination, token).ConfigureAwait(false), ErrorCodes.TargetAlreadyExists, destination);
        await readiness.EnsureReadyAsync(snapshot.ExternalTracks.Any(IsAss), context, token).ConfigureAwait(false);
        return new(snapshot, destination, context);
    }

    private async Task<Probed> ProbeAsync(Validated validated, CancellationToken token)
    {
        var source = await prober.ProbeAsync(validated.Spec.VideoPath, validated.Context, token).ConfigureAwait(false);
        Require(source.Tracks.Any(track => track.Kind == TrackKind.Video), ErrorCodes.SourceVideoMissing);
        var audios = new List<MediaProbeResult>();
        foreach (var audio in validated.Spec.ExternalTracks.Where(track => track.Kind == TrackKind.Audio))
        {
            var probe = await prober.ProbeAsync(audio.Path, validated.Context, token).ConfigureAwait(false);
            Require(probe.Tracks.Any(track => track.Kind == TrackKind.Audio && track.Id == audio.SourceTrackId), ErrorCodes.SourceAudioMissing, audio.Path);
            audios.Add(probe);
        }
        return new(validated, source, new(audios));
    }

    private async Task<Subsetted> SubsetAsync(Probed probed, List<CheckResult> checks, List<Diagnostic> diagnostics,
        IProgress<JobProgress>? progress, CancellationToken token)
    {
        var subtitles = probed.Input.Spec.ExternalTracks.Where(track => track.Kind == TrackKind.Subtitle).ToArray();
        var tracks = new List<ExternalTrackSpec>();
        var fonts = new List<WorkFile>();
        for (var i = 0; i < subtitles.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            if (!IsAss(subtitles[i]))
            {
                var copied = await evidence.CopySubtitleAsync(subtitles[i], i, probed.Input.Context, token).ConfigureAwait(false);
                tracks.Add(subtitles[i] with { Path = copied.Path });
                continue;
            }
            SafeReport(progress, new(JobStage.Subsetting, "SUBSET_ITEM", null, "") { Detail = JsonSerializer.Serialize(new[] { i + 1, subtitles.Length }) });
            token.ThrowIfCancellationRequested();
            var before = diagnostics.ToDictionary(item => (item.Code, item.Message), item => item.Occurrences);
            var result = await subsetter.SubsetAsync(new(subtitles[i], probed.Input.Spec.Fonts), probed.Input.Context with { SubtitleIndex = i },
                cancellationToken: token).ConfigureAwait(false);
            foreach (var item in result.Diagnostics)
            {
                var index = diagnostics.FindIndex(existing => existing.Code == item.Code && existing.Message == item.Message);
                var expected = before.GetValueOrDefault((item.Code, item.Message)) + item.Occurrences;
                if (index < 0) diagnostics.Add(item);
                else if (diagnostics[index].Occurrences < expected) diagnostics[index] = item with { Occurrences = expected };
            }
            checks.Add(new(MuxCheckCodes.SubsetFonts, CheckStatus.Passed, (i + 1).ToString(), null, result.Fonts.Length.ToString()));
            tracks.Add(subtitles[i] with { Path = result.CorrectedSubtitle.Path });
            fonts.AddRange(result.Fonts);
        }
        if (diagnostics.Count > 0)
        {
            SafeReport(progress, new(JobStage.Subsetting, "SUBSET_DIAGNOSTICS", null, "") { Detail = JsonSerializer.Serialize(diagnostics) });
            token.ThrowIfCancellationRequested();
        }
        tracks.AddRange(probed.Input.Spec.ExternalTracks.Where(track => track.Kind == TrackKind.Audio));
        return new(probed, new(tracks), new(fonts));
    }

    private static Planned Plan(Subsetted subsetted)
    {
        Require(subsetted.Tracks.Length == subsetted.Input.Input.Spec.ExternalTracks.Length &&
            subsetted.Tracks.All(track => !string.IsNullOrWhiteSpace(track.Path)) &&
            subsetted.Fonts.All(font => !string.IsNullOrWhiteSpace(font.Path)), ErrorCodes.PlanInvalid);
        return new(subsetted, new(subsetted.Input.Input.Spec.VideoPath, subsetted.Tracks, subsetted.Fonts,
            new(Path.Combine(subsetted.Input.Input.Context.Workspace.WorkDirectory, "result.mkv"))));
    }

    private async Task<Checked> CheckAsync(Muxed muxed, List<CheckResult> checks, CancellationToken token)
    {
        var planned = muxed.Input; var subsetted = planned.Input; var probed = subsetted.Input; var validated = probed.Input;
        var context = validated.Context;
        var output = await prober.ProbeAsync(muxed.Artifact.Path, context, token).ConfigureAwait(false);
        var subtitles = validated.Spec.ExternalTracks.Where(track => track.Kind == TrackKind.Subtitle).ToArray();
        var audios = validated.Spec.ExternalTracks.Where(track => track.Kind == TrackKind.Audio).ToArray();
        var qc = OutputQcRules.CheckFacts(new(probed.Source.Tracks, probed.Source.DurationNanoseconds), new(output.Tracks, output.DurationNanoseconds),
            subtitles.Select(track => new SubtitleQcFacts(IsAss(track), track.Name, track.Language.Value, track.Flags.IsDefault, track.Flags.IsForced)).ToEquatableArray(),
            audios.Select(track => new AudioQcFacts(track.SourceTrackId!.Value.Value, track.Name, track.Language.Value, track.Flags.IsDefault, track.Flags.IsForced)).ToEquatableArray(),
            probed.AudioSources.Select(source => new MediaQcFacts(source.Tracks, source.DurationNanoseconds)).ToEquatableArray());
        checks.AddRange(qc.Checks.Select(check => new CheckResult(MuxCheckCodes.Tracks, CheckStatus.Passed, null, null, JsonSerializer.Serialize(check))));
        Require(qc.Failure is null, ErrorCodes.TrackQcFailed, qc.Failure);
        Require(output.Attachments.Length == subsetted.Fonts.Length, ErrorCodes.AttachmentCountInvalid);
        var identities = new List<MuxAttachmentEvidence>();
        for (var i = 0; i < subsetted.Fonts.Length; i++)
        {
            var font = subsetted.Fonts[i]; var actual = output.Attachments[i];
            var expected = await evidence.ReadAttachmentAsync(font, token).ConfigureAwait(false);
            Require(actual.FileName == AttachmentName(font, i) && actual.Size == expected.Length &&
                actual.ContentType is "font/ttf" or "font/otf", ErrorCodes.AttachmentMetadataInvalid, i + 1);
            identities.Add(expected);
        }
        var extracted = await evidence.PrepareExtractionAsync(context, token).ConfigureAwait(false);
        var subtitleTracks = output.Tracks.Where(track => track.Kind == TrackKind.Subtitle).ToArray();
        await extractor.ExtractTracksAsync(muxed.Artifact.Path, subtitleTracks.Select((track, i) => new TrackExtraction(track.Id,
            new(Path.Combine(extracted, $"subtitle-{i}" + Path.GetExtension(subsetted.Tracks[i].Path))), TrackExtractionFormat.ContainerDefault)).ToEquatableArray(), context, token).ConfigureAwait(false);
        for (var i = 0; i < subtitles.Length; i++)
        {
            var corrected = new WorkFile(subsetted.Tracks[i].Path);
            var reference = await references.BuildSubtitleReferenceAsync(corrected, i, context, token).ConfigureAwait(false);
            var textFile = new WorkFile(Path.Combine(extracted, $"reference-{i}" + Path.GetExtension(corrected.Path)));
            await extractor.ExtractTracksAsync(reference.Path, [new(new(0), textFile, TrackExtractionFormat.ContainerDefault)], context, token).ConfigureAwait(false);
            var before = await evidence.ReadSubtitleAsync(textFile, token).ConfigureAwait(false);
            Require(before.Exists && before.Length > 0, ErrorCodes.SubtitleReferenceInvalid, i + 1);
            var actual = await evidence.ReadSubtitleAsync(new(Path.Combine(extracted, $"subtitle-{i}" + Path.GetExtension(corrected.Path))), token).ConfigureAwait(false);
            Require(OutputQcRules.SubtitleContentEquals(before.Text!, actual.Text!), ErrorCodes.SubtitleTextMismatch, i + 1);
        }
        await extractor.ExtractAttachmentsAsync(muxed.Artifact.Path, subsetted.Fonts.Select((font, i) => new AttachmentExtraction(
            output.Attachments[i].Id, new(Path.Combine(extracted, AttachmentName(font, i))))).ToEquatableArray(), context, token).ConfigureAwait(false);
        for (var i = 0; i < subsetted.Fonts.Length; i++)
        {
            var actual = await evidence.ReadAttachmentAsync(new(Path.Combine(extracted, AttachmentName(subsetted.Fonts[i], i))), token).ConfigureAwait(false);
            Require(identities[i].Sha256 == actual.Sha256, ErrorCodes.AttachmentPayloadMismatch, i + 1);
        }
        checks.Add(new(MuxCheckCodes.Attachments, CheckStatus.Passed, null, null, subsetted.Fonts.Length.ToString()));
        checks.Add(new(MuxCheckCodes.Readable, CheckStatus.Passed, null, null, null));
        for (var i = 0; i < audios.Length; i++)
        {
            var reference = await references.BuildAudioReferenceAsync(audios[i], i, context, token).ConfigureAwait(false);
            var referenceProbe = await prober.ProbeAsync(reference.Path, context, token).ConfigureAwait(false);
            var referenceTrack = referenceProbe.Tracks.Single(track => track.Kind == TrackKind.Audio);
            var actualTrack = output.Tracks.Where(track => track.Kind == TrackKind.Audio).Skip(probed.Source.Tracks.Count(track => track.Kind == TrackKind.Audio)).ElementAt(i);
            var beforeRaw = new WorkFile(Path.Combine(extracted, $"audio-reference-{i}.raw"));
            var afterRaw = new WorkFile(Path.Combine(extracted, $"audio-output-{i}.raw"));
            await extractor.ExtractTracksAsync(reference.Path, [new(referenceTrack.Id, beforeRaw, TrackExtractionFormat.FullRaw)], context, token).ConfigureAwait(false);
            await extractor.ExtractTracksAsync(muxed.Artifact.Path, [new(actualTrack.Id, afterRaw, TrackExtractionFormat.FullRaw)], context, token).ConfigureAwait(false);
            var beforeHash = await evidence.ReadAudioPayloadAsync(beforeRaw, token).ConfigureAwait(false);
            var afterHash = await evidence.ReadAudioPayloadAsync(afterRaw, token).ConfigureAwait(false);
            Require(beforeHash == afterHash, ErrorCodes.AudioPayloadMismatch, i + 1);
            checks.Add(new(MuxCheckCodes.AudioPayload, CheckStatus.Passed, (i + 1).ToString(), beforeHash, afterHash));
        }
        return new(muxed);
    }

    private static bool IsAss(ExternalTrackSpec track) => track.Kind == TrackKind.Subtitle && InputRules.IsAssExtension(Path.GetExtension(track.Path));
    private static string AttachmentName(WorkFile font, int index) => $"{index + 1:D3}-{Path.GetFileName(font.Path)}";
    private static bool ValidLanguage(string value) => !string.IsNullOrWhiteSpace(value) && LanguagePattern().IsMatch(value);
    private static void Require(bool valid, string code, object? detail = null)
    { if (!valid) throw new ToolFailureException(code, "", detail is null ? null : JsonSerializer.Serialize(detail)); }
    private static void SafeReport(IProgress<JobProgress>? progress, JobProgress value) { try { progress?.Report(value); } catch (Exception) { } }
    private static async Task LogSafely(ITaskWorkspace workspace, string text) { try { await workspace.WriteLogAsync(text).ConfigureAwait(false); } catch (ToolFailureException) { } }
    [GeneratedRegex(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$")] private static partial Regex LanguagePattern();
    [GeneratedRegex(@"\d+:\d{2}:\d{2},\d{3}\s*-->\s*\d+:\d{2}:\d{2},\d{3}")] private static partial Regex SrtTimeline();
    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }
}
