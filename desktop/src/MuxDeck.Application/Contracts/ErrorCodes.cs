namespace MuxDeck.Contracts;

/// <summary>集中保存任务错误与过渡检查码；工具诊断码沿用 diagnostics.md，不在这里另立白名单。</summary>
public static class ErrorCodes
{

    public const string LegacyCheck = "MUX_LEGACY_CHECK";
    public const string LegacyFailure = "MUX_LEGACY_FAILURE";
    public const string Cancelled = "MUX_CANCELLED";
    public const string ToolNotFound = "MUX_TOOL_NOT_FOUND";
    public const string ToolTimeout = "MUX_TOOL_TIMEOUT";
    public const string ToolExitFailed = "MUX_TOOL_EXIT_FAILED";
    public const string ToolOutputInvalid = "MUX_TOOL_OUTPUT_INVALID";
    public const string ToolFatalDiagnostic = "MUX_TOOL_FATAL_DIAGNOSTIC";
    public const string TargetAlreadyExists = "MUX_TARGET_ALREADY_EXISTS";
    public const string WorkspaceIoFailed = "MUX_WORKSPACE_IO_FAILED";
    public const string InputEmpty = "MUX_INPUT_EMPTY";
    public const string VideoMissing = "MUX_VIDEO_MISSING";
    public const string FontsDirectoryMissing = "MUX_FONTS_DIRECTORY_MISSING";
    public const string OutputDirectoryInvalid = "MUX_OUTPUT_DIRECTORY_INVALID";
    public const string OutputNameInvalid = "MUX_OUTPUT_NAME_INVALID";
    public const string OutputOverwritesSource = "MUX_OUTPUT_OVERWRITES_SOURCE";
    public const string SubtitleInputInvalid = "MUX_SUBTITLE_INPUT_INVALID";
    public const string SubtitleLanguageInvalid = "MUX_SUBTITLE_LANGUAGE_INVALID";
    public const string SubtitleNameInvalid = "MUX_SUBTITLE_NAME_INVALID";
    public const string AudioInputInvalid = "MUX_AUDIO_INPUT_INVALID";
    public const string AudioMetadataInvalid = "MUX_AUDIO_METADATA_INVALID";
    public const string SourceVideoMissing = "MUX_SOURCE_VIDEO_MISSING";
    public const string SourceAudioMissing = "MUX_SOURCE_AUDIO_MISSING";
    public const string PlanInvalid = "MUX_PLAN_INVALID";
    // 保留旧公开码及其原有验证；新用例的轨道 QC 只生成下方各类独立码。
    public const string TrackQcFailed = "MUX_TRACK_QC_FAILED";
    public const string VideoCountMismatch = "MUX_VIDEO_COUNT_MISMATCH";
    public const string AudioCountMismatch = "MUX_AUDIO_COUNT_MISMATCH";
    public const string VideoMetadataMismatch = "MUX_VIDEO_METADATA_MISMATCH";
    public const string AudioMetadataMismatch = "MUX_AUDIO_METADATA_MISMATCH";
    public const string DurationMismatch = "MUX_DURATION_MISMATCH";
    public const string SubtitleCountMismatch = "MUX_SUBTITLE_COUNT_MISMATCH";
    public const string SubtitleMetadataMismatch = "MUX_SUBTITLE_METADATA_MISMATCH";
    public const string AddedAudioMetadataMismatch = "MUX_ADDED_AUDIO_METADATA_MISMATCH";
    public const string FontBackendNotFound = "MUX_FONT_BACKEND_NOT_FOUND";
    public const string AttachmentCountInvalid = "MUX_ATTACHMENT_COUNT_INVALID";
    public const string AttachmentMetadataInvalid = "MUX_ATTACHMENT_METADATA_INVALID";
    public const string SubtitleReferenceInvalid = "MUX_SUBTITLE_REFERENCE_INVALID";
    public const string SubtitleTextMismatch = "MUX_SUBTITLE_TEXT_MISMATCH";
    public const string AttachmentPayloadMismatch = "MUX_ATTACHMENT_PAYLOAD_MISMATCH";
    public const string AudioPayloadMismatch = "MUX_AUDIO_PAYLOAD_MISMATCH";
    public const string SourceSelectionUnsupported = "MUX_SOURCE_SELECTION_UNSUPPORTED";
    public const string UnexpectedFailure = "MUX_UNEXPECTED_FAILURE";
    public const string SubtitleTextInvalid = "MUX_SUBTITLE_TEXT_INVALID";
}
