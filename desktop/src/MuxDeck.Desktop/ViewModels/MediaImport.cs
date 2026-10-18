using MuxDeck.Contracts;
using MuxDeck.Core.Muxing;
using MuxDeck.Domain;

namespace MuxDeck.Desktop.ViewModels;

/// <summary>导入轨道在 Desktop 的投影；与契约规格的互转只在这里维护。</summary>
public sealed record ImportedTrack(string Path, MediaTrack Media, string Language)
{
    internal static ImportedTrack From(ImportedTrackSpec spec) =>
        new(spec.Path, ToLegacy(spec.Track), spec.Language.Value);

    internal ImportedTrackSpec ToSpec() => new(Path,
        new MediaTrackInfo(new TrackId(Media.Id), Kind(Media.Type), Media.Type, Media.Codec, Media.CodecId,
            new LanguageTag(Media.Language), Media.LanguageIetf is null ? null : new LanguageTag(Media.LanguageIetf),
            Media.Name, new TrackFlags(Media.IsDefault, Media.IsForced), Media.PixelDimensions, Media.AudioChannels,
            Media.AudioSamplingFrequency, Media.HasDefaultFlag, Media.HasForcedFlag),
        new LanguageTag(Language));

    internal static MediaTrack ToLegacy(MediaTrackInfo track) => new(track.Id.Value, track.RawType, track.Codec,
        track.CodecId, track.Language.Value, track.LanguageIetf?.Value, track.Name, track.Flags.IsDefault,
        track.Flags.IsForced, track.PixelDimensions, track.AudioChannels, track.AudioSamplingFrequency,
        track.HasDefaultFlag, track.HasForcedFlag);

    private static TrackKind Kind(string type) => type switch
    {
        "video" => TrackKind.Video,
        "audio" => TrackKind.Audio,
        "subtitles" => TrackKind.Subtitle,
        _ => TrackKind.Unknown
    };
}
public sealed record MediaImportResult(bool Succeeded, string Message, MediaImportPlan? Plan = null);
public sealed class MediaImportPlan
{
    internal MediaImportPlan(SingleMuxViewModel owner, long revision, string? video, string? fonts, IEnumerable<ImportedTrack> tracks, IEnumerable<InputCandidate> candidates)
    { Owner = owner; Revision = revision; Video = video; Fonts = fonts; Tracks = Array.AsReadOnly(tracks.ToArray()); Candidates = Array.AsReadOnly(candidates.ToArray()); }
    internal SingleMuxViewModel Owner { get; }
    internal long Revision { get; }
    public string? Video { get; }
    public string? Fonts { get; }
    public IReadOnlyList<ImportedTrack> Tracks { get; }
    public IReadOnlyList<InputCandidate> Candidates { get; }
}

public sealed partial class SingleMuxViewModel
{
    private readonly CancellationTokenSource importCancellation = new();
    private readonly List<Task> importTasks = [];
    // 识别与校验由 UseCase 承担；端口与用例都来自装配好的服务，不在 ViewModel 里创建默认实现。
    private IPrepareImport Import => service.Import;

    // 拖入反馈的同步路径与导入用例共用同一份 Domain 规则与同一份事实实现，这里只做错误码到文案的映射。
    public string? ValidateMediaInput(IReadOnlyList<InputCandidate> candidates) =>
        DescribeBatch(service.CollectFacts(Items(candidates)));

    private static InputItem[] Items(IReadOnlyList<InputCandidate> candidates) =>
        [.. candidates.Select(candidate => new InputItem(candidate.Path, candidate.LocationKind))];

    private static string? DescribeBatch(IReadOnlyList<InputPathFacts> facts)
    {
        var failure = InputRules.CheckMediaBatch(facts);
        return failure.Succeeded ? null : MediaInputText.Describe(MediaBatchCodes.For(failure.Error)!, failure.Path);
    }

    public Task<MediaImportResult> PrepareMediaInputAsync(IReadOnlyList<InputCandidate> candidates)
    {
        importTasks.RemoveAll(t => t.IsCompleted);
        var task = PrepareMediaCoreAsync(Array.AsReadOnly(candidates.ToArray()));
        importTasks.Add(task); return task;
    }

    private async Task<MediaImportResult> PrepareMediaCoreAsync(IReadOnlyList<InputCandidate> candidates)
    {
        if (!CanEdit) return new(false, "当前任务已锁定，不能接纳输入。");
        var revision = DraftRevision;
        BeginToolProbe();
        try
        {
            var request = new ImportRequest(Guid.NewGuid(), revision,
                [.. candidates.Select(candidate => new InputItem(candidate.Path, candidate.LocationKind))],
                draft.ToConflictSpec(tools));
            // 视图层必须回到原同步上下文，EndToolProbe 等回调会触达界面对象。
            var result = await Import.PrepareAsync(request, tools, importCancellation.Token);
            if (!CanEdit || revision != DraftRevision) return new(false, "识别期间任务或草稿已变化，整批输入保持原样。");
            if (result.Status != ImportStatus.Prepared) return new(false, MediaInputText.Describe(result.Failure!));
            return new(true, "已识别，等待接纳。", ToPlan(result.Plan!, revision, candidates));
        }
        catch (OperationCanceledException) { return new(false, "识别已取消，原输入保持。"); }
        catch (Exception ex) { return new(false, "无法识别拖入文件，整批未接纳：" + ex.Message); }
        finally { EndToolProbe(); }
    }

    private MediaImportPlan ToPlan(ImportPlan plan, long revision, IReadOnlyList<InputCandidate> candidates) =>
        new(this, revision, plan.VideoPath, plan.Fonts?.Directory,
            plan.Tracks.Select(ImportedTrack.From), candidates);

