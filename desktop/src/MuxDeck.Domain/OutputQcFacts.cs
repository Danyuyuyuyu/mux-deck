namespace MuxDeck.Domain;

// 只携带事实；媒体探测、文件读取和旧模型转换由外层完成。
public sealed record MediaQcFacts(EquatableArray<MediaTrackInfo> Tracks, long? DurationNanoseconds);
public sealed record SubtitleQcFacts(bool IsAss, string Name, string Language, bool IsDefault, bool IsForced);
public sealed record AudioQcFacts(int TrackId, string Name, string Language, bool IsDefault, bool IsForced);
public sealed record OutputQcResult(EquatableArray<string> Checks, string? Error);
