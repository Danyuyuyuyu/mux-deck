namespace MuxDeck.Core.Components;

// 过渡装配共用协调器，保持旧 Gate 跨服务实例的安装/卸载互斥范围。
internal static class ComponentOperations
{
    internal static MuxDeck.Contracts.IOperationCoordinator Coordinator { get; } = new MuxDeck.Application.OperationCoordinator();
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
