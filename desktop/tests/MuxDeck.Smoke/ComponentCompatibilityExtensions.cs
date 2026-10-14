using C = MuxDeck.Contracts;
using MuxDeck.Core;
using MuxDeck.Infrastructure;
using MuxDeck.Infrastructure.Components;

internal static class ComponentCompatibilityExtensions
{
    public static Task<MuxDeck.Infrastructure.Components.ComponentInspection> VerifyAsync(this IComponentVerifier verifier, ComponentDefinition definition,
        Workspace workspace, CancellationToken cancellationToken = default) => verifier.VerifyAsync(definition,
        new C.ComponentConfiguration(workspace.Root, workspace.ToolsDirectory, workspace.TaskLogsDirectory, workspace.TaskCacheDirectory, workspace.FontToolsDirectoryOverride), cancellationToken);
}
