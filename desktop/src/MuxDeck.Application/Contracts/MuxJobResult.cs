using MuxDeck.Domain;

namespace MuxDeck.Contracts;

public enum MuxJobStatus { Succeeded, Failed, Cancelled }
public enum CheckStatus { NotEvaluated, Passed, Failed }
public enum DiagnosticSeverity { Information, Warning, Error }
public enum JobStage { Unknown, Validating, Probing, Subsetting, Planning, Muxing, Checking, Publishing, Completed }

// 检查事实不编码成字符串；序号从 1 开始，轨道 ID 保留探测结果的真实值。
public sealed record CheckResult(string Code, CheckStatus Status)
{
    public int? Sequence { get; init; }
    public int? Count { get; init; }
    public TrackId? TrackId { get; init; }
    public string? Name { get; init; }
    public LanguageTag? Language { get; init; }
    public LanguageTag? LanguageIetf { get; init; }
    public TrackFlags? Flags { get; init; }
    public string? ExpectedHash { get; init; }
    public string? ActualHash { get; init; }
    // 仅旧结果向新契约转换时保留无法还原成事实的检查原文。
    public string? LegacyText { get; init; }
}
public sealed record JobFailure(string Code, JobStage Stage, string? Message, string? Detail = null)
{
    public int? Sequence { get; init; }
    public string? Path { get; init; }
}
public sealed record Diagnostic(string Code, DiagnosticSeverity Severity, string Message, int Occurrences = 1);
public sealed record OutputArtifact(string Path);
public sealed record LogRef(string Path);
public sealed record JobProgress(JobStage Stage, string StepCode, double? Percent, string? Message)
{
    public int? Sequence { get; init; }
    public int? Total { get; init; }
    public EquatableArray<Diagnostic> Diagnostics { get; init; }
}
public sealed record MuxJobResult(MuxJobStatus Status, EquatableArray<OutputArtifact> Outputs,
    EquatableArray<CheckResult> Checks, EquatableArray<Diagnostic> Diagnostics,
    JobFailure? Failure, LogRef? Log);
