namespace MuxDeck.Domain;

// 仅携带探测与任务快照事实；路径拆分、探测进程和文件读取由旧边界完成。
public sealed record TrackQcFacts(int Id, string Type, string Codec, string? CodecId,
    string Language, string? LanguageIetf, string Name, bool IsDefault, bool IsForced,
    string? PixelDimensions, int? AudioChannels, long? AudioSamplingFrequency,
    bool HasDefaultFlag, bool HasForcedFlag);
public sealed record MediaQcFacts(EquatableArray<TrackQcFacts> Tracks, long? DurationNanoseconds);
public sealed record SubtitleQcFacts(bool IsAss, string Name, string Language, bool IsDefault, bool IsForced);
public sealed record AudioQcFacts(int TrackId, string Name, string Language, bool IsDefault, bool IsForced);
public sealed record OutputQcResult(EquatableArray<string> Checks, string? Error);
