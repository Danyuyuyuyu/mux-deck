using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Domain.Tests;

public sealed class LanguageRulesTests
{
    // 对应 --media-drop 的“未指定语言从有边界的文件名标签推断，未知回退 und”。
    [Theory]
    [InlineData("movie.zh-Hans", "zh-Hans")]
    [InlineData("movie.zh_hant", "zh-Hant")]
    [InlineData("电影简体", "zh-Hans")]
    [InlineData("電影繁體", "zh-Hant")]
    [InlineData("movie[SC]", "zh-Hans")]
    [InlineData("CHS", "zh-Hans")]
    [InlineData("movie-gb", "zh-Hans")]
    [InlineData("movie(TC)", "zh-Hant")]
    [InlineData("movie.cht", "zh-Hant")]
    [InlineData("movie big5", "zh-Hant")]
    [InlineData("movie.ENG", "en")]
    [InlineData("movie_en", "en")]
    [InlineData("audio.jpn", "ja")]
    [InlineData("audio-ja", "ja")]
    [InlineData("science", "und")]
    [InlineData("english", "und")]
    [InlineData("movie.japanese", "und")]
    [InlineData("untagged", "und")]
    [InlineData("movie.eng.zh-Hant.sc", "zh-Hans")]
    public void Undeclared_language_uses_filename_tags_with_legacy_boundaries(string baseName, string expected) =>
        Assert.Equal(expected, LanguageRules.InferImported(baseName, "und", null));

    // 对应 --media-drop 导入保留探测/编辑语言；反例保证不以文件名覆盖已声明语言。
    [Theory]
    [InlineData("en", null, "en")]
    [InlineData("jpn", "zh-Hant", "zh-Hant")]
    [InlineData("en", "und", "zh-Hans")]
    [InlineData("en", "", "zh-Hans")]
    [InlineData("en", " ", "zh-Hans")]
    [InlineData("UND", null, "UND")]
    [InlineData(" zh-Hant ", null, " zh-Hant ")]
    public void Declared_language_has_priority_and_undeclared_ietf_does_not_fall_back_to_legacy(string legacy, string? ietf, string expected) =>
        Assert.Equal(expected, LanguageRules.InferImported("movie.zh-Hans", legacy, ietf));

    // 对应 Smoke 公共封装 QC，以及 --media-drop-real 的“篡改新增音轨语言时 QC 阻止发布”。
    // 本单元测试只验证相同元数据事实，不启动真实工具。
    [Theory]
    [InlineData("chi", "zh", "chi", true)]
    [InlineData("zho", "ZH", "ZHO", true)]
    [InlineData("eng", "en", "ENG", true)]
    [InlineData("jpn", "ja", "jpn", true)]
    [InlineData("chi", "zh-Hans", "zh-Hans", true)]
    [InlineData("chi", "ZH-HANT", "zh-Hant", true)]
    [InlineData("eng", null, "ENG", true)]
    [InlineData("chi", null, "chi", true)]
    [InlineData("eng", "ja", "eng", false)]
    [InlineData("eng", "", "eng", false)]
    [InlineData("zho", null, "zh", false)]
    [InlineData("chi", "zh", "zh-Hans", false)]
    [InlineData("chi", "zh-Hant", "zh-Hans", false)]
    [InlineData("und", null, "en", false)]
    public void Matching_preserves_ietf_aliases_and_null_only_legacy_fallback(string legacy, string? ietf, string expectedLanguage, bool expected) =>
        Assert.Equal(expected, LanguageRules.Matches(legacy, ietf, expectedLanguage));
}
