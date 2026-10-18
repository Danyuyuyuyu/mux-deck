using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Domain.Tests;

public sealed class OutputNamingTests
{
    // 对应 --single-mux-round1 的“中文空格大写 MKV 接纳与默认输出名”与输出名编辑。
    [Theory]
    [InlineData(null, "源 视频", "源 视频.muxed.mkv")]
    [InlineData("", "源 视频", "源 视频.muxed.mkv")]
    [InlineData(" ", "源 视频", "源 视频.muxed.mkv")]
    [InlineData(null, null, null)]
    [InlineData(null, "", ".muxed.mkv")]
    [InlineData(null, "movie.part.1", "movie.part.1.muxed.mkv")]
    [InlineData("自定义.MKV", "源 视频", "自定义.MKV")]
    [InlineData(" custom.mkv ", null, " custom.mkv ")]
    public void Preview_uses_automatic_name_only_for_blank_requests(string? requested, string? baseName, string? expected) =>
        Assert.Equal(expected, OutputNaming.ResolvePreview(requested, baseName));

    // 对应 Smoke 的成品命名与拒绝非法请求；服务对空白自定义名称的校验仍在原边界执行。
    [Theory]
    [InlineData(null, "源 视频.muxed.mkv")]
    [InlineData("", "")]
    [InlineData(" ", " ")]
    [InlineData("custom.MKV", "custom.MKV")]
    [InlineData("subfolder/custom.mkv", "subfolder/custom.mkv")]
    public void Job_name_defaults_only_for_null_and_preserves_input_for_validation(string? requested, string expected) =>
        Assert.Equal(expected, OutputNaming.ResolveForJob(requested, "源 视频"));
}
