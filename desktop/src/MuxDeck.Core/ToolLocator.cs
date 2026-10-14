using MuxDeck.Infrastructure;
namespace MuxDeck.Core;

public sealed record ToolStatus(string Name, string Purpose, string? Path)
{
    public bool IsAvailable => Path is not null;
    public string Status => IsAvailable ? "已找到" : "未找到";
    public string Location => Path ?? "请安装组件，或将可执行文件加入 PATH";
    public string Source { get; init; } = "未找到";
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
        var path = ToolPathLookup.Find(workspace.ToolsDirectory, relative, executable, out var source);
        return new ToolStatus(name, purpose, path) { Source = source };
    }

    public static string? FindOnPath(string executable) => ToolPathLookup.FindOnPath(executable);
}
