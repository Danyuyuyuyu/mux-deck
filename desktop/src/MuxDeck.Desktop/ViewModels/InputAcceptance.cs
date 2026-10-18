using MuxDeck.Core.Muxing;
using MuxDeck.Domain;

namespace MuxDeck.Desktop.ViewModels;

public enum InputCandidateKind { LocalFile, LocalDirectory, NonLocal }
public enum SubtitleSlot { Sc, Tc }
public enum InputOperation { Video, Subtitles, FontsDirectory, OutputDirectory, Audio }
public enum InputAcceptanceError { None, Locked, InvalidCount, NonLocal, InvalidPath, WrongType, MissingPath, Duplicate, InvalidSlot, Stale, ConfirmationRequired }
public enum VideoProbeState { Empty, Probing, Succeeded, Failed }
public enum SingleMuxState { NoVideo, Probing, ProbeFailed, Incomplete, Ready, Running, Cancelling, Closing, Cancelled, Failed, Succeeded, SucceededWithWarnings }

// 视图只能从系统文件数据构造 Local 候选；文本路径和 URL 必须标为 NonLocal。
public sealed record InputCandidate(string Path, InputCandidateKind Kind)
{
    internal InputLocationKind LocationKind => Kind switch
    {
        InputCandidateKind.LocalFile => InputLocationKind.LocalFile,
        InputCandidateKind.LocalDirectory => InputLocationKind.LocalDirectory,
        InputCandidateKind.NonLocal => InputLocationKind.NonLocal,
        _ => InputLocationKind.Unknown
    };
}
public sealed record SubtitleAssignment(SubtitleSlot Slot, InputCandidate Candidate);
public sealed record InputAcceptanceResult(InputAcceptanceError Error, string Message, InputAcceptancePlan? Plan = null)
{
    public bool Succeeded => Error == InputAcceptanceError.None;
}

// 计划只能由接纳校验创建；保留只读副本，等待弹窗期间不会被调用方改写。
public sealed class InputAcceptancePlan
{
    internal InputAcceptancePlan(SingleMuxViewModel owner, InputOperation operation, DraftPlan draftPlan)
    {
        Owner = owner;
        Operation = operation;
        DraftPlan = draftPlan;
        DraftRevision = draftPlan.Revision;
        Candidates = Array.AsReadOnly(draftPlan.Candidates
            .Select(candidate => new InputCandidate(candidate.Path, Kind(candidate.Kind))).ToArray());
        Assignments = Array.AsReadOnly(draftPlan.Assignments
            .Select(assignment => new SubtitleAssignment((SubtitleSlot)(int)assignment.Slot,
                new InputCandidate(assignment.Candidate.Path, Kind(assignment.Candidate.Kind)))).ToArray());
        RequiresReplacementConfirmation = draftPlan.RequiresReplacementConfirmation;
    }

    internal SingleMuxViewModel Owner { get; }
    internal DraftPlan DraftPlan { get; }
    public InputOperation Operation { get; }
    public long DraftRevision { get; }
    public IReadOnlyList<InputCandidate> Candidates { get; }
    public IReadOnlyList<SubtitleAssignment> Assignments { get; }
    public bool RequiresReplacementConfirmation { get; }

    private static InputCandidateKind Kind(InputLocationKind kind) => kind switch
    {
        InputLocationKind.LocalFile => InputCandidateKind.LocalFile,
        InputLocationKind.LocalDirectory => InputCandidateKind.LocalDirectory,
        InputLocationKind.NonLocal => InputCandidateKind.NonLocal,
        _ => InputCandidateKind.NonLocal
    };
}
