using System.Collections.Immutable;
using MuxDeck.Contracts;
using MuxDeck.Domain;

namespace MuxDeck.Core.Muxing;

/// <summary>只转换旧模型；1b 尚不接入旧入口，不能在这里重新判定检查文案或执行工具。</summary>
public static class LegacyMuxContractConverter
{
    public static MuxJobSpec ToSpec(MuxRequest request, Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return ToSpec(request, new ToolConfiguration(workspace.ToolsDirectory, workspace.TaskLogsDirectory,
            workspace.TaskCacheDirectory, workspace.FontToolsDirectoryOverride));
    }

    public static MuxJobSpec ToSpec(MuxRequest request, ToolConfiguration tools)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(request.Subtitles);
        ArgumentNullException.ThrowIfNull(request.Audios);
        var tracks = request.Subtitles.Select(subtitle => new ExternalTrackSpec(subtitle.Path,
                TrackKind.Subtitle, null, subtitle.Name, new LanguageTag(subtitle.Language),
                new TrackFlags(subtitle.IsDefault, subtitle.IsForced)))
            .Concat(request.Audios.Select(audio => new ExternalTrackSpec(audio.Path, TrackKind.Audio,
                new TrackId(audio.TrackId), audio.Name, new LanguageTag(audio.Language),
                new TrackFlags(audio.IsDefault, audio.IsForced)))).ToImmutableArray();
        // 旧模型没有可编辑的源轨选择；空数组表示继续沿用旧入口的源轨策略。
        return new MuxJobSpec(request.VideoPath, tracks, [], new FontSpec(request.FontsDirectory),
            new OutputSpec(request.OutputDirectory, request.OutputFileName), tools);
    }

    public static MuxRequest ToLegacyRequest(MuxJobSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.ExternalTracks.IsDefault || spec.SourceTracks.IsDefault)
            throw new ArgumentException("任务轨道集合必须初始化。", nameof(spec));
        if (!spec.SourceTracks.IsEmpty)
            throw new NotSupportedException("旧请求无法表达源轨选择；不能静默丢弃新规格。");
        var subtitles = new List<SubtitleInput>();
        var audios = new List<AudioInput>();
        var seenAudio = false;
        foreach (var track in spec.ExternalTracks)
        {
            if (seenAudio && track.Kind == TrackKind.Subtitle)
                throw new NotSupportedException("旧请求无法保持字幕和音频的跨类型交错顺序。");
            if (track.Kind == TrackKind.Subtitle && track.SourceTrackId is null)
                subtitles.Add(new SubtitleInput(track.Path, track.Name, track.Language.Value,
                    track.Flags.IsDefault, track.Flags.IsForced));
            else if (track.Kind == TrackKind.Audio && track.SourceTrackId is { } id)
            {
                seenAudio = true;
                audios.Add(new AudioInput(track.Path, id.Value, track.Name, track.Language.Value,
                    track.Flags.IsDefault, track.Flags.IsForced));
            }
            else
                throw new NotSupportedException("旧请求只支持不指定来源轨号的字幕和指定真实轨号的音频。");
        }
        // 返回集合只读，避免新旧边界交接后的输入被持有原数组的调用方改写。
        return new MuxRequest(spec.VideoPath, subtitles.AsReadOnly(), spec.Fonts.Directory,
            spec.Output.Directory, spec.Output.FileName) { Audios = audios.AsReadOnly() };
    }

    public static MuxJobResult ToResult(MuxResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var status = result.Status switch
        {
            MuxStatus.Succeeded => MuxJobStatus.Succeeded,
            MuxStatus.Failed => MuxJobStatus.Failed,
            MuxStatus.Cancelled => MuxJobStatus.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(result), "未知旧任务状态。")
        };
        // 旧结果未携带失败阶段，不能凭文案或检查项猜测；第 3 步替换为真实阶段与结构化检查。
        var failure = status == MuxJobStatus.Succeeded ? null :
            new JobFailure(status == MuxJobStatus.Cancelled ? ErrorCodes.Cancelled : ErrorCodes.LegacyFailure,
                JobStage.Unknown, result.Error);
        return new MuxJobResult(status,
            result.OutputPath is { } output ? [new OutputArtifact(output)] : [],
            result.Checks.Select(text => new CheckResult(ErrorCodes.LegacyCheck, CheckStatus.NotEvaluated,
                null, null, text)).ToImmutableArray(),
            result.Warnings.Select(warning => new Diagnostic(warning.Code, DiagnosticSeverity.Warning,
                warning.Message, warning.Occurrences)).ToImmutableArray(),
            failure, result.LogPath is { } log ? new LogRef(log) : null);
    }
}
