using MuxDeck.Contracts;
using MuxDeck.Domain;

namespace MuxDeck.Application.Ports;

// 工作区只在端口边界使用，不向 Desktop 的 Contracts 泄露资源生命周期。
public sealed record ToolCallContext(ToolConfiguration Tools, ITaskWorkspace Workspace);
public sealed record FontSubsetRequest(ExternalTrackSpec Subtitle, FontSpec Fonts);
public sealed record FontSubsetResult(string CorrectedSubtitlePath, EquatableArray<OutputArtifact> Fonts,
    EquatableArray<Diagnostic> Diagnostics);
public sealed record MediaMuxRequest(string VideoPath, EquatableArray<ExternalTrackSpec> Tracks,
    EquatableArray<OutputArtifact> Fonts, string TemporaryOutputPath);
public sealed record MediaMuxResult(OutputArtifact Artifact, EquatableArray<Diagnostic> Diagnostics);
public enum TrackExtractionFormat { ContainerDefault, FullRaw }
public sealed record TrackExtraction(TrackId TrackId, string DestinationPath, TrackExtractionFormat Format);
public sealed record AttachmentExtraction(int AttachmentId, string DestinationPath);
public sealed record FontInspectionRequest(string Path, int FaceIndex);
public sealed record FontInspectionResult(string Path, int FaceIndex, EquatableArray<string> Names,
    EquatableArray<int> Glyphs, bool Bold, bool Italic, int? Weight);
public enum ToolCapability { ProbeMedia, MuxMedia, ExtractMedia, SubsetFonts, FontSubsetBackend, InspectFonts }
public sealed record ToolDescriptor(ToolCapability Capability, string ExecutablePath);
