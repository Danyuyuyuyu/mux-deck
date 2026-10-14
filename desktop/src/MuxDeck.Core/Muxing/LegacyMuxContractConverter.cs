using MuxDeck.Contracts;
using MuxDeck.Domain;

namespace MuxDeck.Core.Muxing;

/// <summary>只转换旧模型与统一轨道事实，将结构化结果转换为旧文案；不判定业务规则、不执行工具。</summary>
public static class LegacyMuxContractConverter
{
    // 不规范化旧探测字符串；大小写未知类型仍为 Unknown，保留 null 与空 IETF 的区别。
    public static MediaTrackInfo ToTrackInfo(MediaTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        var kind = track.Type switch
        {
            "video" => TrackKind.Video,
            "audio" => TrackKind.Audio,
            "subtitles" => TrackKind.Subtitle,
            _ => TrackKind.Unknown
        };
        return new(new(track.Id), kind, track.Type, track.Codec, track.CodecId,
            new(track.Language), track.LanguageIetf is null ? null : new LanguageTag(track.LanguageIetf),
            track.Name, new(track.IsDefault, track.IsForced), track.PixelDimensions,
            track.AudioChannels, track.AudioSamplingFrequency, track.HasDefaultFlag, track.HasForcedFlag);
    }

    public static MediaProbeResult ToProbeResult(MediaProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        return new(probe.Path, new(probe.Tracks.Select(ToTrackInfo)),
            new(probe.Attachments.Select(attachment => new MediaAttachmentInfo(attachment.Id,
                attachment.FileName, attachment.ContentType, attachment.Size))), probe.DurationNanoseconds);
    }

