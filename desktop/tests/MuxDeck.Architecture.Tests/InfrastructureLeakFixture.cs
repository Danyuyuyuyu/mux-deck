namespace MuxDeck.Desktop.Environment;

// 故意放在 Desktop 命名空间、装配目录之外，并真实调用 Infrastructure，用于确认护栏会拒绝。
internal sealed class DesktopEnvironmentReader
{
    public int Count(MuxDeck.Contracts.ComponentConfiguration configuration) =>
        new MuxDeck.Infrastructure.ToolLocator(configuration).Inspect().Count;
}
