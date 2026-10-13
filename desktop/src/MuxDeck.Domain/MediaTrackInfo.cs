namespace MuxDeck.Domain;

// 探测契约与 QC 共用唯一轨道事实；保留原始标签与字段存在性，不能把未知值补成已知值。
public sealed record MediaTrackInfo(TrackId Id, TrackKind Kind, string RawType, string Codec,
    string? CodecId, LanguageTag Language, LanguageTag? LanguageIetf, string Name, TrackFlags Flags,
    string? PixelDimensions, int? AudioChannels, long? AudioSamplingFrequency,
    bool HasDefaultFlag, bool HasForcedFlag);
