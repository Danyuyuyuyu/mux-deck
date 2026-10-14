using MuxDeck.Contracts;

namespace MuxDeck.Desktop.ViewModels;

public sealed record EnvironmentStorage(string AppDataRoot, string ToolsDirectory, string ConfigDirectory,
    string LogsDirectory, string CacheDirectory);

public sealed record EnvironmentToolStatus(string Name, string Purpose, string? Path, string Source = "未找到")
{
    public bool IsAvailable => Path is not null;
    public string Status => IsAvailable ? "已找到" : "未找到";
    public string Location => Path ?? "请安装组件，或将可执行文件加入 PATH";
}

public sealed record EnvironmentComponentDefinition(string Id, string Name, string Description, bool IsCore,
    string[] ToolNames, string GuideUrl);
