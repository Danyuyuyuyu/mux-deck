using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using MuxDeck.Domain;

namespace MuxDeck.Infrastructure;

/// <summary>
/// 路径事实的唯一实现：同步收集供同步拖入反馈使用，异步端口直接包装同一同步实现。
/// </summary>
public sealed class SystemPathFacts : IPathFacts, ISyncPathFacts
{
    public static SystemPathFacts Instance { get; } = new();

    public IReadOnlyList<InputPathFacts> Collect(IReadOnlyList<InputItem> items) => [.. items.Select(Collect)];

    IReadOnlyList<InputPathFacts> ISyncPathFacts.Collect(IReadOnlyList<InputItem> items, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return CollectForFixedSlot(items);
    }

    public InputPathFacts Collect(InputItem item)
    {
        string? failureDetail = null;
        var directoryInput = item.Kind == InputLocationKind.LocalDirectory
            ? item.Path : Path.GetDirectoryName(item.Path) ?? "";
        // 旧 VM 只对候选路径本身调用 GetFullPath；所在目录失败时保留原始值，不算候选路径无效。
        var directory = NormalizeDirectory(directoryInput);
        var normalized = Normalize(item.Path, ref failureDetail);
        // 旧实现是在 GetFullPath 规范化之后判断非法字符；这里对同一个规范化结果判断，保持等价。
        return new(item.Path, Path.IsPathFullyQualified(item.Path), normalized, Path.GetExtension(item.Path),
            directory, item.Kind, File.Exists(item.Path), Directory.Exists(item.Path),
            normalized.IndexOfAny(Path.GetInvalidPathChars()) >= 0, failureDetail);
    }

    public Task<EquatableArray<InputPathFacts>> CollectAsync(IReadOnlyList<InputItem> items, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new EquatableArray<InputPathFacts>([.. Collect(items)]));
    }

    /// <summary>
    /// 固定槽位接纳事实：与旧 ValidateCandidate 一致，先 GetFullPath 再裁剪末尾分隔符，
    /// 存在性、扩展名与非法字符都基于裁剪后的路径；异常集合以旧 catch 为准。
    /// </summary>
    public IReadOnlyList<InputPathFacts> CollectForFixedSlot(IReadOnlyList<InputItem> items) =>
        [.. items.Select(CollectForFixedSlot)];

    public InputPathFacts CollectForFixedSlot(InputItem item)
    {
        // 完整路径判定在旧代码中位于 GetFullPath 之前，失败回退时也要保留原判定。
        var fullyQualified = Path.IsPathFullyQualified(item.Path);
        try
        {
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(item.Path));
            var directory = item.Kind == InputLocationKind.LocalDirectory
                ? normalized : Path.GetDirectoryName(normalized) ?? "";
            return new(item.Path, fullyQualified, normalized, Path.GetExtension(normalized), directory,
                item.Kind, File.Exists(normalized), Directory.Exists(normalized),
                normalized.IndexOfAny(Path.GetInvalidPathChars()) >= 0, null);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return new(item.Path, fullyQualified, item.Path, "", "", item.Kind, false, false, false, exception.Message);
        }
    }

    public Task<EquatableArray<InputPathFacts>> CollectSlotAsync(IReadOnlyList<InputItem> items, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new EquatableArray<InputPathFacts>([.. CollectForFixedSlot(items)]));
    }

    private static string NormalizeDirectory(string path)
    {
        string? ignored = null;
        return Normalize(path, ref ignored);
    }

    private static string Normalize(string path, ref string? failureDetail)
    {
        try { return Path.GetFullPath(path); }
        catch (ArgumentException exception) { failureDetail ??= exception.Message; return path; }
        catch (NotSupportedException exception) { failureDetail ??= exception.Message; return path; }
        catch (PathTooLongException exception) { failureDetail ??= exception.Message; return path; }
        catch (IOException exception) { failureDetail ??= exception.Message; return path; }
        catch (System.Security.SecurityException exception) { failureDetail ??= exception.Message; return path; }
    }
}