    public MediaImportResult CommitMediaInput(MediaImportPlan plan, bool videoReplacementConfirmed = false)
    {
        if (!CanEdit || !ReferenceEquals(plan.Owner, this) || plan.Revision != DraftRevision)
            return new(false, "草稿已变化或任务已锁定，原输入保持。");
        if (ValidateMediaInput(plan.Candidates) is { } error) return new(false, error);
        var identities = plan.Tracks.Select(_ => "external-" + Guid.NewGuid().ToString("N")).ToArray();
        var imported = plan.Tracks.Select((input, index) => new DraftTrackImport(identities[index], input.Path,
            input.Media.Type == "audio" ? TrackKind.Audio : TrackKind.Subtitle, new TrackId(input.Media.Id),
            input.Media.Name, new LanguageTag(input.Language), input.Media.HasDefaultFlag, input.Media.IsDefault,
            input.Media.IsForced)).ToArray();
        var videoBefore = draft.VideoPath;
        var fontsBefore = draft.FontsDirectory;
        var outcome = draft.CommitImport(new DraftImport(plan.Video, plan.Fonts, imported, videoReplacementConfirmed));
        if (!outcome.Succeeded)
            return new(false, DraftText.Describe(DraftEntry.Import, InputOperation.Audio, outcome.Error, outcome.Reason, outcome.Detail));

        if (plan.Video is not null && draft.VideoPath != videoBefore)
        {
            sourceProbe = null; SelectedTrack = null; RebuildTracks(false);
            Raise(nameof(VideoPath)); Raise(nameof(ResolvedOutputFileName)); NotifyVideoSummary(); _ = ProbeVideoAsync();
        }
        if (plan.Fonts is not null && draft.FontsDirectory != fontsBefore) { Raise(nameof(FontsDirectory)); Raise(nameof(HasFontsDirectory)); }
        var computed = draft.ExternalTracks;
        for (var index = 0; index < plan.Tracks.Count; index++)
        {
            var input = plan.Tracks[index];
            var t = input.Media;
            var identity = identities[index];
            var flags = computed.First(track => track.Identity == identity).Flags;
            var row = new TrackRowViewModel { Id = identity, OriginTrackId = t.Id, Type = t.Type == "audio" ? "音频" : "字幕", Codec = t.Codec, CodecId = t.CodecId, Language = input.Language, LegacyLanguage = t.Language, LanguageIetf = t.LanguageIetf, Name = t.Name, IsEditable = true, Policy = "新增", Source = "外加文件", AudioChannels = t.AudioChannels, IsDefault = flags.IsDefault, IsForced = t.IsForced };
            row.InputPath = input.Path; row.SetEditGuard(() => CanEdit); row.PropertyChanged += SubtitlePropertyChanged;
            externalOrder.Add(row);
        }
        RebuildTracks(); UpdateInputs();
        var added = new List<string>();
        if (plan.Video is not null) added.Add("视频");
        if (plan.Tracks.Count > 0) added.Add($"{plan.Tracks.Count} 条外加轨道");
        if (plan.Fonts is not null) added.Add("字体目录");
        var message = plan.Video is null && plan.Tracks.Count == 0 && plan.Fonts is not null ? "已设置字体目录。" : "已自动落位：" + string.Join(" · ", added) + "。";
        ReportInteractionMessage(message, NotificationKind.Success); return new(true, message);
    }
    public void RemoveExternalTrack(TrackRowViewModel row)
    {
        if (!CanEdit || !externalOrder.Contains(row)) return;
        if (row.Slot is { } slot) { RemoveSubtitle(slot); return; }
        row.PropertyChanged -= SubtitlePropertyChanged;
        draft.RemoveExternalTrack(row.Id);
        externalOrder.Remove(row); row.InputPath = ""; RebuildTracks(); UpdateInputs();
    }
    public void ReplaceImportedTrack(TrackRowViewModel row, ImportedTrack replacement)
    {
        if (!CanEdit || !externalOrder.Contains(row) || row.Slot is not null) return;
        var index = externalOrder.IndexOf(row);
        var outcome = draft.ReplaceExternalTrack(row.Id, new DraftTrackState(row.Id, replacement.Path,
            row.IsAudio ? TrackKind.Audio : TrackKind.Subtitle, new TrackId(replacement.Media.Id), row.Name,
            new LanguageTag(row.Language ?? ""), new TrackFlags(row.IsDefault, row.IsForced)) { IsIncluded = row.IsIncluded });
        if (!outcome.Succeeded) return;
        var updated = new TrackRowViewModel { Id = row.Id, OriginTrackId = replacement.Media.Id, Type = row.Type, Codec = replacement.Media.Codec, CodecId = replacement.Media.CodecId, AudioChannels = replacement.Media.AudioChannels, LegacyLanguage = replacement.Media.Language, LanguageIetf = replacement.Media.LanguageIetf, Language = row.Language ?? "", Name = row.Name, IsDefault = row.IsDefault, IsForced = row.IsForced, IsIncluded = row.IsIncluded, IsEditable = true, Source = "外加文件", Policy = "新增" };
        updated.InputPath = replacement.Path; updated.SetEditGuard(() => CanEdit); updated.PropertyChanged += SubtitlePropertyChanged;
        row.PropertyChanged -= SubtitlePropertyChanged; externalOrder[index] = updated;
        if (ReferenceEquals(SelectedTrack, row)) SelectedTrack = updated;
        RebuildTracks(); UpdateInputs();
    }
}
