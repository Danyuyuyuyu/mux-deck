namespace MuxDeck.Core.Components;

// 安装和卸载共用守卫，避免两个服务实例同时改动同一工具目录。
internal static class ComponentOperations
{
    internal static SemaphoreSlim Gate { get; } = new(1, 1);
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
