using System.Text.RegularExpressions;

namespace MuxDeck.Domain;

// 字幕文本规则只有一份：旧单任务链路与导入用例共用同一实现。
public static partial class SubtitleRules
{
    public static bool HasSrtTimeline(string text) => SrtTimeline().IsMatch(text);

    [GeneratedRegex(@"\d+:\d{2}:\d{2},\d{3}\s*-->\s*\d+:\d{2}:\d{2},\d{3}")]
    private static partial Regex SrtTimeline();
}
