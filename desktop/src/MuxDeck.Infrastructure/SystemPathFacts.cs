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
        var directory = item.Kind == InputLocationKind.LocalDirectory
            ? Normalize(item.Path) : Normalize(Path.GetDirectoryName(item.Path) ?? "");
        return new(item.Path, Path.IsPathFullyQualified(item.Path), Normalize(item.Path), Path.GetExtension(item.Path),
            directory, item.Kind, File.Exists(item.Path), Directory.Exists(item.Path),
            item.Path.IndexOfAny(Path.GetInvalidPathChars()) >= 0);
    }

    public Task<EquatableArray<InputPathFacts>> CollectAsync(IReadOnlyList<InputItem> items, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new EquatableArray<InputPathFacts>([.. Collect(items)]));
    }

    // 规范化只在事实被使用时才有意义（重复与字体目录），失败的路径保持原样，交给同一批规则报错。
    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException or System.Security.SecurityException)
        { return path; }
    }
}