    public static MuxJobSpec ToSpec(MuxRequest request, Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ToolConfiguration tools = workspace;
        return ToSpec(request, tools);
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
                new TrackFlags(audio.IsDefault, audio.IsForced)))).ToEquatableArray();
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
            result.Checks.Select(text => new CheckResult(ErrorCodes.LegacyCheck, CheckStatus.NotEvaluated) { LegacyText = text }).ToEquatableArray(),
            result.Warnings.Select(warning => new Diagnostic(warning.Code, DiagnosticSeverity.Warning,
                warning.Message, warning.Occurrences)).ToEquatableArray(),
            failure, result.LogPath is { } log ? new LogRef(log) : null);
    }

    // 旧入口的非法请求文案在转换处保留；新用例只接收完整的不可变规格。
    internal static MuxJobSpec ToRunSpec(MuxRequest request, ToolConfiguration tools)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Subtitles is null || request.Audios is null || request.Subtitles.Count + request.Audios.Count == 0)
            return ToSpec(request with { Subtitles = [], Audios = [] }, tools);
        if (request.Subtitles.Any(item => item is null)) throw new ArgumentException("字幕参数不能为空。");
        if (request.Audios.Any(item => item is null)) throw new ArgumentException("音频参数不能为空。");
        return ToSpec(request, tools);
    }

    public static MediaProbe ToLegacyProbe(MediaProbeResult probe) => new(probe.Path,
        probe.Tracks.Select(track => new MediaTrack(track.Id.Value, track.RawType, track.Codec, track.CodecId,
            track.Language.Value, track.LanguageIetf?.Value, track.Name, track.Flags.IsDefault, track.Flags.IsForced,
            track.PixelDimensions, track.AudioChannels, track.AudioSamplingFrequency, track.HasDefaultFlag, track.HasForcedFlag)).ToArray(),
        probe.Attachments.Select(attachment => new MediaAttachment(attachment.Id, attachment.FileName, attachment.ContentType, attachment.Size)).ToArray(),
        probe.DurationNanoseconds);

    public static MuxProgress? ToLegacyProgress(JobProgress value)
    {
        if (value.StepCode == "SUBSET_ITEM")
        {
            return new(MuxStage.Subsetting, $"子集化字幕 {value.Sequence}/{value.Total}");
        }
        if (value.StepCode == "SUBSET_DIAGNOSTICS")
        {
            return new(MuxStage.Subsetting, "子集化注意提示：" + string.Join("；", value.Diagnostics.Select(w => $"{w.Message}（累计 {w.Occurrences} 次）")));
        }
        return value.Stage switch
        {
            JobStage.Validating => new(MuxStage.Validating, "检查输入与真实工具依赖"),
            JobStage.Probing => new(MuxStage.Probing, "探测源视频和所有音轨"),
            JobStage.Muxing => new(MuxStage.Muxing, "保留视频和音轨，嵌入新增字幕与子集字体"),
            JobStage.Checking => new(MuxStage.Checking, "核验轨道、旗标、字体附件和成品可读性"),
            JobStage.Publishing => new(MuxStage.Installing, "在输出目录准备成品并拒绝覆盖地提交"),
            JobStage.Completed => new(MuxStage.Completed, "封装完成", 100),
            _ => null
        };
    }

    public static MuxResult ToLegacyResult(MuxJobResult result) => new(
        result.Status switch { MuxJobStatus.Succeeded => MuxStatus.Succeeded, MuxJobStatus.Cancelled => MuxStatus.Cancelled, _ => MuxStatus.Failed },
        result.Outputs.FirstOrDefault()?.Path, result.Failure is null ? null : LegacyFailure(result.Failure),
        result.Log?.Path, result.Checks.Select(LegacyCheck).ToArray())
    {
        Warnings = result.Diagnostics.Select(d => new MuxWarning(d.Code, d.Message, d.Occurrences)).ToArray()
    };

    private static string LegacyCheck(CheckResult check) => check.Code switch
    {
        MuxCheckCodes.SubsetFonts => $"字幕 {check.Sequence}：修正 ASS 与 {check.Count} 个子集字体的引用、字形完整性通过",
        MuxCheckCodes.RetainedVideo => $"保留 {check.Count} 条 video 轨：编码、尺寸/声道/采样率和轨道元数据一致",
        MuxCheckCodes.RetainedAudio => $"保留 {check.Count} 条 audio 轨：编码、尺寸/声道/采样率和轨道元数据一致",
        MuxCheckCodes.SubtitleTrack => $"新增字幕 {check.Sequence}（轨 ID {check.TrackId?.Value}）：{check.Name} / {(check.LanguageIetf ?? check.Language)?.Value} / default={check.Flags?.IsDefault} / forced={check.Flags?.IsForced}",
        MuxCheckCodes.AddedAudioTrack => $"外加音频 {check.Sequence}（轨 ID {check.TrackId?.Value}）：编码、声道、采样率、名称、语言与旗标一致",
        MuxCheckCodes.Attachments => $"源字幕和附件已移除；{check.Count} 个子集字体附件提取与 SHA-256 校验通过",
        MuxCheckCodes.Readable => "MKV 可探测，新增字幕提取后与同工具往返参考全文一致",
        MuxCheckCodes.AudioPayload => $"外加音频 {check.Sequence} 原始数据及 CodecPrivate SHA-256 一致",
        _ => check.LegacyText ?? ""
    };

    private static string LegacyFailure(JobFailure failure)
    {
        if (failure.Code == ErrorCodes.Cancelled) return "任务已取消";
        if (failure.Code == ErrorCodes.FontBackendNotFound)
            return "找不到同目录的 pyftsubset 与 ttx，AFS 需要可运行的 fontTools 后端。";
        if (!string.IsNullOrEmpty(failure.Message)) return failure.Message;
        return failure.Code switch
        {
            ErrorCodes.InputEmpty => "至少需要一条启用的外加字幕或音频。",
            ErrorCodes.VideoMissing => "视频不存在",
            ErrorCodes.FontsDirectoryMissing => "字体目录不存在：" + failure.Path,
            ErrorCodes.OutputDirectoryInvalid => "请使用与源视频不同的独立输出目录。",
            ErrorCodes.OutputNameInvalid => "输出文件名必须是有效的 MKV 文件名，不能包含目录。",
            ErrorCodes.OutputOverwritesSource => "输出不能覆盖源视频。",
            ErrorCodes.SubtitleInputInvalid => "字幕必须是存在的 ASS/SRT 文件：" + failure.Path,
            ErrorCodes.SubtitleLanguageInvalid => "字幕语言必须使用有效的语言代码或 BCP 47 标签。",
            ErrorCodes.SubtitleNameInvalid => "字幕轨名称无效。",
            ErrorCodes.AudioInputInvalid => "音频路径或 Track ID 无效。",
            ErrorCodes.AudioMetadataInvalid => "音频名称或语言无效。",
            ErrorCodes.SourceVideoMissing => "源文件没有视频轨。",
            ErrorCodes.SourceAudioMissing => "外加音频 Track ID 不存在：" + failure.Path,
            ErrorCodes.TargetAlreadyExists => "目标已存在，拒绝覆盖：" + failure.Path,
            ErrorCodes.VideoCountMismatch => "video 轨数量与请求不同。",
            ErrorCodes.AudioCountMismatch => "audio 轨数量与请求不同。",
            ErrorCodes.VideoMetadataMismatch => $"video 轨 {failure.Sequence} 的编码、媒体属性或轨道元数据与源文件不同。",
            ErrorCodes.AudioMetadataMismatch => $"audio 轨 {failure.Sequence} 的编码、媒体属性或轨道元数据与源文件不同。",
            ErrorCodes.DurationMismatch => "成品时长与源文件不符。",
            ErrorCodes.SubtitleCountMismatch => "新增字幕轨数量不符。",
            ErrorCodes.SubtitleMetadataMismatch => $"新增字幕轨 {failure.Sequence} 的 ASS 类型、名称、语言、default 或 forced 旗标不符。",
            ErrorCodes.AddedAudioMetadataMismatch => $"外加音频 {failure.Sequence} 编码、音频属性或元数据不符。",
            ErrorCodes.AttachmentCountInvalid => "成品字体附件数量不符。",
            ErrorCodes.AttachmentMetadataInvalid => $"字体附件 {failure.Sequence} 的身份、类型或大小不符。",
            ErrorCodes.SubtitleReferenceInvalid => $"新增字幕 {failure.Sequence} 未产出有效 ASS 校验参考字幕。",
            ErrorCodes.SubtitleTextMismatch => $"新增字幕 {failure.Sequence} 提取后内容与同工具字幕参考不符。",
            ErrorCodes.AttachmentPayloadMismatch => $"字体附件 {failure.Sequence} 提取后校验不一致。",
            ErrorCodes.AudioPayloadMismatch => $"外加音频 {failure.Sequence} 数据与参考不符。",
            ErrorCodes.SubtitleTextInvalid => failure.Detail ?? "SRT 没有有效时间轴：" + failure.Path,
            _ => failure.Code
        };
    }

    // 仅复制旧请求的路径投影；业务校验由用例完成，旧日志不从新规格反向解析。
    internal static MuxRequest NormalizeRequestForLog(MuxRequest request)
    {
        var video = Path.GetFullPath(request.VideoPath);
        return request with
        {
            VideoPath = video,
            FontsDirectory = string.IsNullOrWhiteSpace(request.FontsDirectory) ? "" : Path.GetFullPath(request.FontsDirectory),
            OutputDirectory = Path.GetFullPath(request.OutputDirectory),
            OutputFileName = OutputNaming.ResolveForJob(request.OutputFileName, Path.GetFileNameWithoutExtension(video)),
            Subtitles = request.Subtitles.Select(subtitle => subtitle with { Path = Path.GetFullPath(subtitle.Path) }).ToArray(),
            Audios = request.Audios.Select(audio => audio with { Path = Path.GetFullPath(audio.Path) }).ToArray()
        };
    }
}
