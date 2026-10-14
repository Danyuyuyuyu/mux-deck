using MuxDeck.Core.Muxing;
using Xunit;

namespace MuxDeck.Core.Tests;

public sealed class ProbeRoundTripTests
{
    [Theory]
    [InlineData("video", null, false, false)]
    [InlineData("audio", "", true, false)]
    [InlineData("subtitles", "zh-Hant", false, true)]
    [InlineData("Video", "x-private", true, true)]
    [InlineData("unknown", null, true, true)]
    [InlineData("", "", false, false)]
    public void Legacy_probe_round_trip_preserves_every_field_and_raw_unknown_types(string type, string? ietf, bool defaultFlag, bool forcedFlag)
    {
        var track = new MediaTrack(21, type, "原始编码", "codec-id", "chi", ietf, "中文 名称  ",
            true, false, "1920x1080", 6, 48000, defaultFlag, forcedFlag);
        var attachments = new[] { new MediaAttachment(7, "字体.ttf", "font/ttf", 12345) };
        var legacy = new MediaProbe("中文 path.mkv", [track], attachments, 987654321);
        var restored = LegacyMuxContractConverter.ToLegacyProbe(LegacyMuxContractConverter.ToProbeResult(legacy));
        Assert.Equal(legacy.Path, restored.Path);
        Assert.Equal(legacy.DurationNanoseconds, restored.DurationNanoseconds);
        Assert.Equal(legacy.Tracks, restored.Tracks);
        Assert.Equal(legacy.Attachments, restored.Attachments);
    }
}
