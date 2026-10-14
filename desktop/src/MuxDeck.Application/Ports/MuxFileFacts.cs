using MuxDeck.Contracts;
using MuxDeck.Domain;

namespace MuxDeck.Application.Ports;

public sealed record MuxVideoFacts(string Path, bool Exists);
public sealed record MuxFontDirectoryFacts(string Path, bool Exists);
public sealed record MuxOutputFacts(OutputSpec Output, bool NameValid, bool SameSourceDirectory, bool OverwritesSource);
public sealed record MuxTrackFileFacts(ExternalTrackSpec Track, bool Exists, string? SubtitleText);
public sealed record MuxAttachmentEvidence(long Length, string Sha256);
public sealed record MuxSubtitleEvidence(bool Exists, long Length, string? Text);
