using MuxDeck.Domain;

namespace MuxDeck.Contracts;

public enum ImportStatus { Prepared, Rejected, Cancelled }
public sealed record InputItem(string Path, InputLocationKind Kind);
public sealed record ImportedTrackSpec(string Path, MediaTrackInfo Track, LanguageTag Language);
public sealed record ImportRequest(Guid DraftSessionId, long DraftRevision,
    EquatableArray<InputItem> Inputs, MuxJobSpec? CurrentSpec);
public sealed record ImportPlan(Guid DraftSessionId, long DraftRevision,
    EquatableArray<InputItem> Inputs, string? VideoPath, FontSpec? Fonts,
    EquatableArray<ImportedTrackSpec> Tracks, bool RequiresReplacementConfirmation);
public sealed record OperationFailure(string Code, string Message, string? Detail = null);
public sealed record ImportResult(ImportStatus Status, ImportPlan? Plan, OperationFailure? Failure);
