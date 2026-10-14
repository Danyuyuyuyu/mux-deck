using MuxDeck.Contracts;
using MuxDeck.Domain;

namespace MuxDeck.Application.Ports;

/// <summary>
/// 路径事实端口：边界收集完整路径判定、规范化、扩展名、所在目录与存在性，Domain 规则只消费事实。
/// 同步收集在 Infrastructure 实现同一份事实；本端口是它的可取消异步投影。
/// </summary>
public interface IPathFacts
{
    Task<EquatableArray<InputPathFacts>> CollectAsync(IReadOnlyList<InputItem> items, CancellationToken cancellationToken = default);
}
