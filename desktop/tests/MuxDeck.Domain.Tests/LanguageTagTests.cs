using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Domain.Tests;

public sealed class LanguageTagTests
{
    [Theory]
    [InlineData("")]
    [InlineData("und")]
    [InlineData("zh-Hans")]
    [InlineData("zho")]
    [InlineData(" zh-Hant ")]
    [InlineData("x-private")]
    public void Language_tags_preserve_raw_input_for_legacy_conversion(string raw)
    {
        var tag = new LanguageTag(raw);
        Assert.Equal(raw, tag.Value);
        Assert.Equal(raw, tag.ToString());
    }

    [Fact]
    public void Default_tag_has_an_empty_value()
    {
        Assert.Equal(string.Empty, default(LanguageTag).Value);
    }

    [Fact]
    public void Null_is_rejected_instead_of_becoming_an_undeclared_language()
    {
        Assert.Throws<ArgumentNullException>(() => new LanguageTag(null!));
    }

    [Fact]
    public void Default_and_empty_language_tags_have_the_same_value_and_hash()
    {
        var first = default(LanguageTag);
        var second = new LanguageTag("");
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.True(first == second);
        Assert.NotEqual(new LanguageTag("zh-Hans"), new LanguageTag("zh-hans"));
    }
}
