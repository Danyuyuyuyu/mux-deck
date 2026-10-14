using MuxDeck.Application.Ports;
using MuxDeck.Application.Ports.Failures;
using MuxDeck.Contracts;
using MuxDeck.Infrastructure;

namespace MuxDeck.Core.Muxing;

// 仅接线旧入口日志，不解析用例消息、不转换任务规格。
internal sealed class LegacyTaskWorkspaceFactory : ITaskWorkspaceFactory
{
    private ITaskWorkspace? current;
    public async Task<ITaskWorkspace> CreateAsync(ToolConfiguration tools, CancellationToken cancellationToken = default)
    {
        current = await new FileTaskWorkspaceFactory().CreateAsync(tools, cancellationToken).ConfigureAwait(false);
        return current;
    }
    public void Write(string value)
    {
        if (current is null) return;
        try { current.WriteLogAsync(value).GetAwaiter().GetResult(); }
        catch (ToolFailureException) { }
    }
}
