using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Domain.Tests;

public sealed class OutputQcRulesTests
{
    private static TrackQcFacts Track(string type = "video", int id = 0) =>
        new(id, type, type == "video" ? "AVC" : "AAC", "codec", "eng", "en", "源轨",
            true, false, "1920x1080", 2, 48000, true, true);
    private static MediaQcFacts Media(params TrackQcFacts[] tracks) => new(new(tracks), 100_000_000_000);
    private static OutputQcResult Check(MediaQcFacts source, MediaQcFacts output) =>
        OutputQcRules.CheckTracks(source, output, [], [], []);

    // 对应 Smoke 成品源 A/V 保留与 MP4 未声明字段可由输出补齐。
    [Fact]
    public void Retained_tracks_preserve_known_fields_and_allow_new_track_ids()
    {
        var track = Track();
        var checks = Check(Media(track), Media(track with { Id = 9, LanguageIetf = "EN" }));
        Assert.Collection(checks.Checks, item => Assert.Contains("保留 1 条 video", item),
            item => Assert.Contains("保留 0 条 audio", item));
    }

    [Theory]
    [InlineData("codec")]
    [InlineData("codecId")]
    [InlineData("dimensions")]
    [InlineData("channels")]
    [InlineData("sampling")]
    [InlineData("name")]
    [InlineData("language")]
    [InlineData("ietf")]
    [InlineData("default")]
    [InlineData("forced")]
    public void Changed_known_source_metadata_is_rejected(string field)
    {
        var before = Track();
        var actual = field switch
        {
            "codec" => before with { Codec = "wrong" },
            "codecId" => before with { CodecId = "wrong" },
            "dimensions" => before with { PixelDimensions = "wrong" },
            "channels" => before with { AudioChannels = 6 },
            "sampling" => before with { AudioSamplingFrequency = 44100 },
            "name" => before with { Name = "wrong" },
            "language" => before with { Language = "jpn" },
            "ietf" => before with { LanguageIetf = "ja" },
            "default" => before with { IsDefault = false },
            _ => before with { IsForced = true }
        };
        Assert.Contains("源文件不同", Check(Media(before), Media(actual)).Error!);
    }

    [Fact]
    public void Absent_source_metadata_and_flags_are_not_fabricated()
    {
        var source = Track() with { CodecId = null, PixelDimensions = null, AudioChannels = null,
            AudioSamplingFrequency = null, LanguageIetf = null, HasDefaultFlag = false, HasForcedFlag = false };
        Assert.Null(Check(Media(source), Media(Track() with { IsDefault = false, IsForced = true })).Error);
        Assert.NotNull(Check(Media(source), Media()).Error);
    }

    // 对应真实多轨时长与未知 MP4 不用字幕尾时间补时长；纯事实测试不启动工具。
    [Theory]
    [InlineData(101_000_000_000L, true)]
    [InlineData(101_000_000_001L, false)]
    [InlineData(99_000_000_000L, true)]
    [InlineData(98_999_999_999L, false)]
    public void Duration_keeps_the_existing_one_second_or_one_percent_tolerance(long duration, bool expected)
    {
        var output = Media(Track()) with { DurationNanoseconds = duration };
        if (expected) Assert.Null(Check(Media(Track()), output).Error);
        else Assert.Contains("时长", Check(Media(Track()), output).Error!);
    }

    [Fact]
    public void Unknown_audio_duration_disables_prediction_and_declared_audio_extends_it()
    {
        var source = Media(Track());
        var audio = Track("audio", 9);
        var output = Media(Track(), audio) with { DurationNanoseconds = 200_000_000_000 };
        var inputs = new EquatableArray<AudioQcFacts>([new(9, audio.Name, audio.Language, audio.IsDefault, audio.IsForced)]);
        Assert.Null(OutputQcRules.CheckTracks(source, output, [], inputs,
            [Media(audio) with { DurationNanoseconds = null }]).Error);
        Assert.Null(OutputQcRules.CheckTracks(source, output, [], inputs,
            [Media(audio) with { DurationNanoseconds = 200_000_000_000 }]).Error);
    }

