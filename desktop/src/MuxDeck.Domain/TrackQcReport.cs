namespace MuxDeck.Domain;

// 规则只产生可解释的事实，旧文本通过兼容转换生成。
public enum TrackQcCheckKind { Retained, Subtitle, AddedAudio }
public enum TrackQcFailureKind { SourceCount, SourceMetadata, Duration, SubtitleCount, SubtitleMetadata, AudioMetadata }
public sealed record TrackQcCheck(TrackQcCheckKind Kind, TrackKind TrackKind, int Index, int Count, MediaTrackInfo? Track);
public sealed record TrackQcFailure(TrackQcFailureKind Kind, TrackKind TrackKind = TrackKind.Unknown, int Index = 0);
public sealed record TrackQcReport(EquatableArray<TrackQcCheck> Checks, TrackQcFailure? Failure);
