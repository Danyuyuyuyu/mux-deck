using MuxDeck.Core.Muxing;
using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Core.Tests;

public sealed class MediaProbeConversionTests
{
    private static MediaTrack Track(string type = "audio", string? ietf = "en") =>
        new(9, type, "AAC", null, "ENG", ietf, "评论", false, true,
            null, 6, 48000, false, true);

    [Theory]
    [InlineData("video", TrackKind.Video)]
    [InlineData("audio", TrackKind.Audio)]
    [InlineData("subtitles", TrackKind.Subtitle)]
    [InlineData("attachment", TrackKind.Unknown)]
    [InlineData("Video", TrackKind.Unknown)]
    [InlineData("", TrackKind.Unknown)]
    [InlineData("unknown", TrackKind.Unknown)]
    public void Kind_conversion_preserves_exact_legacy_labels(string type, TrackKind expected)
    {
        var converted = LegacyMuxContractConverter.ToTrackInfo(Track(type));
        Assert.Equal(expected, converted.Kind);
        Assert.Equal(type, converted.RawType);
    }

    [Fact]
    public void Conversion_preserves_known_and_unknown_metadata_without_fabrication()
    {
        var converted = LegacyMuxContractConverter.ToTrackInfo(Track());
        Assert.Equal(new TrackId(9), converted.Id);
        Assert.Equal("AAC", converted.Codec);
        Assert.Null(converted.CodecId);
        Assert.Equal(new LanguageTag("ENG"), converted.Language);
        Assert.Equal(new LanguageTag("en"), converted.LanguageIetf);
        Assert.Equal("评论", converted.Name);
        Assert.Equal(new TrackFlags(false, true), converted.Flags);
        Assert.Null(converted.PixelDimensions);
        Assert.Equal(6, converted.AudioChannels);
        Assert.Equal(48000L, converted.AudioSamplingFrequency);
        Assert.False(converted.HasDefaultFlag);
        Assert.True(converted.HasForcedFlag);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Conversion_does_not_collapse_missing_and_empty_ietf(string? ietf)
    {
        var converted = LegacyMuxContractConverter.ToTrackInfo(Track(ietf: ietf));
        Assert.Equal(ietf, converted.LanguageIetf?.Value);
    }

    [Fact]
    public void Probe_conversion_snapshots_tracks_attachments_order_and_duration()
    {
        var tracks = new[] { Track(), Track("video") with { Id = 2 } };
        var attachments = new[] { new MediaAttachment(5, "font.ttf", "font/ttf", 123) };
        var converted = LegacyMuxContractConverter.ToProbeResult(new("input.mkv", tracks, attachments, 100));
        tracks[0] = Track() with { Id = 100 };
        attachments[0] = attachments[0] with { Size = 0 };
        Assert.Equal("input.mkv", converted.Path);
        Assert.Equal(new[] { 9, 2 }, converted.Tracks.Select(track => track.Id.Value));
        Assert.Equal(100L, converted.DurationNanoseconds);
        var attachment = Assert.Single(converted.Attachments);
        Assert.Equal(5, attachment.Id);
        Assert.Equal("font.ttf", attachment.FileName);
        Assert.Equal("font/ttf", attachment.ContentType);
        Assert.Equal(123L, attachment.Size);
        // 同一转换结果可直接交给纯 QC，无另一套轨道事实或再次解释语言/旗标。
        var qc = new MediaQcFacts(converted.Tracks, converted.DurationNanoseconds);
        var result = OutputQcRules.CheckFacts(qc, qc, [], [], []);
        Assert.Null(result.Failure);
        Assert.Equal(new[] { new TrackQcCheck(TrackQcCheckKind.Retained, TrackKind.Video, 0, 1, null),
            new TrackQcCheck(TrackQcCheckKind.Retained, TrackKind.Audio, 0, 1, null) }, result.Checks.ToArray());
    }
}
