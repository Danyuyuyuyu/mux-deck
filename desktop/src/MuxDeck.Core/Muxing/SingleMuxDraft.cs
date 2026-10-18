using MuxDeck.Application.Drafts;
using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using MuxDeck.Domain;

namespace MuxDeck.Core.Muxing;

public enum DraftSlot { Sc, Tc, Unknown }

public enum DraftOperationKind { Video, Subtitles, FontsDirectory, OutputDirectory }

public enum DraftErrorKind
{
    None,
    Locked,
    Stale,
    ConfirmationRequired,
    InvalidCount,
    InvalidSlot,
    Duplicate,
    NonLocal,
    InvalidPath,
    WrongType,
    MissingPath,
    ExternalTrackMissing
}

public enum DraftFailureReason
{
    None,
    NonLocal,
    WrongLocationKindDirectory,
    WrongLocationKindFile,
    PathNotAbsolute,
    InvalidPathSyntax,
    PathInaccessible,
    DirectoryMissing,
    FileMissing,
    UnsupportedVideoExtension,
    UnsupportedSubtitleExtension
}

public sealed record DraftResult(bool Succeeded, DraftErrorKind Error, DraftPlan? Plan = null,
    string? Detail = null, DraftFailureReason Reason = DraftFailureReason.None);

public sealed record DraftPreparation(bool Succeeded, DraftErrorKind Error, DraftPlan? Plan = null,
    bool RequiresConfirmation = false, string? Detail = null, DraftFailureReason Reason = DraftFailureReason.None);

/// <summary>提交计划的 Core 过渡投影；Application 计划仍由草稿内部持有。</summary>
public sealed class DraftPlan
{
    internal DraftPlan(DraftAcceptancePlan inner)
    {
        Inner = inner;
        Operation = SingleMuxDraft.Operation(inner.Operation);
        Revision = inner.Revision;
        RequiresReplacementConfirmation = inner.RequiresReplacementConfirmation;
        Candidates = [.. inner.Candidates.Select(candidate =>
            new DraftCandidateItem(candidate.Item.Path, candidate.Item.Kind))];
        Assignments = [.. inner.Assignments.Select(assignment =>
            new DraftSlotAssignment(SingleMuxDraft.CoreSlot(assignment.Slot),
                new DraftCandidateItem(assignment.Candidate.Item.Path, assignment.Candidate.Item.Kind)))];
    }

    internal DraftAcceptancePlan Inner { get; }
    public DraftOperationKind Operation { get; }
    public long Revision { get; }
    public bool RequiresReplacementConfirmation { get; }
    public IReadOnlyList<DraftCandidateItem> Candidates { get; }
    public IReadOnlyList<DraftSlotAssignment> Assignments { get; }
}

public sealed record DraftSlotState(string Path, bool IsIncluded, string Name, LanguageTag Language,
    bool IsDefault, bool IsForced);

public sealed record DraftTrackState(string Identity, string Path, TrackKind Kind, TrackId? SourceTrackId,
    string Name, LanguageTag Language, TrackFlags Flags)
{
    public bool IsIncluded { get; init; } = true;
}

public sealed record DraftTrackImport(string Identity, string Path, TrackKind Kind, TrackId? SourceTrackId,
    string Name, LanguageTag Language, bool HasDefaultFlag, bool IsDefault, bool IsForced);

public sealed record DraftImport(string? VideoPath, string? FontsDirectory,
    IReadOnlyList<DraftTrackImport> Tracks, bool VideoReplacementConfirmed = false);

/// <summary>
/// ViewModel 使用的草稿门面：保持 ViewModel 只依赖 Core/Contracts/Domain，
/// 内部仍委托给 Application.MuxDraft，草稿是唯一数据源。
/// </summary>
public sealed class SingleMuxDraft
{
    private readonly MuxDraft inner;

