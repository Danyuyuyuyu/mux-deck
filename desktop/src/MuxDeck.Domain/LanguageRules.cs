using System.Text.RegularExpressions;

namespace MuxDeck.Domain;

public static partial class LanguageRules
{
    [GeneratedRegex(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$")]
    private static partial Regex LanguageTagPattern();

    // 仅在旧探测字段未声明语言时推断；保留 IETF 优先级、und 大小写和标签边界。
    public static string InferImported(string sourceBaseName, string legacyLanguage, string? languageIetf)
    {
        var language = languageIetf ?? legacyLanguage;
        if (!string.IsNullOrWhiteSpace(language) && language != "und") return language;
        foreach (var (pattern, code) in new[] { ("zh[-_]hans|简体|简中|(^|[._\\-\\s\\[\\]()])(sc|chs|gb)($|[._\\-\\s\\[\\]()])", "zh-Hans"), ("zh[-_]hant|繁体|繁體|繁中|(^|[._\\-\\s\\[\\]()])(tc|cht|big5)($|[._\\-\\s\\[\\]()])", "zh-Hant"), ("(^|[._\\-\\s\\[\\]()])(eng|en)($|[._\\-\\s\\[\\]()])", "en"), ("(^|[._\\-\\s\\[\\]()])(jpn|ja)($|[._\\-\\s\\[\\]()])", "ja") })
            if (Regex.IsMatch(sourceBaseName, pattern, RegexOptions.IgnoreCase)) return code;
        return "und";
    }

    // 语言标签语法校验：旧单任务链路与导入用例共用同一实现。
    public static bool IsValidTag(string value) => !string.IsNullOrWhiteSpace(value) && LanguageTagPattern().IsMatch(value);

    public static bool Matches(string legacyLanguage, string? languageIetf, string expectedLanguage)
    {
        var canonical = expectedLanguage.ToLowerInvariant() switch { "chi" or "zho" => "zh", "eng" => "en", "jpn" => "ja", _ => expectedLanguage };
        return string.Equals(languageIetf, canonical, StringComparison.OrdinalIgnoreCase) ||
               (languageIetf is null && string.Equals(legacyLanguage, expectedLanguage, StringComparison.OrdinalIgnoreCase));
    }
}
