using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Domain.Tests;

public sealed class InputTypeRulesTests
{
    // 对应 --media-drop 的视频/ASS/SRT/音频/字体混合导入及未知类型整批拒绝。
    [Theory]
    [InlineData(".mkv", true, false, false, false)]
    [InlineData(".mp4", true, false, false, false)]
    [InlineData(".mov", true, false, false, false)]
    [InlineData(".avi", true, false, false, false)]
    [InlineData(".webm", true, false, false, false)]
    [InlineData(".m4v", true, false, false, false)]
    [InlineData(".ts", true, false, false, false)]
    [InlineData(".m2ts", true, false, false, false)]
    [InlineData(".mts", true, false, false, false)]
    [InlineData(".mpg", true, false, false, false)]
    [InlineData(".mpeg", true, false, false, false)]
    [InlineData(".flac", false, true, false, false)]
    [InlineData(".aac", false, true, false, false)]
    [InlineData(".m4a", false, true, false, false)]
    [InlineData(".wav", false, true, false, false)]
    [InlineData(".opus", false, true, false, false)]
    [InlineData(".ogg", false, true, false, false)]
    [InlineData(".mp3", false, true, false, false)]
    [InlineData(".mp2", false, true, false, false)]
    [InlineData(".ac3", false, true, false, false)]
    [InlineData(".eac3", false, true, false, false)]
    [InlineData(".dts", false, true, false, false)]
    [InlineData(".mka", false, true, false, false)]
    [InlineData(".w64", false, true, false, false)]
    [InlineData(".ass", false, false, true, false)]
    [InlineData(".srt", false, false, true, false)]
    [InlineData(".ttf", false, false, false, true)]
    [InlineData(".otf", false, false, false, true)]
    [InlineData(".ttc", false, false, false, true)]
    [InlineData(".otc", false, false, false, true)]
    [InlineData(".MKV", true, false, false, false)]
    [InlineData(".FLAC", false, true, false, false)]
    [InlineData(".ASS", false, false, true, false)]
    [InlineData(".TTC", false, false, false, true)]
    [InlineData(".txt", false, false, false, false)]
    [InlineData(".ssa", false, false, false, false)]
    [InlineData(".mk3d", false, false, false, false)]
    [InlineData(".ass.bak", false, false, false, false)]
    [InlineData("", false, false, false, false)]
    public void Extensions_keep_the_existing_entry_allowlist(string extension, bool video, bool audio, bool subtitle, bool font)
    {
        Assert.Equal((video, audio, subtitle, font), (InputRules.IsVideoExtension(extension),
            InputRules.IsAudioExtension(extension), InputRules.IsSubtitleExtension(extension), InputRules.IsFontExtension(extension)));
        Assert.Equal(video || audio || subtitle || font, InputRules.IsSupportedMediaExtension(extension));
        Assert.Equal(extension is ".ass" or ".ASS", InputRules.IsAssExtension(extension));
    }

    // 对应 --single-mux-round1 的大写 MKV/ASS 接纳及错误类型拒绝；自动导入扩展不改变旧槽位。
    [Theory]
    [InlineData(".MKV", true, true)]
    [InlineData(".mp4", true, true)]
    [InlineData(".mov", true, false)]
    [InlineData(".ass", true, false)]
    [InlineData(".ASS", false, true)]
    [InlineData(".srt", false, false)]
    [InlineData(".mp4", false, false)]
    public void Fixed_slots_retain_their_narrower_formats(string extension, bool video, bool expected) =>
        Assert.Equal(expected, InputRules.AcceptsFixedSlotExtension(extension, video));

    // 对应固定槽位非本地数据拒绝、文件/文件夹不可混用。
    [Theory]
    [InlineData(InputLocationKind.LocalFile, false, InputKindError.None)]
    [InlineData(InputLocationKind.LocalDirectory, true, InputKindError.None)]
    [InlineData(InputLocationKind.NonLocal, false, InputKindError.NonLocal)]
    [InlineData(InputLocationKind.NonLocal, true, InputKindError.NonLocal)]
    [InlineData(InputLocationKind.LocalFile, true, InputKindError.WrongType)]
    [InlineData(InputLocationKind.LocalDirectory, false, InputKindError.WrongType)]
    [InlineData(InputLocationKind.Unknown, false, InputKindError.WrongType)]
    public void Candidate_kind_rejection_keeps_nonlocal_priority(InputLocationKind kind, bool directory, InputKindError expected) =>
        Assert.Equal(expected, InputRules.CheckCandidateKind(kind, directory));

    // 自动入口按本地文件筛类型，目录和历史未知 kind 仍沿用原流程，不新增拒绝策略。
    [Theory]
    [InlineData(InputLocationKind.LocalFile, ".srt", true, false)]
    [InlineData(InputLocationKind.LocalFile, ".txt", false, false)]
    [InlineData(InputLocationKind.LocalFile, ".OTC", true, true)]
    [InlineData(InputLocationKind.LocalDirectory, "", true, true)]
    [InlineData(InputLocationKind.Unknown, ".txt", true, false)]
    public void Media_candidate_type_and_font_group_follow_the_supplied_kind(InputLocationKind kind, string extension, bool accepted, bool fontDirectory)
    {
        Assert.Equal(accepted, InputRules.AcceptsMediaCandidateType(kind, extension));
        Assert.Equal(fontDirectory, InputRules.UsesFontDirectory(kind, extension));
    }

    // 对应 --media-drop 的多音轨容器；有视频但不是受支持音频入口时不冒充音频。
    [Theory]
    [InlineData(2, true, false, true)]
    [InlineData(1, false, false, true)]
    [InlineData(1, true, true, true)]
    [InlineData(0, true, false, false)]
    [InlineData(1, false, true, false)]
    public void Audio_probe_requires_audio_and_preserves_the_container_gate(int count, bool audioExtension, bool videoTrack, bool expected) =>
        Assert.Equal(expected, InputRules.AcceptsAudioProbe(count, audioExtension, videoTrack));

    // 对应 --media-drop 有效 ASS/SRT 和文件内容不符整批拒绝；CodecId 优先于描述文本。
    [Theory]
    [InlineData(1, "S_TEXT/ASS", "unknown", true, true)]
    [InlineData(1, "S_TEXT/UTF8", "unknown", false, true)]
    [InlineData(1, null, "SubStationAlpha", true, true)]
    [InlineData(1, null, "SubRip/SRT", false, true)]
    [InlineData(0, null, "", true, false)]
    [InlineData(2, "S_TEXT/ASS", "ASS", true, false)]
    [InlineData(1, "S_TEXT/UTF8", "ASS", true, false)]
    [InlineData(1, "", "ASS", true, false)]
    [InlineData(1, null, "unknown", false, false)]
    public void Subtitle_probe_requires_one_matching_track(int count, string? codecId, string codec, bool ass, bool expected) =>
        Assert.Equal(expected, InputRules.AcceptsSubtitleProbe(count, codecId, codec, ass));

    // 对应 --media-drop 按实际视频轨确认源视频，扩展名不能代替容器能力。
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public void Source_video_requires_both_the_container_fact_and_video_entry(bool hasVideo, bool videoExtension, bool expected) =>
        Assert.Equal(expected, InputRules.IsSourceVideo(hasVideo, videoExtension));
}
