namespace MuxDeck.Domain;

// 规则只产生可解释的事实，旧文本通过兼容转换生成。
public enum TrackQcCheckKind { Retained, Subtitle, AddedAudio }
public enum TrackQcFailureKind { SourceCount, SourceMetadata, Duration, SubtitleCount, SubtitleMetadata, AudioMetadata }
public sealed record TrackQcCheck(TrackQcCheckKind Kind, TrackKind TrackKind, int Index, int Count, MediaTrackInfo? Track);
public sealed record TrackQcFailure(TrackQcFailureKind Kind, TrackKind TrackKind = TrackKind.Unknown, int Index = 0);
public sealed record TrackQcReport(EquatableArray<TrackQcCheck> Checks, TrackQcFailure? Failure);

public static class LegacyTrackQcText
{
    public static string Check(TrackQcCheck check) => check.Kind switch
    {
        TrackQcCheckKind.Retained => $"保留 {check.Count} 条 {Type(check.TrackKind)} 轨：编码、尺寸/声道/采样率和轨道元数据一致",
        TrackQcCheckKind.Subtitle => $"新增字幕 {check.Index}（轨 ID {check.Track!.Id.Value}）：{check.Track.Name} / {check.Track.LanguageIetf?.Value ?? check.Track.Language.Value} / default={check.Track.Flags.IsDefault} / forced={check.Track.Flags.IsForced}",
        TrackQcCheckKind.AddedAudio => $"外加音频 {check.Index}（轨 ID {check.Track!.Id.Value}）：编码、声道、采样率、名称、语言与旗标一致",
        _ => throw new ArgumentOutOfRangeException(nameof(check))
    };
    public static string Failure(TrackQcFailure failure) => failure.Kind switch
    {
        TrackQcFailureKind.SourceCount => $"{Type(failure.TrackKind)} 轨数量与请求不同。",
        TrackQcFailureKind.SourceMetadata => $"{Type(failure.TrackKind)} 轨 {failure.Index} 的编码、媒体属性或轨道元数据与源文件不同。",
        TrackQcFailureKind.Duration => "成品时长与源文件不符。",
        TrackQcFailureKind.SubtitleCount => "新增字幕轨数量不符。",
        TrackQcFailureKind.SubtitleMetadata => $"新增字幕轨 {failure.Index} 的 ASS 类型、名称、语言、default 或 forced 旗标不符。",
        TrackQcFailureKind.AudioMetadata => $"外加音频 {failure.Index} 编码、音频属性或元数据不符。",
        _ => throw new ArgumentOutOfRangeException(nameof(failure))
    };
    private static string Type(TrackKind kind) => kind == TrackKind.Video ? "video" : "audio";
}
