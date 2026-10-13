using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Domain.Tests;

public sealed class AssDocumentTests
{
    // 对应 Smoke ASS/字体使用与 --warning-policy 的映射/候选/字形拒绝场景。
    [Fact]
    public void Sections_are_required_without_reading_a_file()
    {
        Assert.True(AssDocument.HasRequiredSections(AssFixture.Text));
        Assert.False(AssDocument.HasRequiredSections(AssFixture.Text.Replace("[Events]", "")));
        Assert.True(AssDocument.HasRequiredSections(AssFixture.Text.ToLowerInvariant()));
    }

    [Fact]
    public void Unicode_and_ass_escapes_produce_the_requested_codepoints()
    {
        var usage = Assert.Single(AssDocument.ReadUsage(AssFixture.WithText(@"A😀\NB\hC")));
        Assert.Equal(new AssDocument.FontFace("ARIAL", false, false), usage.Face);
        Assert.Equal(new[] { 65, 66, 67, 160, 0x1F600 }, usage.Glyphs.Order().ToArray());
    }

    [Fact]
    public void Overrides_and_style_reset_keep_font_and_flags_with_their_text()
    {
        var usage = AssDocument.ReadUsage(AssFixture.WithText(@"A{\fnTimes New Roman\b700\i1}B{\r}C{\rOther}D"));
        var baseUse = Assert.Single(usage, item => item.Face == new AssDocument.FontFace("ARIAL", false, false));
        var other = Assert.Single(usage, item => item.Face == new AssDocument.FontFace("TIMES NEW ROMAN", true, true));
        Assert.Equal(new[] { 65, 67 }, baseUse.Glyphs.Order().ToArray());
        Assert.Equal(new[] { 66, 68 }, other.Glyphs.Order().ToArray());
    }

    [Fact]
    public void Drawing_commands_are_not_font_glyphs()
    {
        var usage = Assert.Single(AssDocument.ReadUsage(AssFixture.WithText(@"{\p1}m 0 0 l 10 10{\p0}A")));
        Assert.Equal(new[] { 65 }, usage.Glyphs.ToArray());
    }

    [Theory]
    [InlineData(@"{\t(\fnArial)}A", "动画")]
    [InlineData(@"{\t(\b1)}A", "动画")]
    [InlineData(@"{\t(\i1)}A", "动画")]
    [InlineData(@"{\b600}A", "非标准")]
    [InlineData(@"{\rMissing}A", "覆盖样式不存在")]
    [InlineData(@"{\p1}m 0 0", "文本对白")]
    public void Unsupported_or_undefined_font_usage_is_rejected(string text, string message) =>
        Assert.Contains(message, Assert.Throws<MediaRuleException>(() =>
            AssDocument.ReadUsage(AssFixture.WithText(text))).Message);

    [Fact]
    public void Missing_style_and_format_fields_are_rejected()
    {
        Assert.Contains("未定义样式", Assert.Throws<MediaRuleException>(() =>
            AssDocument.ReadUsage(AssFixture.Text.Replace(",Default,Hello", ",Missing,Hello"))).Message);
        Assert.Contains("格式字段缺失", Assert.Throws<MediaRuleException>(() =>
            AssDocument.ReadUsage(AssFixture.Text.Replace("Name, Fontname, Bold, Italic", "Name, Other, Bold, Italic"))).Message);
    }

    [Fact]
    public void Font_mappings_normalize_tokens_and_reject_ambiguous_provenance()
    {
        var mapping = Assert.Single(AssDocument.ReadFontMappings("; Font Subset: token - @Arial\r\n; Font Subset: TOKEN - Arial\n"));
        Assert.Equal(new AssDocument.FontMapping("TOKEN", "ARIAL"), mapping);
        Assert.Contains("歧义", Assert.Throws<MediaRuleException>(() =>
            AssDocument.ReadFontMappings("; Font Subset: token - Arial\n; Font Subset: token - Times")).Message);
    }
}
