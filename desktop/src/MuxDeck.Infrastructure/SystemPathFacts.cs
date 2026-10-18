using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using MuxDeck.Domain;

namespace MuxDeck.Infrastructure;

/// <summary>
/// 路径事实的唯一实现：同步收集供同步拖入反馈使用，异步端口直接包装同一同步实现。
/// </summary>
public sealed class SystemPathFacts : IPathFacts
{
    public static SystemPathFacts Instance { get; } = new();

    public IReadOnlyList<InputPathFacts> Collect(IReadOnlyList<InputItem> items) => [.. items.Select(Collect)];

    public InputPathFacts Collect(InputItem item)
    {
        string? failureDetail = null;
        var directoryInput = item.Kind == InputLocationKind.LocalDirectory
            ? item.Path : Path.GetDirectoryName(item.Path) ?? "";
        var directory = Normalize(directoryInput, ref failureDetail);
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
