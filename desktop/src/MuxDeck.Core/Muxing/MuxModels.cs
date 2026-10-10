namespace MuxDeck.Core.Muxing;

public sealed record SubtitleInput(string Path, string Name, string Language, bool IsDefault, bool IsForced = false);
public sealed record AudioInput(string Path, int TrackId, string Name, string Language, bool IsDefault = false, bool IsForced = false);
public sealed record MuxRequest(string VideoPath, IReadOnlyList<SubtitleInput> Subtitles,
    string FontsDirectory, string OutputDirectory, string? OutputFileName = null)
{
    public IReadOnlyList<AudioInput> Audios { get; init; } = [];
}
public enum MuxStatus { Succeeded, Failed, Cancelled }
public enum MuxStage { Validating, Probing, Subsetting, Muxing, Checking, Installing, Completed }
public sealed record MuxProgress(MuxStage Stage, string Message, double? Percent = null);
public sealed record MuxWarning(string Code, string Message, int Occurrences);
public sealed record MuxResult(MuxStatus Status, string? OutputPath, string? Error,
    string? LogPath, IReadOnlyList<string> Checks)
{
    public IReadOnlyList<MuxWarning> Warnings { get; init; } = [];
}
public sealed record MediaTrack(int Id, string Type, string Codec, string? CodecId,
    string Language, string? LanguageIetf, string Name, bool IsDefault, bool IsForced,
    string? PixelDimensions, int? AudioChannels, long? AudioSamplingFrequency,
    bool HasDefaultFlag = true, bool HasForcedFlag = true);
public sealed record MediaAttachment(int Id, string FileName, string ContentType, long Size);
public sealed record MediaProbe(string Path, IReadOnlyList<MediaTrack> Tracks,
    IReadOnlyList<MediaAttachment> Attachments, long? DurationNanoseconds);