    public SingleMuxDraft(ISyncPathFacts pathFacts)
    {
        inner = new MuxDraft(syncPathFacts: pathFacts);
        inner.Changed += (_, revision) => Changed?.Invoke(this, revision);
        inner.EditabilityChanged += (_, _) => EditabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler<long>? Changed;
    public event EventHandler? EditabilityChanged;

    public long DraftRevision => inner.DraftRevision;
    public bool IsLocked => inner.IsLocked;
    public bool CanEdit => inner.CanEdit;
    public string VideoPath => inner.VideoPath;
    public string? VideoBaseName => inner.VideoBaseName;
    public string FontsDirectory => inner.FontsDirectory;
    public string OutputDirectory => inner.OutputDirectory;
    public string OutputFileName => inner.OutputFileName;
    public DraftSlotState Sc => Slot(DraftSlot.Sc);
    public DraftSlotState Tc => Slot(DraftSlot.Tc);
    public IReadOnlyList<DraftTrackState> ExternalTracks => [.. inner.ExternalTracks.Select(State)];

    public DraftSlotState Slot(DraftSlot slot)
    {
        var value = inner.Slot(ApplicationSlot(slot));
        return new(value.Path, value.IsIncluded, value.Name, value.Language, value.IsDefault, value.IsForced);
    }

    public string PathFor(DraftSlot slot) => inner.PathFor(ApplicationSlot(slot));
    public bool IsSlotIncluded(DraftSlot slot) => inner.IsSlotIncluded(ApplicationSlot(slot));
    public void SetLocked(bool locked) => inner.SetLocked(locked);

    public DraftResult SetVideoPath(string value) => Result(inner.SetVideoPath(value));
    public DraftResult SetFontsDirectory(string value) => Result(inner.SetFontsDirectory(value));
    public DraftResult SetOutputDirectory(string value) => Result(inner.SetOutputDirectory(value));
    public DraftResult SetOutputFileName(string value) => Result(inner.SetOutputFileName(value));
    public DraftResult SetSubtitlePath(DraftSlot slot, string path) => Result(inner.SetSubtitlePath(ApplicationSlot(slot), path));
    public DraftResult SetSubtitleIncluded(DraftSlot slot, bool included) => Result(inner.SetSubtitleIncluded(ApplicationSlot(slot), included));
    public DraftResult SetSubtitleIncludedRaw(DraftSlot slot, bool included) => Result(inner.SetSubtitleIncludedRaw(ApplicationSlot(slot), included));
    public DraftResult SetSubtitleName(DraftSlot slot, string name) => Result(inner.SetSubtitleName(ApplicationSlot(slot), name));
    public DraftResult SetSubtitleDefault(DraftSlot slot, bool value) => Result(inner.SetSubtitleDefault(ApplicationSlot(slot), value));
    public DraftResult SetSubtitleForced(DraftSlot slot, bool value) => Result(inner.SetSubtitleForced(ApplicationSlot(slot), value));

    public DraftResult AddExternalTrack(DraftTrackState track) => Result(inner.AddExternalTrack(Snapshot(track)));
    public DraftResult RemoveExternalTrack(string identity) => Result(inner.RemoveExternalTrack(identity));
    public DraftResult ReplaceExternalTrack(string identity, DraftTrackState replacement) =>
        Result(inner.ReplaceExternalTrack(identity, Snapshot(replacement)));
    public DraftResult SetExternalIncluded(string identity, bool value) => Result(inner.SetExternalIncluded(identity, value));
    public DraftResult SetExternalName(string identity, string value) => Result(inner.SetExternalName(identity, value));
    public DraftResult SetExternalDefault(string identity, bool value) => Result(inner.SetExternalDefault(identity, value));
    public DraftResult SetExternalForced(string identity, bool value) => Result(inner.SetExternalForced(identity, value));

    public DraftResult CommitImport(DraftImport request) =>
        Result(inner.CommitImport(new DraftImportRequest(request.VideoPath, request.FontsDirectory,
            [.. request.Tracks.Select(Imported)], request.VideoReplacementConfirmed)));

    public DraftPreparation Prepare(DraftOperationKind operation, IReadOnlyList<DraftCandidateItem> candidates,
        IReadOnlyList<DraftSlotAssignment> assignments)
    {
        var result = inner.Prepare(Operation(operation),
            [.. candidates.Select(candidate => new DraftCandidate(new InputItem(candidate.Path, candidate.Kind)))],
            [.. assignments.Select(assignment => new DraftAssignment(ApplicationSlot(assignment.Slot),
                new DraftCandidate(new InputItem(assignment.Candidate.Path, assignment.Candidate.Kind))))]);
        return Preparation(result);
    }

    public DraftResult Commit(DraftPlan plan, bool replacementsConfirmed = false) =>
        Result(inner.Commit(plan.Inner, replacementsConfirmed));

    public MuxJobSpec ToSpec(ToolConfiguration tools) => inner.ToSpec(tools);
    public MuxJobSpec ToConflictSpec(ToolConfiguration tools) => inner.ToConflictSpec(tools);

    private static DraftResult Result(DraftOutcome outcome) =>
        new(outcome.Succeeded, Error(outcome.ErrorCode), null, outcome.Detail, Reason(outcome.Reason));

    private static DraftPreparation Preparation(DraftPrepareResult result) =>
        new(result.Succeeded, Error(result.ErrorCode), result.Plan is null ? null : new DraftPlan(result.Plan),
            result.RequiresConfirmation, result.Detail, Reason(result.Reason));

    private static DraftTrackState State(DraftExternalTrackSnapshot track) =>
        new(track.Identity, track.Path, track.Kind, track.SourceTrackId, track.Name, track.Language, track.Flags)
        { IsIncluded = track.IsIncluded };

    private static DraftTrackState State(DraftTrackImport track) =>
        new(track.Identity, track.Path, track.Kind, track.SourceTrackId, track.Name, track.Language,
            new TrackFlags(track.IsDefault, track.IsForced));

    private static DraftExternalTrackSnapshot Snapshot(DraftTrackState track) =>
        new(track.Identity, track.Path, track.Kind, track.SourceTrackId, track.Name, track.Language, track.Flags)
        { IsIncluded = track.IsIncluded };

    private static DraftImportedTrack Imported(DraftTrackImport track) =>
        new(track.Identity, track.Path, track.Kind, track.SourceTrackId, track.Name, track.Language,
            track.HasDefaultFlag, track.IsDefault, track.IsForced);

    private static SubtitleSlot ApplicationSlot(DraftSlot slot) => slot switch
    {
        DraftSlot.Sc => SubtitleSlot.Sc,
        DraftSlot.Tc => SubtitleSlot.Tc,
        // 保留枚举以外的值，让草稿按 InvalidSlot 拒绝而不是折叠成 TC。
        _ => (SubtitleSlot)99
    };

    internal static DraftSlot CoreSlot(SubtitleSlot slot) => slot switch
    {
        SubtitleSlot.Sc => DraftSlot.Sc,
        SubtitleSlot.Tc => DraftSlot.Tc,
        _ => DraftSlot.Unknown
    };

    internal static DraftOperationKind Operation(DraftOperation operation) => operation switch
    {
        DraftOperation.Video => DraftOperationKind.Video,
        DraftOperation.Subtitles => DraftOperationKind.Subtitles,
        DraftOperation.FontsDirectory => DraftOperationKind.FontsDirectory,
        _ => DraftOperationKind.OutputDirectory
    };

    private static DraftOperation Operation(DraftOperationKind operation) => operation switch
    {
        DraftOperationKind.Video => DraftOperation.Video,
        DraftOperationKind.Subtitles => DraftOperation.Subtitles,
        DraftOperationKind.FontsDirectory => DraftOperation.FontsDirectory,
        _ => DraftOperation.OutputDirectory
    };

    private static DraftErrorKind Error(string? code) => code switch
    {
        DraftCodes.Locked => DraftErrorKind.Locked,
        DraftCodes.Stale => DraftErrorKind.Stale,
        DraftCodes.ConfirmationRequired => DraftErrorKind.ConfirmationRequired,
        DraftCodes.InvalidCount => DraftErrorKind.InvalidCount,
        DraftCodes.InvalidSlot => DraftErrorKind.InvalidSlot,
        DraftCodes.Duplicate => DraftErrorKind.Duplicate,
        DraftCodes.NonLocal => DraftErrorKind.NonLocal,
        DraftCodes.InvalidPath => DraftErrorKind.InvalidPath,
        DraftCodes.WrongType => DraftErrorKind.WrongType,
        DraftCodes.MissingPath => DraftErrorKind.MissingPath,
        DraftCodes.ExternalTrackMissing => DraftErrorKind.ExternalTrackMissing,
        _ => DraftErrorKind.None
    };

    private static DraftFailureReason Reason(DraftReason reason) => reason switch
    {
        DraftReason.NonLocal => DraftFailureReason.NonLocal,
        DraftReason.WrongLocationKindDirectory => DraftFailureReason.WrongLocationKindDirectory,
        DraftReason.WrongLocationKindFile => DraftFailureReason.WrongLocationKindFile,
        DraftReason.PathNotAbsolute => DraftFailureReason.PathNotAbsolute,
        DraftReason.InvalidPathSyntax => DraftFailureReason.InvalidPathSyntax,
        DraftReason.PathInaccessible => DraftFailureReason.PathInaccessible,
        DraftReason.DirectoryMissing => DraftFailureReason.DirectoryMissing,
        DraftReason.FileMissing => DraftFailureReason.FileMissing,
        DraftReason.UnsupportedVideoExtension => DraftFailureReason.UnsupportedVideoExtension,
        DraftReason.UnsupportedSubtitleExtension => DraftFailureReason.UnsupportedSubtitleExtension,
        _ => DraftFailureReason.None
    };
}

public sealed record DraftCandidateItem(string Path, InputLocationKind Kind);
public sealed record DraftSlotAssignment(DraftSlot Slot, DraftCandidateItem Candidate);
