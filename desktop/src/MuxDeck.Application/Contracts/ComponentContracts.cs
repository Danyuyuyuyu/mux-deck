using MuxDeck.Domain;

namespace MuxDeck.Contracts;

public enum ComponentState { Unknown, Found, Checking, Missing, Partial, Available, Invalid, DependencyMissing }
public enum ComponentPhase { Idle, Preparing, Downloading, Installing, Verifying, Uninstalling, Completed, Failed, Cancelled }
public enum ComponentOperationStatus { Succeeded, Failed, Cancelled }
public enum ComponentRemovalKind { Component, Dependencies }
public enum ComponentPartPurpose { Unknown, PackagingAndProbe, SubtitleFontChapterExtraction, TrackMetadataRepair, SubtitleRenderPreview, PrimaryFontSubsetting, FallbackFontSubsetting }
public enum ComponentPartSource { Missing, MuxDeck, Path, System }
public sealed record ComponentConfiguration(string WorkspaceRoot, string ToolsDirectory,
    string LogsDirectory, string CacheDirectory, string? FontToolsDirectoryOverride = null);
public sealed record ComponentPart(string Name, string? Path, ComponentState State, string? Reason,
    ComponentPartPurpose Purpose = ComponentPartPurpose.Unknown, ComponentPartSource Source = ComponentPartSource.Missing);
public sealed record ComponentDirectoryQuery(string ComponentId, ComponentRemovalKind Kind, string ToolsDirectory, string TargetDirectory);
public sealed record ComponentInspection(string Id, ComponentState State, EquatableArray<ComponentPart> Parts,
    string? Version = null, string? Reason = null);
public sealed record ComponentProgress(ComponentPhase Phase, string Message, long Bytes = 0, long? TotalBytes = null);
public sealed record ComponentUninstallPlan(string ComponentId, ComponentRemovalKind Kind,
    string ToolsDirectory, string TargetDirectory);
public sealed record ComponentOperationResult(ComponentOperationStatus Status, string? Directory,
    LogRef? Log, OperationFailure? Failure);
