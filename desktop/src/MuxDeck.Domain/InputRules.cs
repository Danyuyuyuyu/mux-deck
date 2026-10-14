namespace MuxDeck.Domain;

public enum InputLocationKind { LocalFile, LocalDirectory, NonLocal, Unknown }
public enum InputKindError { None, NonLocal, WrongType }

public static class InputRules
{
    // 扩展名由边界提取；这里只筛选入口，实际媒体能力仍以探测事实为准。
    public static bool IsVideoExtension(string extension) => extension.ToLowerInvariant() is
        ".mkv" or ".mp4" or ".mov" or ".avi" or ".webm" or ".m4v" or ".ts" or ".m2ts" or ".mts" or ".mpg" or ".mpeg";
    public static bool IsAudioExtension(string extension) => extension.ToLowerInvariant() is
        ".flac" or ".aac" or ".m4a" or ".wav" or ".opus" or ".ogg" or ".mp3" or ".mp2" or ".ac3" or ".eac3" or ".dts" or ".mka" or ".w64";
    public static bool IsSubtitleExtension(string extension) => extension.ToLowerInvariant() is ".ass" or ".srt";
    public static bool IsAssExtension(string extension) => extension.Equals(".ass", StringComparison.OrdinalIgnoreCase);
    public static bool IsFontExtension(string extension) => extension.ToLowerInvariant() is ".ttf" or ".otf" or ".ttc" or ".otc";

    // 批量拖入的唯一校验顺序：数量 → 非本地 → 完整路径 → 是否存在 → 类型（逐项）→ 重复 → 字体目录。
    // 只判断事实并返回命中项，中文文案由调用边界映射。
    public static MediaBatchFailure CheckMediaBatch(IReadOnlyList<InputPathFacts> facts)
    {
        if (!AcceptsMediaSelectionCount(facts.Count)) return new(MediaBatchError.EmptySelection, null);
        if (facts.Any(item => CheckCandidateKind(item.Kind, expectsDirectory: false) == InputKindError.NonLocal))
            return new(MediaBatchError.NonLocal, null);
        foreach (var item in facts)
        {
            if (!AcceptsMediaPath(item.Path, item.IsFullyQualified)) return new(MediaBatchError.PathNotAbsolute, item.Path);
            if (!(item.Kind == InputLocationKind.LocalDirectory ? item.DirectoryExists : item.FileExists))
                return new(MediaBatchError.PathMissing, item.Path);
            if (!AcceptsMediaCandidateType(item.Kind, item.Extension)) return new(MediaBatchError.TypeUnsupported, item.Path);
        }
        if (HasDuplicatePaths(facts.Select(item => item.FullPath).ToArray())) return new(MediaBatchError.Duplicate, null);
        return new(AcceptsFontDirectories(facts.Where(item => UsesFontDirectory(item.Kind, item.Extension)).Select(item => item.Directory))
            ? MediaBatchError.None : MediaBatchError.MultipleFontDirectories, null);
    }

    public static bool IsSupportedMediaExtension(string extension) =>
        IsVideoExtension(extension) || IsAudioExtension(extension) || IsSubtitleExtension(extension) || IsFontExtension(extension);

    // 固定槽位的旧入口范围不随自动导入支持类型扩展。
    public static bool AcceptsFixedSlotExtension(string extension, bool isVideo) => isVideo
        ? extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) || extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
        : IsAssExtension(extension);

    public static InputKindError CheckCandidateKind(InputLocationKind kind, bool expectsDirectory) =>
        kind == InputLocationKind.NonLocal ? InputKindError.NonLocal :
        kind == (expectsDirectory ? InputLocationKind.LocalDirectory : InputLocationKind.LocalFile)
            ? InputKindError.None : InputKindError.WrongType;

    public static bool AcceptsMediaSelectionCount(int count) => count > 0;

    public static bool AcceptsSelectionCount(int count, bool subtitleSlots) =>
        subtitleSlots ? count is >= 1 and <= 2 : count == 1;

    public static bool IsSubtitleSlot(int slot) => slot is 0 or 1;

    public static bool IsSubtitleSlotAvailable(int slot, IEnumerable<int> assignedSlots) =>
        IsSubtitleSlot(slot) && !assignedSlots.Contains(slot);

    // 完整路径、无效字符集合和规范化由运行边界提供，不在 Domain 仿造 Path 的平台语义。
    public static bool AcceptsMediaPath(string path, bool isFullyQualified) =>
        isFullyQualified && !path.Contains("://", StringComparison.Ordinal);

    public static bool AcceptsFixedSlotPath([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? path, bool isFullyQualified) =>
        !string.IsNullOrWhiteSpace(path) && AcceptsMediaPath(path, isFullyQualified) &&
        !path.StartsWith(@"\\?\", StringComparison.Ordinal) && !path.StartsWith(@"\\.\", StringComparison.Ordinal);

    public static bool AcceptsNormalizedPathSyntax(string path, bool containsInvalidPathChars) =>
        !containsInvalidPathChars && !path.AsSpan(2).Contains(':');

    public static bool SameNormalizedPath(string left, string right, bool ignoreCase) =>
        string.Equals(left, right, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static bool HasDuplicatePaths(IEnumerable<string> normalizedPaths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return normalizedPaths.Any(path => !seen.Add(path));
    }

    public static bool SubtitlePathsConflict(string sc, string tc, bool samePath) =>
        !string.IsNullOrWhiteSpace(sc) && !string.IsNullOrWhiteSpace(tc) && samePath;

    public static bool RequiresReplacement(string currentPath, bool samePath) =>
        !string.IsNullOrWhiteSpace(currentPath) && !samePath;

    public static bool AcceptsFontDirectories(IEnumerable<string> normalizedDirectories) =>
        normalizedDirectories.Distinct(StringComparer.OrdinalIgnoreCase).Count() <= 1;

    public static bool AcceptsMediaCandidateType(InputLocationKind kind, string extension) =>
        kind != InputLocationKind.LocalFile || IsSupportedMediaExtension(extension);

    public static bool UsesFontDirectory(InputLocationKind kind, string extension) =>
        kind == InputLocationKind.LocalDirectory || IsFontExtension(extension);

    public static bool IsSourceVideo(bool hasVideoTrack, bool isVideoExtension) => hasVideoTrack && isVideoExtension;

    public static bool AcceptsSourceVideoCount(int count) => count <= 1;

    public static bool AcceptsAudioProbe(int audioTrackCount, bool isAudioExtension, bool hasVideoTrack) =>
        audioTrackCount > 0 && (isAudioExtension || !hasVideoTrack);

    public static bool AcceptsSubtitleProbe(int subtitleTrackCount, string? codecId, string codec, bool isAss)
    {
        if (subtitleTrackCount != 1) return false;
        if (codecId is not null) return codecId == (isAss ? "S_TEXT/ASS" : "S_TEXT/UTF8");
        return isAss
            ? codec.Contains("ASS", StringComparison.OrdinalIgnoreCase) || codec.Contains("SubStation", StringComparison.OrdinalIgnoreCase)
            : codec.Contains("SRT", StringComparison.OrdinalIgnoreCase) || codec.Contains("SubRip", StringComparison.OrdinalIgnoreCase);
    }

    // 一个音频容器的不同真实轨号可分别导入；已存在字幕按整个文件身份去重。
    public static bool ImportedTrackConflicts(bool samePath, bool existingIsSubtitle, int existingTrackId, int candidateTrackId) =>
        samePath && (existingIsSubtitle || existingTrackId == candidateTrackId);
}
