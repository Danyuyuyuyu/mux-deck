using System.Text.RegularExpressions;
using MuxDeck.Core.Muxing;

namespace MuxDeck.Desktop.ViewModels;

public sealed record ImportedTrack(string Path, MediaTrack Media, string Language);
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
    public static string? MediaInputError(IReadOnlyList<InputCandidate> candidates)
    {
        if (candidates.Count == 0) return "拖入本地视频、音频、ASS/SRT 或字体目录。";
        if (candidates.Any(c => c.Kind == InputCandidateKind.NonLocal)) return "仅接受系统本地文件或文件夹，URL / 文本路径不受支持。";
        foreach (var c in candidates)
        {
            if (!Path.IsPathFullyQualified(c.Path) || c.Path.Contains("://", StringComparison.Ordinal)) return "需要完整的本地路径。";
            if (c.Kind == InputCandidateKind.LocalDirectory ? !Directory.Exists(c.Path) : !File.Exists(c.Path)) return "路径不存在或不可访问：" + c.Path;
            if (c.Kind == InputCandidateKind.LocalFile && !(MediaFileTypes.IsVideo(c.Path) || MediaFileTypes.IsAudio(c.Path) || MediaFileTypes.IsSubtitle(c.Path) || MediaFileTypes.IsFont(c.Path)))
                return "不支持此文件类型：" + Path.GetFileName(c.Path) + "；支持视频、音频、ASS/SRT 和字体。";
        }
        if (candidates.Select(c => Path.GetFullPath(c.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != candidates.Count) return "同一文件不能重复导入。";
        var fonts = candidates.Where(c => c.Kind == InputCandidateKind.LocalDirectory || MediaFileTypes.IsFont(c.Path)).Select(c => Path.GetFullPath(c.Kind == InputCandidateKind.LocalDirectory ? c.Path : Path.GetDirectoryName(c.Path)!)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return fonts > 1 ? "一次只能设置一个字体目录。" : null;
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
        try
        {
            if (MediaInputError(candidates) is { } error) return new(false, error);
            string? video = null, fonts = null; var tracks = new List<ImportedTrack>();
            foreach (var c in candidates)
            {
                var path = Path.GetFullPath(c.Path);
                if (c.Kind == InputCandidateKind.LocalDirectory || MediaFileTypes.IsFont(path)) { fonts = c.Kind == InputCandidateKind.LocalDirectory ? path : Path.GetDirectoryName(path); continue; }
                var probe = await service.ProbeAsync(path, importCancellation.Token);
                if (!CanEdit || revision != DraftRevision) return new(false, "识别期间任务或草稿已变化，整批输入保持原样。");
                if (MediaFileTypes.IsSubtitle(path))
                {
                    SubtitleText.Validate(path);
                    var subtitle = probe.Tracks.Where(t => t.Type == "subtitles").ToArray();
                    var expected = MediaFileTypes.IsAss(path) ? "S_TEXT/ASS" : "S_TEXT/UTF8";
                    if (subtitle.Length != 1 || (subtitle[0].CodecId is { } id ? id != expected : MediaFileTypes.IsAss(path) ? !subtitle[0].Codec.Contains("ASS", StringComparison.OrdinalIgnoreCase) && !subtitle[0].Codec.Contains("SubStation", StringComparison.OrdinalIgnoreCase) : !subtitle[0].Codec.Contains("SRT", StringComparison.OrdinalIgnoreCase) && !subtitle[0].Codec.Contains("SubRip", StringComparison.OrdinalIgnoreCase))) return new(false, "文件内容不是有效的 ASS/SRT：" + path);
                    tracks.Add(new(path, subtitle[0], ImportedLanguage(path, subtitle[0])));
                }
                else if (probe.Tracks.Any(t => t.Type == "video") && MediaFileTypes.IsVideo(path))
                {
                    if (video is not null) return new(false, "单个封装一次只能接纳一个源视频。");
                    video = path;
                }
                else
                {
                    var audios = probe.Tracks.Where(t => t.Type == "audio").ToArray();
                    if (audios.Length == 0 || !MediaFileTypes.IsAudio(path) && probe.Tracks.Any(t => t.Type == "video")) return new(false, "文件没有可导入的音频轨：" + path);
                    tracks.AddRange(audios.Select(t => new ImportedTrack(path, t, ImportedLanguage(path, t))));
                }
            }
            if (tracks.Any(t => externalOrder.Any(r => SamePath(r.InputPath, t.Path) && (r.IsSubtitle || r.OriginTrackId == t.Media.Id)))) return new(false, "文件中的轨道已加入；请在属性栏更换已有文件。");
            return new(true, "已识别，等待接纳。", new(this, revision, video, fonts, tracks, candidates));
        }
        catch (OperationCanceledException) { return new(false, "识别已取消，原输入保持。"); }
        catch (Exception ex) { return new(false, "无法识别拖入文件，整批未接纳：" + ex.Message); }
    }
    private static string ImportedLanguage(string path, MediaTrack track)
    {
        var language = track.LanguageIetf ?? track.Language;
        if (!string.IsNullOrWhiteSpace(language) && language != "und") return language;
        var name = Path.GetFileNameWithoutExtension(path);
        foreach (var (pattern, code) in new[] { ("zh[-_]hans|简体|简中|(^|[._\\-\\s\\[\\]()])(sc|chs|gb)($|[._\\-\\s\\[\\]()])", "zh-Hans"), ("zh[-_]hant|繁体|繁體|繁中|(^|[._\\-\\s\\[\\]()])(tc|cht|big5)($|[._\\-\\s\\[\\]()])", "zh-Hant"), ("(^|[._\\-\\s\\[\\]()])(eng|en)($|[._\\-\\s\\[\\]()])", "en"), ("(^|[._\\-\\s\\[\\]()])(jpn|ja)($|[._\\-\\s\\[\\]()])", "ja") })
            if (Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase)) return code;
        return "und";
    }
    public MediaImportResult CommitMediaInput(MediaImportPlan plan, bool videoReplacementConfirmed = false)
    {
        if (!CanEdit || !ReferenceEquals(plan.Owner, this) || plan.Revision != DraftRevision) return new(false, "草稿已变化或任务已锁定，原输入保持。");
        if (MediaInputError(plan.Candidates) is { } error) return new(false, error);
        if (plan.Video is not null && !string.IsNullOrWhiteSpace(VideoPath) && !SamePath(VideoPath, plan.Video) && !videoReplacementConfirmed) return new(false, "更换已有视频需要确认，整批未接纳。");
        ChangeConfiguration(() =>
        {
            if (plan.Video is not null && !SamePath(VideoPath, plan.Video)) VideoPath = plan.Video;
            if (plan.Fonts is not null && !SamePath(FontsDirectory, plan.Fonts)) FontsDirectory = plan.Fonts;
            foreach (var input in plan.Tracks)
            {
                var t = input.Media;
                var row = new TrackRowViewModel { Id = "external-" + Guid.NewGuid().ToString("N"), OriginTrackId = t.Id, Type = t.Type == "audio" ? "音频" : "字幕", Codec = t.Codec, CodecId = t.CodecId, Language = input.Language, LegacyLanguage = t.Language, LanguageIetf = t.LanguageIetf, Name = t.Name, IsEditable = true, Policy = "新增", Source = "外加文件", AudioChannels = t.AudioChannels, IsDefault = t.Type == "subtitles" ? !externalOrder.Any(r => r.IsSubtitle) : t.HasDefaultFlag && t.IsDefault, IsForced = t.IsForced };
                row.InputPath = input.Path; row.SetEditGuard(() => CanEdit); row.PropertyChanged += SubtitlePropertyChanged;
                externalOrder.Add(row); MarkDraftChanged();
            }
            RebuildTracks(); UpdateInputs();
        });
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
        if (row.Id is "SC" or "TC") { RemoveSubtitle(row.Id == "SC" ? SubtitleSlot.Sc : SubtitleSlot.Tc); return; }
        row.PropertyChanged -= SubtitlePropertyChanged;
        externalOrder.Remove(row); row.InputPath = ""; MarkDraftChanged(); RebuildTracks(); UpdateInputs();
    }
    public void ReplaceImportedTrack(TrackRowViewModel row, ImportedTrack replacement)
    {
        if (!CanEdit || !externalOrder.Contains(row) || row.Id is "SC" or "TC") return;
        var index = externalOrder.IndexOf(row);
        var updated = new TrackRowViewModel { Id = row.Id, OriginTrackId = replacement.Media.Id, Type = row.Type, Codec = replacement.Media.Codec, CodecId = replacement.Media.CodecId, AudioChannels = replacement.Media.AudioChannels, LegacyLanguage = replacement.Media.Language, LanguageIetf = replacement.Media.LanguageIetf, Language = row.Language, Name = row.Name, IsDefault = row.IsDefault, IsForced = row.IsForced, IsIncluded = row.IsIncluded, IsEditable = true, Source = "外加文件", Policy = "新增" };
        updated.InputPath = replacement.Path; updated.SetEditGuard(() => CanEdit); updated.PropertyChanged += SubtitlePropertyChanged;
        row.PropertyChanged -= SubtitlePropertyChanged; externalOrder[index] = updated;
        if (ReferenceEquals(SelectedTrack, row)) SelectedTrack = updated;
        MarkDraftChanged(); RebuildTracks(); UpdateInputs();
    }
}
