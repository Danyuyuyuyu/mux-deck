namespace MuxDeck.Core.Components;

// 4a-2 前保留旧入口共享桥接，实例由外部装配提供，Core 不再创建协调器。
internal static class ComponentOperations
{
    private static MuxDeck.Contracts.IOperationCoordinator? coordinator;
    internal static MuxDeck.Contracts.IOperationCoordinator Coordinator => coordinator ??
        throw new InvalidOperationException("组件协调器尚未装配。");
    internal static void Configure(MuxDeck.Contracts.IOperationCoordinator value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Interlocked.CompareExchange(ref coordinator, value, null) is { } existing && !ReferenceEquals(existing, value))
            throw new InvalidOperationException("组件协调器已装配，不能替换。");
    }
}

public enum ComponentRemovalKind { Component, Dependencies }
public sealed record ComponentRemovalPlan(string ComponentId, ComponentRemovalKind Kind, string ToolsDirectory, string TargetDirectory);
public sealed record ComponentRemovalProgress(string Message);
public sealed record ComponentRemovalResult(string Directory, string LogPath);
public sealed class ComponentRemovalException(string message, string logPath, Exception? inner = null) : IOException(message, inner)
{
    public string LogPath { get; } = logPath;
}
public interface IComponentUninstaller
{
    Task<ComponentRemovalResult> UninstallAsync(ComponentRemovalPlan plan, StoragePaths storage, IProgress<ComponentRemovalProgress> progress, CancellationToken cancellationToken);
}
