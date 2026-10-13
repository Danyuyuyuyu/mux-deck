using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Domain.Tests;

public sealed class InputPathRulesTests
{
    // 对应 --single-mux-round1 的相对/非本地路径拒绝和中文完整路径接纳。
    // Path 平台判定在边界完成，Domain 必须尊重传入事实，不能自己触碰文件系统。
    [Theory]
    [InlineData(@"C:\输入\源 视频.MKV", true, true, true)]
    [InlineData(@"\\server\share\video.mkv", true, true, true)]
    [InlineData("relative.mkv", false, false, false)]
    [InlineData(@"C:relative.mkv", false, false, false)]
    [InlineData("https://example/video.mkv", false, false, false)]
    [InlineData(@"C:\https://video.mkv", true, false, false)]
    [InlineData(@"\\?\C:\video.mkv", true, false, true)]
    [InlineData(@"\\.\C:\video.mkv", true, false, true)]
    [InlineData("", false, false, false)]
    [InlineData(" ", false, false, false)]
    public void Path_gates_preserve_fixed_slot_and_media_entry_differences(string path, bool qualified, bool fixedSlot, bool media)
    {
        Assert.Equal(fixedSlot, InputRules.AcceptsFixedSlotPath(path, qualified));
        Assert.Equal(media, InputRules.AcceptsMediaPath(path, qualified));
    }

    [Fact]
    public void Null_fixed_slot_path_is_rejected() =>
        Assert.False(InputRules.AcceptsFixedSlotPath(null, false));

    // 对应固定槽位无效路径拒绝；无效字符由当前运行平台提供，不在 Domain 硬编码。
    [Theory]
    [InlineData(@"C:\video.mkv", false, true)]
    [InlineData(@"\\server\share\video.mkv", false, true)]
    [InlineData(@"C:\video.mkv:stream", false, false)]
    [InlineData(@"C:\video.mkv", true, false)]
    public void Normalized_path_rejects_invalid_character_facts_and_stream_markers(string path, bool invalidChars, bool expected) =>
        Assert.Equal(expected, InputRules.AcceptsNormalizedPathSyntax(path, invalidChars));

    // 对应 --single-mux-round1 的空/多视频、多字体目录、1–2 ASS 数量限制。
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, true)]
    [InlineData(2, false, false)]
    [InlineData(0, true, false)]
    [InlineData(1, true, true)]
    [InlineData(2, true, true)]
    [InlineData(3, true, false)]
    public void Fixed_slot_count_has_one_source_or_one_to_two_subtitles(int count, bool subtitles, bool expected) =>
        Assert.Equal(expected, InputRules.AcceptsSelectionCount(count, subtitles));

    // 对应 --media-drop 混合批次可接纳、空批次提示和多个源视频拒绝。
    [Theory]
    [InlineData(0, false, true)]
    [InlineData(1, true, true)]
    [InlineData(2, true, false)]
    [InlineData(5, true, false)]
    public void Media_batches_allow_many_inputs_but_at_most_one_source_video(int count, bool selection, bool sourceVideos)
    {
        Assert.Equal(selection, InputRules.AcceptsMediaSelectionCount(count));
        Assert.Equal(sourceVideos, InputRules.AcceptsSourceVideoCount(count));
    }

    // 对应 --single-mux-round1 同槽重复分配和非法槽位拒绝。
    [Theory]
    [InlineData(0, new int[0], true)]
    [InlineData(1, new[] { 0 }, true)]
    [InlineData(0, new[] { 0 }, false)]
    [InlineData(1, new[] { 0, 1 }, false)]
    [InlineData(-1, new int[0], false)]
    [InlineData(2, new int[0], false)]
    public void Each_known_subtitle_slot_can_be_assigned_once(int slot, int[] used, bool expected) =>
        Assert.Equal(expected, InputRules.IsSubtitleSlotAvailable(slot, used));

    // 对应 Windows 同路径大小写接纳无害；Core 的平台大小写差异也通过事实传入。
    [Theory]
    [InlineData(@"C:\input\SC.ass", @"c:\INPUT\sc.ASS", true, true)]
    [InlineData(@"C:\input\SC.ass", @"c:\INPUT\sc.ASS", false, false)]
    [InlineData("/input/sc.ass", "/input/sc.ass", false, true)]
    [InlineData("/input/sc.ass", "/input/tc.ass", true, false)]
    public void Supplied_normalized_paths_use_the_requested_case_policy(string left, string right, bool ignoreCase, bool expected) =>
        Assert.Equal(expected, InputRules.SameNormalizedPath(left, right, ignoreCase));

    // 对应 --media-drop 重复批次整体拒绝；规范化已在边界完成。
    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { @"C:\input\sc.ass" }, false)]
    [InlineData(new[] { @"C:\input\sc.ass", @"c:\INPUT\SC.ASS" }, true)]
    [InlineData(new[] { @"C:\input\sc.ass", @"C:\input\tc.ass" }, false)]
    public void Duplicate_normalized_paths_are_case_insensitive(string[] paths, bool expected) =>
        Assert.Equal(expected, InputRules.HasDuplicatePaths(paths));

    // 对应两个槽位同一路径拒绝及整组交换旧槽位合法；只比较替换完成后的集合。
    [Theory]
    [InlineData("sc.ass", "tc.ass", false, false)]
    [InlineData("tc.ass", "sc.ass", false, false)]
    [InlineData("sc.ass", "SC.ASS", true, true)]
    [InlineData("", "tc.ass", false, false)]
    [InlineData(" ", " ", true, false)]
    public void Final_subtitle_set_rejects_only_two_present_equal_paths(string sc, string tc, bool samePath, bool expected) =>
        Assert.Equal(expected, InputRules.SubtitlePathsConflict(sc, tc, samePath));

    // 对应未确认替换不改草稿、同槽同路径无害、新增输入不要求替换确认。
    [Theory]
    [InlineData("", false, false)]
    [InlineData(" ", false, false)]
    [InlineData("old.ass", true, false)]
    [InlineData("old.ass", false, true)]
    public void Replacement_is_required_only_for_a_present_different_input(string current, bool samePath, bool expected) =>
        Assert.Equal(expected, InputRules.RequiresReplacement(current, samePath));

    // 对应 --media-drop 字体文件归位其父目录，多个不同字体目录整批拒绝。
    [Theory]
    [InlineData(new string[0], true)]
    [InlineData(new[] { @"C:\fonts" }, true)]
    [InlineData(new[] { @"C:\fonts", @"c:\FONTS" }, true)]
    [InlineData(new[] { @"C:\fonts", @"C:\other" }, false)]
    public void Font_inputs_may_share_one_normalized_directory(string[] directories, bool expected) =>
        Assert.Equal(expected, InputRules.AcceptsFontDirectories(directories));

    // 对应 --media-drop “一个音频容器自动导入多条音轨，真实 ID 不按位置猜测”及已加入轨道拒绝。
    [Theory]
    [InlineData(true, false, 2, 9, false)]
    [InlineData(true, false, 2, 2, true)]
    [InlineData(true, true, 2, 9, true)]
    [InlineData(false, false, 2, 2, false)]
    [InlineData(false, true, 2, 2, false)]
    public void Import_duplicates_use_subtitle_file_or_audio_track_identity(bool samePath, bool subtitle, int existingId, int candidateId, bool expected) =>
        Assert.Equal(expected, InputRules.ImportedTrackConflicts(samePath, subtitle, existingId, candidateId));
}
