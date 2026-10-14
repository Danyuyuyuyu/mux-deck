using MuxDeck.Contracts;
using MuxDeck.Core.Muxing;
using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Core.Tests;

public sealed class StructuredMuxResultTests
{
    [Theory]
    [InlineData(ErrorCodes.VideoCountMismatch, "video 轨数量与请求不同。")]
    [InlineData(ErrorCodes.AudioCountMismatch, "audio 轨数量与请求不同。")]
    [InlineData(ErrorCodes.VideoMetadataMismatch, "video 轨 3 的编码、媒体属性或轨道元数据与源文件不同。")]
    [InlineData(ErrorCodes.AudioMetadataMismatch, "audio 轨 3 的编码、媒体属性或轨道元数据与源文件不同。")]
    [InlineData(ErrorCodes.DurationMismatch, "成品时长与源文件不符。")]
    [InlineData(ErrorCodes.SubtitleCountMismatch, "新增字幕轨数量不符。")]
    [InlineData(ErrorCodes.SubtitleMetadataMismatch, "新增字幕轨 3 的 ASS 类型、名称、语言、default 或 forced 旗标不符。")]
    [InlineData(ErrorCodes.AddedAudioMetadataMismatch, "外加音频 3 编码、音频属性或元数据不符。")]
    public void Each_track_failure_code_renders_the_original_text(string code, string expected)
    {
        var result = LegacyMuxContractConverter.ToLegacyResult(new(MuxJobStatus.Failed, [], [], [],
            new(code, JobStage.Checking, null) { Sequence = 3 }, null));
        Assert.Equal(expected, result.Error);
    }

    [Fact]
    public void Subtitle_facts_render_original_text_without_deserialization()
    {
        var check = new CheckResult(MuxCheckCodes.SubtitleTrack, CheckStatus.Passed)
        { Sequence = 2, TrackId = new(7), Name = "繁體", Language = new("zh-Hant"), Flags = new(false, true) };
        var result = LegacyMuxContractConverter.ToLegacyResult(new(MuxJobStatus.Succeeded, [], [check], [], null, null));
        Assert.Equal("新增字幕 2（轨 ID 7）：繁體 / zh-Hant / default=False / forced=True", Assert.Single(result.Checks));
    }

    [Theory]
    [InlineData(ErrorCodes.FontBackendNotFound, "任何原始消息", "找不到同目录的 pyftsubset 与 ttx，AFS 需要可运行的 fontTools 后端。")]
    [InlineData(ErrorCodes.ToolNotFound, "找不到其他工具 ttx", "找不到其他工具 ttx")]
    [InlineData(ErrorCodes.ToolNotFound, "找不到其他工具 pyftsubset", "找不到其他工具 pyftsubset")]
    public void Backend_wording_depends_on_capability_code_not_message_suffix(string code, string message, string expected)
    {
        var result = LegacyMuxContractConverter.ToLegacyResult(new(MuxJobStatus.Failed, [], [], [],
            new(code, JobStage.Validating, message), null));
        Assert.Equal(expected, result.Error);
    }

    [Fact]
    public void Subset_progress_reads_sequence_total_and_diagnostic_values()
    {
        var item = LegacyMuxContractConverter.ToLegacyProgress(new(JobStage.Subsetting, "SUBSET_ITEM", null, null)
        { Sequence = 2, Total = 3 });
        Assert.Equal("子集化字幕 2/3", item!.Message);
        var warnings = LegacyMuxContractConverter.ToLegacyProgress(new(JobStage.Subsetting, "SUBSET_DIAGNOSTICS", null, null)
        { Diagnostics = [new("warning", DiagnosticSeverity.Warning, "原提示", 2)] });
        Assert.Equal("子集化注意提示：原提示（累计 2 次）", warnings!.Message);
    }
}
