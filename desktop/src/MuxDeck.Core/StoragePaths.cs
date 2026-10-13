namespace MuxDeck.Core;

/// <summary>
/// Mux Deck 的持久化目录布局。工具目录与工作目录分开，便携版仍可使用工作区内的 bin/data。
/// </summary>
public sealed record StoragePaths(
    string AppDataRoot,
    string ToolsDirectory,
    string ConfigDirectory,
    string LogsDirectory,
    string CacheDirectory)
{
    public static StoragePaths Default
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root)) root = AppContext.BaseDirectory;
            root = Path.Combine(root, "MuxDeck");
            return ForRoot(root, Path.Combine(root, "tools"));
        }
    }

    public static StoragePaths ForWorkspace(string workspaceRoot)
    {
        var root = Path.Combine(Path.GetFullPath(workspaceRoot), "data");
        return ForRoot(root, Path.Combine(Path.GetFullPath(workspaceRoot), "bin"));
    }

    public static StoragePaths ForRoot(string root, string toolsDirectory)
    {
        root = Path.GetFullPath(root);
        return new StoragePaths(root, Path.GetFullPath(toolsDirectory),
            Path.Combine(root, "config"), Path.Combine(root, "logs"), Path.Combine(root, "cache"));
    }

    public void EnsureDirectories()
    {
        foreach (var path in new[] { AppDataRoot, ToolsDirectory, ConfigDirectory, LogsDirectory, CacheDirectory })
            Directory.CreateDirectory(path);
    }
}
