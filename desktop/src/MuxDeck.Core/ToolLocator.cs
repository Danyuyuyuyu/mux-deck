namespace MuxDeck.Core;

public sealed record ToolStatus(string Name, string Purpose, string? Path)
{
    public bool IsAvailable => Path is not null;
    public string Status => IsAvailable ? "已找到" : "未找到";
    public string Location => Path ?? "请安装组件，或将可执行文件加入 PATH";
}

public sealed class ToolLocator(Workspace workspace)
{
    public IReadOnlyList<ToolStatus> Inspect() =>
    [
        Find("mkvmerge", "封装与轨道探测", "mkvtoolnix/mkvmerge.exe", "mkvmerge"),
        Find("mkvextract", "字幕、字体与章节提取", "mkvtoolnix/mkvextract.exe", "mkvextract"),
        Find("mkvpropedit", "轨道与容器元数据修补", "mkvtoolnix/mkvpropedit.exe", "mkvpropedit"),
        Find("ffmpeg", "字幕渲染与预览", "ffmpeg/bin/ffmpeg.exe", "ffmpeg"),
        Find("AssFontSubset", "字体子集化主工具", "assfontsubset/AssFontSubset.Console.exe", "AssFontSubset.Console"),
        Find("assfonts", "字体子集化备用工具", "assfonts/assfonts.exe", "assfonts")
    ];

    private ToolStatus Find(string name, string purpose, string relative, string executable)
    {
        var bundled = Path.Combine(workspace.ToolsDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
        var path = File.Exists(bundled) ? bundled : FindOnPath(executable);
        if (path is null && OperatingSystem.IsWindows() && name.StartsWith("mkv", StringComparison.Ordinal))
        {
            var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "MKVToolNix", executable + ".exe");
            if (File.Exists(installed)) path = installed;
        }
        return new ToolStatus(name, purpose, path);
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
