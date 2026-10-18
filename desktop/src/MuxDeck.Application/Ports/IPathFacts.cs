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

    /// <summary>
    /// 固定槽位接纳使用的事实：旧 VM 先裁剪完整路径，再据其判断存在性、扩展名与语法。
    /// 默认回退到批量事实，只有真实边界（SystemPathFacts）覆写为固定槽位语义。
    /// </summary>
    Task<EquatableArray<InputPathFacts>> CollectSlotAsync(IReadOnlyList<InputItem> items, CancellationToken cancellationToken = default) =>
        CollectAsync(items, cancellationToken);
}
