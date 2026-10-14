using MuxDeck.Domain;

namespace MuxDeck.Contracts;

public enum MuxJobStatus { Succeeded, Failed, Cancelled }
public enum CheckStatus { NotEvaluated, Passed, Failed }
public enum DiagnosticSeverity { Information, Warning, Error }
public enum JobStage { Unknown, Validating, Probing, Subsetting, Planning, Muxing, Checking, Publishing, Completed }

public sealed record CheckResult(string Code, CheckStatus Status, string? Subject,
    string? Expected, string? Actual);
public sealed record JobFailure(string Code, JobStage Stage, string? Message, string? Detail = null);
public sealed record Diagnostic(string Code, DiagnosticSeverity Severity, string Message, int Occurrences = 1);
public sealed record OutputArtifact(string Path);
public sealed record LogRef(string Path);
public sealed record JobProgress(JobStage Stage, string StepCode, double? Percent, string Message)
{
    // 阶段事实供兼容入口转换文案，不在用例内重复保存旧文本。
    public string? Detail { get; init; }
}
public sealed record MuxJobResult(MuxJobStatus Status, EquatableArray<OutputArtifact> Outputs,
    EquatableArray<CheckResult> Checks, EquatableArray<Diagnostic> Diagnostics,
    JobFailure? Failure, LogRef? Log);
