using MuxDeck.Contracts;
using MuxDeck.Domain;

namespace MuxDeck.Application.Ports;

/// <summary>
/// 固定槽位接纳的路径事实端口：按旧 ValidateCandidate 裁剪完整路径后收集存在性、扩展名与语法。
/// 同步与异步方法共用同一事实实现，批量拖入继续使用 <see cref="IPathFacts"/>。
/// </summary>
public interface ISlotPathFacts
{
    IReadOnlyList<InputPathFacts> Collect(IReadOnlyList<InputItem> items, CancellationToken cancellationToken = default);
    Task<EquatableArray<InputPathFacts>> CollectAsync(IReadOnlyList<InputItem> items, CancellationToken cancellationToken = default);
}
