using System.Collections.Immutable;
using MuxDeck.Domain;

namespace MuxDeck.Contracts;

public sealed record MediaTrackInfo(TrackId Id, TrackKind Kind, string RawType, string Codec,
    string? CodecId, LanguageTag Language, LanguageTag? LanguageIetf, string Name, TrackFlags Flags,
    string? PixelDimensions, int? AudioChannels, long? AudioSamplingFrequency,
    bool HasDefaultFlag = true, bool HasForcedFlag = true);
public sealed record MediaAttachmentInfo(int Id, string FileName, string ContentType, long Size);
public sealed record MediaProbeResult(string Path, ImmutableArray<MediaTrackInfo> Tracks,
    ImmutableArray<MediaAttachmentInfo> Attachments, long? DurationNanoseconds);
