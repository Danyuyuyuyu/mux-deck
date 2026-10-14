namespace MuxDeck.Infrastructure.Components;

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
