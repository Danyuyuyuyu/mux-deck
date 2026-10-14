namespace MuxDeck.Contracts;

/// <summary>集中保存任务错误与过渡检查码；工具诊断码沿用 diagnostics.md，不在这里另立白名单。</summary>
public static class ErrorCodes
{

    public const string ComponentOperationBusy = "MUX_COMPONENT_OPERATION_BUSY";
    public const string ComponentInstallFailed = "MUX_COMPONENT_INSTALL_FAILED";
    public const string ComponentUninstallFailed = "MUX_COMPONENT_UNINSTALL_FAILED";
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
    // 拖入识别的结构化失败码：界面按键映射文案，不解析文案本身。
    public const string ImportEmptySelection = "MUX_IMPORT_EMPTY_SELECTION";
    public const string ImportNonLocal = "MUX_IMPORT_NON_LOCAL";
    public const string ImportPathNotAbsolute = "MUX_IMPORT_PATH_NOT_ABSOLUTE";
    public const string ImportPathMissing = "MUX_IMPORT_PATH_MISSING";
    public const string ImportTypeUnsupported = "MUX_IMPORT_TYPE_UNSUPPORTED";
    public const string ImportDuplicate = "MUX_IMPORT_DUPLICATE";
    public const string ImportFontDirectories = "MUX_IMPORT_FONT_DIRECTORIES";
    public const string ImportSourceVideoCount = "MUX_IMPORT_SOURCE_VIDEO_COUNT";
    public const string ImportAudioMissing = "MUX_IMPORT_AUDIO_MISSING";
    public const string ImportSubtitleMismatch = "MUX_IMPORT_SUBTITLE_MISMATCH";
    public const string ImportTrackConflict = "MUX_IMPORT_TRACK_CONFLICT";
    public const string ImportSubtitleTimeline = "MUX_IMPORT_SUBTITLE_TIMELINE";
}

/// <summary>Domain 批量校验结果到契约错误码的唯一映射，生产与消费两侧共用。</summary>
public static class MediaBatchCodes
{
    public static string? For(Domain.MediaBatchError error) => error switch
    {
        Domain.MediaBatchError.None => null,
        Domain.MediaBatchError.EmptySelection => ErrorCodes.ImportEmptySelection,
        Domain.MediaBatchError.NonLocal => ErrorCodes.ImportNonLocal,
        Domain.MediaBatchError.PathNotAbsolute => ErrorCodes.ImportPathNotAbsolute,
        Domain.MediaBatchError.PathMissing => ErrorCodes.ImportPathMissing,
        Domain.MediaBatchError.TypeUnsupported => ErrorCodes.ImportTypeUnsupported,
        Domain.MediaBatchError.Duplicate => ErrorCodes.ImportDuplicate,
        Domain.MediaBatchError.MultipleFontDirectories => ErrorCodes.ImportFontDirectories,
        _ => ErrorCodes.UnexpectedFailure
    };
}
