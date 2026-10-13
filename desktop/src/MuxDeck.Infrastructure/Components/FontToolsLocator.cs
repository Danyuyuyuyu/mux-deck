using MuxDeck.Contracts;
namespace MuxDeck.Infrastructure.Components;

public static class FontToolsLocator
{
    public static string? Find(ToolConfiguration workspace)
    {
        // 暂存验证必须验证候选依赖，不能被本机已安装后端掩盖。
        if (workspace.FontToolsDirectoryOverride is { } candidate) return HasTools(candidate) ? candidate : null;
        var candidates = new List<string>
        {
            Path.Combine(workspace.ToolsDirectory, "fonttools", "Scripts"),
            Path.Combine(workspace.ToolsDirectory, "assfontsubset", "python", "Scripts")
        };
        var subset = ToolPathLookup.FindOnPath("pyftsubset");
        if (subset is not null) candidates.Add(Path.GetDirectoryName(subset)!);
        var python = ToolPathLookup.FindOnPath("python");
        if (python is not null) candidates.Add(Path.Combine(Path.GetDirectoryName(python)!, "Scripts"));
        candidates.Add(Path.Combine(workspace.ToolsDirectory, "python", "Scripts"));
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python");
        if (Directory.Exists(installed)) candidates.AddRange(Directory.EnumerateDirectories(installed).OrderDescending().Select(p => Path.Combine(p, "Scripts")));
        return candidates.Distinct().FirstOrDefault(HasTools);
    }

    private static bool HasTools(string directory) => File.Exists(Path.Combine(directory, Executable("pyftsubset"))) && File.Exists(Path.Combine(directory, Executable("ttx")));
    private static string Executable(string name) => name + (OperatingSystem.IsWindows() ? ".exe" : "");
}
