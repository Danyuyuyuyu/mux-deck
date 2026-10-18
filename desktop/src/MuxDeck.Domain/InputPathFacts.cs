namespace MuxDeck.Domain;

/// <summary>批量输入的命中项；只携带事实，不携带任何显示文案。</summary>
public enum MediaBatchError
{
    None, EmptySelection, NonLocal, PathNotAbsolute, PathMissing, TypeUnsupported, Duplicate, MultipleFontDirectories
}

/// <summary>一次批量校验的结果：命中的错误与出错路径（用于界面拼出原始文案）。</summary>
public sealed record MediaBatchFailure(MediaBatchError Error, string? Path)
{
    public bool Succeeded => Error == MediaBatchError.None;
}

/// <summary>
/// 单个拖入候选的路径事实。由边界收集（完整路径判定、规范化、扩展名、所在目录、存在性）；
/// 路径本身规范化失败时 FailureDetail 携带边界异常信息，其他字段为安全回退值。
/// Domain 只消费事实，不触碰 System.IO。
/// </summary>
public sealed record InputPathFacts(
    string Path,
    bool IsFullyQualified,
    string FullPath,
    string Extension,
    string Directory,
    InputLocationKind Kind,
    bool FileExists,
    bool DirectoryExists,
    bool ContainsInvalidPathChars,
    string? FailureDetail);
