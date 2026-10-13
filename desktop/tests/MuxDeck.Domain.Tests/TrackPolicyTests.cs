using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Domain.Tests;

public sealed class TrackPolicyTests
{
    // 对应 Smoke / --single-mux-round1 的 SC、TC-only、SC+TC 默认轨和移除最后字幕。
    [Theory]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, false, false)]
    public void Subtitle_defaults_follow_the_present_slots(bool hasSc, bool hasTc, bool scDefault, bool tcDefault) =>
        Assert.Equal((scDefault, tcDefault), TrackPolicy.SubtitleDefaults(hasSc, hasTc));

    [Fact]
    public void Empty_draft_keeps_legacy_initial_flags_until_a_slot_changes()
    {
        Assert.True(TrackPolicy.InitialScDefault);
        Assert.False(TrackPolicy.InitialTcDefault);
        Assert.Equal((false, false), TrackPolicy.SubtitleDefaults(false, false));
    }

    // 对应 --single-mux-round1 的“源 A/V 保留与源字幕移除为只读策略”及源附件投影。
    [Theory]
    [InlineData(TrackKind.Video, true)]
    [InlineData(TrackKind.Audio, true)]
    [InlineData(TrackKind.Subtitle, false)]
    [InlineData(TrackKind.Attachment, false)]
    [InlineData(TrackKind.Unknown, false)]
    public void Only_source_video_and_audio_are_retained(TrackKind kind, bool expected) =>
        Assert.Equal(expected, TrackPolicy.RetainsSource(kind));

    // 对应 --single-mux-ui-round1 的只读源纳入列、外加可切换及禁用槽位保留元数据。
    [Theory]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, true, true)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, false)]
    public void Inclusion_uses_source_policy_or_external_choice(bool isExternal, bool externalIncluded, bool sourceRetained, bool expected) =>
        Assert.Equal(expected, TrackPolicy.IsIncluded(isExternal, externalIncluded, sourceRetained));
}
