namespace MuxDeck.Infrastructure;

public static class ToolPathLookup
{
    public static string Executable(string name) => name + (OperatingSystem.IsWindows() ? ".exe" : "");
    public static string? Find(string tools, string relative, string executable) =>
        Find(tools, relative, executable, out _);

    // 定位与来源同时返回，旧显示入口不再重复查询 PATH 或系统安装目录。
    public static string? Find(string tools, string relative, string executable, out string source)
    {
        var bundled = Path.Combine(tools, relative.Replace('/', Path.DirectorySeparatorChar));
        var bundledExists = File.Exists(bundled);
        var path = bundledExists ? bundled : FindOnPath(executable);
        source = bundledExists ? "Mux Deck" : path is not null ? "PATH" : "未找到";
        if (path is null && OperatingSystem.IsWindows() && executable.StartsWith("mkv", StringComparison.Ordinal))
        {
            var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MKVToolNix", executable + ".exe");
            if (File.Exists(installed)) { path = installed; source = "系统"; }
        }
        return path;
    }

    public static string? FindOnPath(string executable)
    {
        var file = executable + (OperatingSystem.IsWindows() ? ".exe" : "");
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            try
            {
                var candidate = Path.GetFullPath(Path.Combine(directory.Trim('"'), file));
                if (File.Exists(candidate)) return candidate;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { }
        }
        return null;
    }
}
