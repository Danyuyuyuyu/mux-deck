using MuxDeck.Domain;

namespace MuxDeck.Contracts;

public sealed record MediaAttachmentInfo(int Id, string FileName, string ContentType, long Size);
public sealed record MediaProbeResult(string Path, EquatableArray<MediaTrackInfo> Tracks,
    EquatableArray<MediaAttachmentInfo> Attachments, long? DurationNanoseconds);