    // 对应 --warning-policy QC 拒绝仍保留此前已通过的检查，不能以重构丢失失败证据。
    [Fact]
    public void Later_failure_keeps_preceding_checks()
    {
        var result = OutputQcRules.CheckTracks(Media(Track()),
            Media(Track()) with { DurationNanoseconds = 200_000_000_000 }, [], [], []);
        Assert.Contains("时长", result.Error!);
        Assert.Collection(result.Checks, item => Assert.Contains("video", item), item => Assert.Contains("audio", item));
    }

    // 对应 --warning-policy 新增字幕 QC 与单/双/TC-only 的元数据检查。
    [Theory]
    [InlineData("valid", true)]
    [InlineData("count", false)]
    [InlineData("codec", false)]
    [InlineData("name", false)]
    [InlineData("language", false)]
    [InlineData("default", false)]
    [InlineData("forced", false)]
    public void Added_subtitles_must_match_every_requested_field(string field, bool expected)
    {
        var subtitle = Track("subtitles", 8) with { CodecId = "S_TEXT/ASS", Name = "SC",
            Language = "chi", LanguageIetf = "zh-Hans" };
        subtitle = field switch
        {
            "codec" => subtitle with { CodecId = "S_TEXT/UTF8" },
            "name" => subtitle with { Name = "wrong" },
            "language" => subtitle with { LanguageIetf = "zh-Hant" },
            "default" => subtitle with { IsDefault = false },
            "forced" => subtitle with { IsForced = true },
            _ => subtitle
        };
        var actual = field == "count" ? Media(Track()) : Media(Track(), subtitle);
        var results = OutputQcRules.CheckTracks(Media(Track()), actual, [new(true, "SC", "zh-Hans", true, false)], [], []);
        if (expected) { Assert.Null(results.Error); Assert.Contains(results.Checks, item => item.Contains("新增字幕 1", StringComparison.Ordinal)); }
        else Assert.NotNull(results.Error);
    }

    // 对应 --media-drop-real 的新增音轨元数据及真实 Track ID 选择；这里复用探测事实。
    [Theory]
    [InlineData("valid", true)]
    [InlineData("codec", false)]
    [InlineData("channels", false)]
    [InlineData("sampling", false)]
    [InlineData("name", false)]
    [InlineData("language", false)]
    [InlineData("default", false)]
    [InlineData("forced", false)]
    public void Added_audio_uses_real_source_track_id_and_request_metadata(string field, bool expected)
    {
        var before = Track("audio", 9);
        var actual = before with { Id = 22, Name = "评论" };
        actual = field switch
        {
            "codec" => actual with { Codec = "wrong" },
            "channels" => actual with { AudioChannels = 6 },
            "sampling" => actual with { AudioSamplingFrequency = 44100 },
            "name" => actual with { Name = "wrong" },
            "language" => actual with { LanguageIetf = "ja" },
            "default" => actual with { IsDefault = false },
            "forced" => actual with { IsForced = true },
            _ => actual
        };
        var results = OutputQcRules.CheckTracks(Media(Track()), Media(Track(), actual), [],
            [new(9, "评论", "eng", true, false)], [Media(Track("audio", 2) with { Codec = "unselected" }, before)]);
        if (expected) { Assert.Null(results.Error); Assert.Contains(results.Checks, item => item.Contains("外加音频 1", StringComparison.Ordinal)); }
        else Assert.NotNull(results.Error);
    }

    // 对应 --warning-policy 的同工具 transport 全文参考；不能放宽 Dialogue 任一字段。
    [Theory]
    [InlineData("identical", true)]
    [InlineData("crlf", true)]
    [InlineData("text", false)]
    [InlineData("timing", false)]
    [InlineData("style", false)]
    [InlineData("font", false)]
    [InlineData("order", false)]
    [InlineData("trailingSpace", false)]
    public void Subtitle_full_text_only_normalizes_crlf(string change, bool expected)
    {
        var reference = AssFixture.Text;
        var actual = change switch
        {
            "crlf" => reference.Replace("\n", "\r\n"),
            "text" => reference.Replace("Hello", "Changed"),
            "timing" => reference.Replace("0:00:01.00", "0:00:01.10"),
            "style" => reference.Replace(",Default,Hello", ",Other,Hello"),
            "font" => reference.Replace("Arial", "Times"),
            "order" => reference.Replace("Hello", "temporary").Replace("World", "Hello").Replace("temporary", "World"),
            "trailingSpace" => reference + " ",
            _ => reference
        };
        Assert.Equal(expected, OutputQcRules.SubtitleContentEquals(reference, actual));
    }
}
