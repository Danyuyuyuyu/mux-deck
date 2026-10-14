using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Domain.Tests;

public sealed class MediaTrackInfoTests
{
    private static MediaTrackInfo Track(TrackKind kind = TrackKind.Video, string rawType = "video") =>
        new(new(7), kind, rawType, "AVC", "V_MPEG4/ISO/AVC", new("eng"), new("en"),
            "源轨", new(true, false), "1920x1080", null, null, true, true);

    [Fact]
    public void Independently_built_probe_and_qc_facts_have_equal_values()
    {
        Assert.Equal(Track(), Track());
        var first = new MediaQcFacts([Track()], 100);
        var second = new MediaQcFacts([Track()], 100);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, second with { Tracks = [Track() with { Flags = new(false, false) }] });
    }

    [Fact]
    public void Missing_ietf_and_declared_empty_ietf_remain_distinct()
    {
        var missing = Track() with { LanguageIetf = null };
        var empty = Track() with { LanguageIetf = new("") };
        Assert.NotEqual(missing, empty);
        var accepted = OutputQcRules.CheckFacts(new([missing], null), new([empty], null), [], [], []);
        Assert.Null(accepted.Failure);
        Assert.Equal(new[] { new TrackQcCheck(TrackQcCheckKind.Retained, TrackKind.Video, 0, 1, null),
            new TrackQcCheck(TrackQcCheckKind.Retained, TrackKind.Audio, 0, 0, null) }, accepted.Checks.ToArray());
        var rejected = OutputQcRules.CheckFacts(new([empty], null), new([missing], null), [], [], []);
        Assert.Equal(new TrackQcFailure(TrackQcFailureKind.SourceMetadata, TrackKind.Video, 1), rejected.Failure);
        Assert.Empty(rejected.Checks);
    }

    // 对应旧 --warning-policy 元数据严格比较：传统语言大小写不同不能被值类型规范化放行。
    [Fact]
    public void Traditional_language_still_uses_ordinal_comparison()
    {
        var source = Track();
        var output = source with { Language = new("ENG") };
        var result = OutputQcRules.CheckFacts(new([source], null), new([output], null), [], [], []);
        Assert.Equal(new TrackQcFailure(TrackQcFailureKind.SourceMetadata, TrackKind.Video, 1), result.Failure);
        Assert.Empty(result.Checks);
    }

    [Fact]
    public void Unknown_raw_type_is_not_treated_as_a_known_track_kind()
    {
        var unknown = Track(TrackKind.Unknown, "Video");
        var result = OutputQcRules.CheckFacts(new([unknown], null), new([], null), [], [], []);
        Assert.Null(result.Failure);
        Assert.Equal(new[] { new TrackQcCheck(TrackQcCheckKind.Retained, TrackKind.Video, 0, 0, null),
            new TrackQcCheck(TrackQcCheckKind.Retained, TrackKind.Audio, 0, 0, null) }, result.Checks.ToArray());
    }
}
