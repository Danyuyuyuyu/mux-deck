using MuxDeck.Contracts;
using MuxDeck.Domain;

namespace MuxDeck.Application.Ports;

/// <summary>
/// 固定槽位接纳的同步路径事实端口：与旧 ValidateCandidate 一样按裁剪后的完整路径计算存在性、扩展名与语法。
/// 拖入反馈继续使用 <see cref="IPathFacts"/> 的批量语义，两者不混用。
/// </summary>
public interface ISyncPathFacts
{
    IReadOnlyList<InputPathFacts> Collect(IReadOnlyList<InputItem> items, CancellationToken cancellationToken = default);
}
