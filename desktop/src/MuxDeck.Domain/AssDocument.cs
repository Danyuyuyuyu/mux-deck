using System.Text;
using System.Text.RegularExpressions;

namespace MuxDeck.Domain;

public static partial class AssDocument
{
    public sealed record FontFace(string Name, bool Bold, bool Italic);
    public sealed record FontUsage(FontFace Face, EquatableArray<int> Glyphs);
    public sealed record FontMapping(string Token, string OriginalName);

    public static bool HasRequiredSections(string text) =>
        text.Contains("[Script Info]", StringComparison.OrdinalIgnoreCase) &&
        text.Contains("[V4+ Styles]", StringComparison.OrdinalIgnoreCase) &&
        text.Contains("[Events]", StringComparison.OrdinalIgnoreCase);

    public static EquatableArray<FontUsage> ReadUsage(string text)
    {
        var styles = new Dictionary<string, FontFace>(StringComparer.OrdinalIgnoreCase);
        var usage = new Dictionary<FontFace, HashSet<int>>();
        var section = "";
        string[] format = [];
        var dialogueCount = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { section = line; format = []; continue; }
            if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
                format = line[7..].Split(',').Select(x => x.Trim()).ToArray();
            else if (section.Equals("[V4+ Styles]", StringComparison.OrdinalIgnoreCase) &&
                     line.StartsWith("Style:", StringComparison.OrdinalIgnoreCase))
            {
                var values = line[6..].Split(',');
                styles[Field("Name", format, values)] = new FontFace(
                    Field("Fontname", format, values).TrimStart('@').ToUpperInvariant(),
                    Field("Bold", format, values) != "0", Field("Italic", format, values) != "0");
            }
            else if (section.Equals("[Events]", StringComparison.OrdinalIgnoreCase) &&
                     line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
            {
                dialogueCount++;
                var values = line[9..].TrimStart().Split(',', format.Length);
                var style = Field("Style", format, values);
                if (!styles.TryGetValue(style, out var baseFont))
                    throw new MediaRuleException($"字幕使用了未定义样式：{style}");
                var font = baseFont;
                var drawing = false;
                foreach (Match part in Parts().Matches(Field("Text", format, values)))
                {
                    var value = part.Value;
                    if (value.StartsWith('{'))
                    {
                        // 首阶段不猜测动画中字体切换的时间范围，避免漏掉初态字形。
                        if (Regex.IsMatch(value, @"\\t\([^)]*\\(?:fn|r|b-?\d*(?=\\|\))|i-?\d*(?=\\|\)))"))
                            throw new MediaRuleException("首阶段尚不支持 \\t 动画中的字体/粗斜体切换。");
                        foreach (Match tag in Tags().Matches(value[1..^1]))
                        {
                            if (tag.Groups[1].Success)
                            {
                                var name = tag.Groups[1].Value.Trim().TrimStart('@');
                                font = font with { Name = name.Length == 0 ? baseFont.Name : name.ToUpperInvariant() };
                            }
                            else if (tag.Groups[2].Success)
                            {
                                var reset = tag.Groups[2].Value.Trim();
                                font = reset.Length == 0 ? baseFont : styles.GetValueOrDefault(reset)
                                    ?? throw new MediaRuleException($"字幕覆盖样式不存在：{reset}");
                                drawing = false;
                            }
                            else if (tag.Groups[3].Success) drawing = tag.Groups[3].Value != "0";
                            else if (tag.Groups[4].Success)
                            {
                                var weight = tag.Groups[4].Value;
                                if (weight is not ("" or "0" or "1" or "-1" or "400" or "700"))
                                    throw new MediaRuleException("首阶段尚不支持非标准 ASS 字重：" + weight);
                                font = font with { Bold = weight.Length == 0 ? baseFont.Bold : weight is "1" or "-1" or "700" };
                            }
                            else if (tag.Groups[5].Success)
                                font = font with { Italic = tag.Groups[5].Value.Length == 0 ? baseFont.Italic : tag.Groups[5].Value != "0" };
                        }
                    }
                    else if (!drawing)
                    {
                        if (!usage.TryGetValue(font, out var glyphs)) usage[font] = glyphs = [];
                        value = value.Replace("\\N", "").Replace("\\n", "").Replace("\\h", "\u00A0");
                        foreach (var rune in value.EnumerateRunes())
                            if (!Rune.IsControl(rune)) glyphs.Add(rune.Value);
                    }
                }
            }
        }
        if (dialogueCount == 0 || usage.Count == 0)
            throw new MediaRuleException("ASS 未包含可子集化的文本对白。");
        return new(usage.Select(pair => new FontUsage(pair.Key, new(pair.Value))));
    }


    public static EquatableArray<FontMapping> ReadFontMappings(string correctedAss)
    {
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match mapping in SubsetMapping().Matches(correctedAss))
        {
            var token = mapping.Groups[1].Value.ToUpperInvariant();
            var original = mapping.Groups[2].Value.Trim().TrimStart('@').ToUpperInvariant();
            if (mappings.TryGetValue(token, out var existing) && existing != original)
                throw new MediaRuleException("AFS 字体 mapping 存在歧义：" + token);
            mappings[token] = original;
        }
        return new(mappings.Select(pair => new FontMapping(pair.Key, pair.Value)));
    }

    private static string Field(string name, string[] format, string[] values)
    {
        var index = Array.FindIndex(format, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index < values.Length ? values[index].Trim()
            : throw new MediaRuleException($"ASS 格式字段缺失：{name}");
    }

    [GeneratedRegex(@"\{[^}]*\}|[^{}]+")]
    private static partial Regex Parts();
    [GeneratedRegex(@"\\fn([^\\}]*)|\\r([^\\}]*)|\\p(\d+)(?=\\|$)|\\b(-?\d*)(?=\\|$)|\\i(-?\d*)(?=\\|$)")]
    private static partial Regex Tags();
    [GeneratedRegex(@"^; Font Subset: ([^ ]+) - ([^\r\n]+)", RegexOptions.Multiline)]
    private static partial Regex SubsetMapping();
}
