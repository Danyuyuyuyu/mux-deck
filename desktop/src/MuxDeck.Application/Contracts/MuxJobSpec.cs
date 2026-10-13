using System.Collections.Immutable;
using MuxDeck.Domain;

namespace MuxDeck.Contracts;

public sealed record ExternalTrackSpec(string Path, TrackKind Kind, TrackId? SourceTrackId,
    string Name, LanguageTag Language, TrackFlags Flags);
public sealed record SourceTrackSelection(TrackId TrackId, TrackKind Kind, bool IsIncluded);
public sealed record FontSpec(string Directory);
public sealed record OutputSpec(string Directory, string? FileName = null);
public sealed record ToolConfiguration(string ToolsDirectory, string LogsDirectory,
    string CacheDirectory, string? FontToolsDirectoryOverride = null);

/// <summary>所有路径与工具配置都是本次提交的值，集合使用不可变数组。</summary>
public sealed record MuxJobSpec(string VideoPath, ImmutableArray<ExternalTrackSpec> ExternalTracks,
    ImmutableArray<SourceTrackSelection> SourceTracks, FontSpec Fonts, OutputSpec Output,
    ToolConfiguration Tools);
