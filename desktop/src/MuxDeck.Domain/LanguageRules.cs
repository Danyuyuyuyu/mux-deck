using System.Text.RegularExpressions;

namespace MuxDeck.Domain;

public static class LanguageRules
{
    // 仅在旧探测字段未声明语言时推断；保留 IETF 优先级、und 大小写和标签边界。
    public static string InferImported(string sourceBaseName, string legacyLanguage, string? languageIetf)
    {
        var language = languageIetf ?? legacyLanguage;
        if (!string.IsNullOrWhiteSpace(language) && language != "und") return language;
        foreach (var (pattern, code) in new[] { ("zh[-_]hans|简体|简中|(^|[._\\-\\s\\[\\]()])(sc|chs|gb)($|[._\\-\\s\\[\\]()])", "zh-Hans"), ("zh[-_]hant|繁体|繁體|繁中|(^|[._\\-\\s\\[\\]()])(tc|cht|big5)($|[._\\-\\s\\[\\]()])", "zh-Hant"), ("(^|[._\\-\\s\\[\\]()])(eng|en)($|[._\\-\\s\\[\\]()])", "en"), ("(^|[._\\-\\s\\[\\]()])(jpn|ja)($|[._\\-\\s\\[\\]()])", "ja") })
            if (Regex.IsMatch(sourceBaseName, pattern, RegexOptions.IgnoreCase)) return code;
        return "und";
    }

    public static bool Matches(string legacyLanguage, string? languageIetf, string expectedLanguage)
    {
        var canonical = expectedLanguage.ToLowerInvariant() switch { "chi" or "zho" => "zh", "eng" => "en", "jpn" => "ja", _ => expectedLanguage };
        return string.Equals(languageIetf, canonical, StringComparison.OrdinalIgnoreCase) ||
               (languageIetf is null && string.Equals(legacyLanguage, expectedLanguage, StringComparison.OrdinalIgnoreCase));
    }
}
