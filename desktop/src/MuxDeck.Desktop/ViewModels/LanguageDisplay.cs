using System.Globalization;

namespace MuxDeck.Desktop.ViewModels;

public static class LanguageDisplay
{
    // 仅转换展示名称；不归一化传给 MKVToolNix 或 QC 的原始代码。
    public static string Name(string code)
    {
        var normalized = code.Trim().ToLowerInvariant();
        var known = normalized switch
        {
            "" or "und" => "未指定", "jpn" or "ja" => "日语",
            "zh-hans" or "zh-cn" or "zh-sg" => "简体中文",
            "zh-hant" or "zh-tw" or "zh-hk" or "zh-mo" => "繁体中文",
            "chi" or "zho" or "zh" => "中文", "eng" or "en" => "英语",
            "kor" or "ko" => "韩语", "fra" or "fre" or "fr" => "法语",
            "deu" or "ger" or "de" => "德语", "spa" or "es" => "西班牙语",
            "ita" or "it" => "意大利语", "rus" or "ru" => "俄语", _ => null
        };
        if (known is not null) return known;
        try
        {
            var culture = CultureInfo.GetCultureInfo(code);
            // 系统可能返回未知标签的占位 Culture，不让空名称隐藏原始代码。
            return string.IsNullOrWhiteSpace(culture.NativeName) ? code : culture.NativeName;
        }
        catch (CultureNotFoundException) { return code; }
    }
}
