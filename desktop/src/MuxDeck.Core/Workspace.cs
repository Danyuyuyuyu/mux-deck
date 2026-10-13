namespace MuxDeck.Core;

public sealed record Workspace(string Root)
{
    /// <summary>安装版由用户设置覆盖；未覆盖时保留工作区 bin 的便携行为。</summary>
    public string? ToolsDirectoryOverride { get; init; }
    public StoragePaths? StorageOverride { get; init; }
    /// <summary>仅供安装暂存验证指定候选后端，正常任务使用组件目录解析。</summary>
    public string? FontToolsDirectoryOverride { get; init; }
    public string ToolsDirectory => ToolsDirectoryOverride ?? Path.Combine(Root, "bin");
    public string TaskLogsDirectory => StorageOverride?.LogsDirectory ?? Path.Combine(Root, "desktop", "artifacts", "runs");
    public string TaskCacheDirectory => StorageOverride?.CacheDirectory ?? Path.Combine(Root, "desktop", "artifacts", "runs");

    public static Workspace Discover(string? explicitRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            var root = Path.GetFullPath(explicitRoot);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
            return new Workspace(root);
        }

        // Development uses the repo; portable deployments use the executable directory.
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "start_mux_ui.bat")))
                    return new Workspace(dir.FullName);

        return new Workspace(AppContext.BaseDirectory);
    }
}
