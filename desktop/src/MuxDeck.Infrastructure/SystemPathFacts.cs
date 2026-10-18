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
        try
        {
            var normalized = Path.GetFullPath(item.Path);
            var directory = item.Kind == InputLocationKind.LocalDirectory
                ? normalized : Path.GetFullPath(Path.GetDirectoryName(item.Path) ?? "");
            // 旧实现是在 GetFullPath 规范化之后判断非法字符；这里对同一个规范化结果判断，保持等价。
            return new(item.Path, Path.IsPathFullyQualified(item.Path), normalized, Path.GetExtension(item.Path),
                directory, item.Kind, File.Exists(item.Path), Directory.Exists(item.Path),
                normalized.IndexOfAny(Path.GetInvalidPathChars()) >= 0, null);
        }
        catch (ArgumentException exception) { return Failed(item, exception); }
        catch (IOException exception) { return Failed(item, exception); }
        catch (NotSupportedException exception) { return Failed(item, exception); }
        catch (UnauthorizedAccessException exception) { return Failed(item, exception); }
    }

    public Task<EquatableArray<InputPathFacts>> CollectAsync(IReadOnlyList<InputItem> items, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new EquatableArray<InputPathFacts>([.. Collect(items)]));
    }

    private static InputPathFacts Failed(InputItem item, Exception exception) =>
        new(item.Path, false, item.Path, "", "", item.Kind, false, false, false, exception.Message);
}
