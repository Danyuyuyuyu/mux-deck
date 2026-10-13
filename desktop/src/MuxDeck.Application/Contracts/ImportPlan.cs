using System.Collections.Immutable;
using MuxDeck.Domain;

namespace MuxDeck.Contracts;

public enum InputItemKind { LocalFile, LocalDirectory, NonLocal }
public enum ImportStatus { Prepared, Rejected, Cancelled }
public sealed record InputItem(string Path, InputItemKind Kind);
public sealed record ImportedTrackSpec(string Path, MediaTrackInfo Track, LanguageTag Language);
public sealed record ImportRequest(Guid DraftSessionId, long DraftRevision,
    ImmutableArray<InputItem> Inputs, MuxJobSpec? CurrentSpec);
public sealed record ImportPlan(Guid DraftSessionId, long DraftRevision,
    ImmutableArray<InputItem> Inputs, string? VideoPath, FontSpec? Fonts,
    ImmutableArray<ImportedTrackSpec> Tracks, bool RequiresReplacementConfirmation);
public sealed record OperationFailure(string Code, string Message, string? Detail = null);
public sealed record ImportResult(ImportStatus Status, ImportPlan? Plan, OperationFailure? Failure);
