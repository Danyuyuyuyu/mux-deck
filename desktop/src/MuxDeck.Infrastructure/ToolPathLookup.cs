namespace MuxDeck.Infrastructure;

public static class ToolPathLookup
{
    public static string Executable(string name) => name + (OperatingSystem.IsWindows() ? ".exe" : "");
    public static string? Find(string tools, string relative, string executable)
    {
        var bundled = Path.Combine(tools, relative.Replace('/', Path.DirectorySeparatorChar));
        var path = File.Exists(bundled) ? bundled : FindOnPath(executable);
        if (path is null && OperatingSystem.IsWindows() && executable.StartsWith("mkv", StringComparison.Ordinal))
        {
            var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MKVToolNix", executable + ".exe");
            if (File.Exists(installed)) path = installed;
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
