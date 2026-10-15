using MuxDeck.Application.Ports;
using MuxDeck.Application.Ports.Failures;
using MuxDeck.Contracts;
using MuxDeck.Domain;

namespace MuxDeck.Application;

/// <summary>
/// 接管拖入输入的识别与校验：批量路径事实校验、逐个只读探测、按扩展名判类型、字幕读取与校验、
/// 语言推断、与已有轨道的冲突检查。任务锁定、草稿版本与取消等待仍由调用方负责；
/// 失败以错误码加出错路径返回，界面按键映射文案。
/// </summary>
public sealed class PrepareImportUseCase(IProbeMedia prober, IMuxInputReader reader, IPathFacts paths) : IPrepareImport
{
    public async Task<ImportResult> PrepareAsync(ImportRequest request, ToolConfiguration tools, CancellationToken cancellationToken = default)
    {
        try
        {
            var facts = await paths.CollectAsync(request.Inputs, cancellationToken).ConfigureAwait(false);
            if (InputRules.CheckMediaBatch(facts) is { Succeeded: false } batch)
                return Reject(MediaBatchCodes.For(batch.Error)!, batch.Path);
            string? video = null, fonts = null;
            var tracks = new List<ImportedTrackSpec>();
            foreach (var item in facts)
            {
                if (InputRules.UsesFontDirectory(item.Kind, item.Extension))
                {
                    fonts = item.Directory;
                    continue;
                }
                var probe = await prober.ProbeAsync(item.FullPath, tools, cancellationToken).ConfigureAwait(false);
                if (InputRules.IsSubtitleExtension(item.Extension))
                {
                    var text = await ReadSubtitleAsync(item.FullPath, cancellationToken).ConfigureAwait(false);
                    if (!InputRules.IsAssExtension(item.Extension) && !SubtitleRules.HasSrtTimeline(text))
                        return Reject(ErrorCodes.ImportSubtitleTimeline, item.FullPath);
                    if (InputRules.IsAssExtension(item.Extension))
                    {
                        try { _ = AssDocument.ReadUsage(text); }
                        catch (MediaRuleException ex)
                        {
                            // Domain 保留旧 AssGlyphValidator.ReadUsage 的原始文案；界面只补旧入口前缀。
                            return new(ImportStatus.Rejected, null, new(ErrorCodes.ImportAssUsage, ex.Message, item.FullPath));
                        }
                    }
                    var subtitle = probe.Tracks.Where(track => track.Kind == TrackKind.Subtitle).ToArray();
                    if (!InputRules.AcceptsSubtitleProbe(subtitle.Length, subtitle.Length == 1 ? subtitle[0].CodecId : null,
                        subtitle.Length == 1 ? subtitle[0].Codec : "", InputRules.IsAssExtension(item.Extension)))
                        return Reject(ErrorCodes.ImportSubtitleMismatch, item.FullPath);
                    tracks.Add(new(item.FullPath, subtitle[0], Language(item.FullPath, subtitle[0])));
                }
                else if (InputRules.IsSourceVideo(probe.Tracks.Any(track => track.Kind == TrackKind.Video), InputRules.IsVideoExtension(item.Extension)))
                {
                    if (!InputRules.AcceptsSourceVideoCount(video is null ? 1 : 2))
                        return Reject(ErrorCodes.ImportSourceVideoCount, null);
                    video = item.FullPath;
                }
                else
                {
                    var audios = probe.Tracks.Where(track => track.Kind == TrackKind.Audio).ToArray();
                    if (!InputRules.AcceptsAudioProbe(audios.Length, InputRules.IsAudioExtension(item.Extension), probe.Tracks.Any(track => track.Kind == TrackKind.Video)))
                        return Reject(ErrorCodes.ImportAudioMissing, item.FullPath);
                    tracks.AddRange(audios.Select(track => new ImportedTrackSpec(item.FullPath, track, Language(item.FullPath, track))));
                }
            }
            if (tracks.Any(track => Conflicts(request.CurrentSpec, track)))
                return Reject(ErrorCodes.ImportTrackConflict, null);
            var plan = new ImportPlan(request.DraftSessionId, request.DraftRevision, request.Inputs, video,
                fonts is null ? null : new FontSpec(fonts), [.. tracks], RequiresReplacement(request.CurrentSpec, video));
            return new(ImportStatus.Prepared, plan, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (ToolFailureException ex) { return new(ImportStatus.Rejected, null, new(ex.Code, ex.Message, ex.Detail)); }
        catch (Exception ex) { return new(ImportStatus.Rejected, null, new(ErrorCodes.UnexpectedFailure, ex.Message, ex.ToString())); }
    }

    // 字幕原文只经统一输入端口读取；SRT 时间轴规则由 Domain 提供，与单任务链路共用。
    private async Task<string> ReadSubtitleAsync(string path, CancellationToken cancellationToken)
    {
        var facts = await reader.ReadTrackAsync(new ExternalTrackSpec(path, TrackKind.Subtitle, null, "",
            default, new TrackFlags(false, false)), cancellationToken).ConfigureAwait(false);
        return facts.SubtitleText ?? "";
    }

    private static LanguageTag Language(string path, MediaTrackInfo track) =>
        new(LanguageRules.InferImported(Path.GetFileNameWithoutExtension(path), track.Language.Value, track.LanguageIetf?.Value));

    private static bool Conflicts(MuxJobSpec? current, ImportedTrackSpec candidate) =>
        current is not null && current.ExternalTracks.Any(track => InputRules.ImportedTrackConflicts(
            SamePath(track.Path, candidate.Path), track.Kind == TrackKind.Subtitle,
            track.SourceTrackId?.Value ?? -1, candidate.Track.Id.Value));

    private static bool RequiresReplacement(MuxJobSpec? current, string? video) =>
        video is not null && InputRules.RequiresReplacement(current?.VideoPath ?? "", SamePath(current?.VideoPath, video));

    private static bool SamePath(string? left, string? right) =>
        left is not null && right is not null && InputRules.SameNormalizedPath(left, right, OperatingSystem.IsWindows());

    private static ImportResult Reject(string code, string? path) => new(ImportStatus.Rejected, null, new(code, "", path));
}
