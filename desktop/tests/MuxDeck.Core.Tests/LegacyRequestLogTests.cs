using System.Text.Json;
using MuxDeck.Core;
using MuxDeck.Core.Muxing;
using Xunit;

namespace MuxDeck.Core.Tests;

public sealed class LegacyRequestLogTests
{
    [Fact]
    public async Task Old_entry_writes_exact_legacy_request_after_validation_and_keeps_use_case_log()
    {
        var root = Path.Combine(Path.GetTempPath(), "MuxDeck-request-log-" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "output");
        Directory.CreateDirectory(output);
        var video = Path.Combine(root, "视频.mkv");
        var subtitle = Path.Combine(root, "字幕.srt");
        await File.WriteAllTextAsync(video, "fixture", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(subtitle, "1\n00:00:00,000 --> 00:00:01,000\nHello\n", TestContext.Current.CancellationToken);
        // 已有目标在请求行写入之后拒绝，避免本日志用例调用真实工具。
        var target = Path.Combine(output, "final.mkv");
        await File.WriteAllTextAsync(target, "original", TestContext.Current.CancellationToken);
        var subtitles = new[] { new SubtitleInput(Path.Combine(root, ".", "字幕.srt"), "中文 \"名称\"", "zh-Hans", true, true) };
        var request = new MuxRequest(Path.Combine(root, ".", "视频.mkv"), subtitles, "", output, "final.mkv");
        // 期望来自旧版日志协议：字段顺序和 escaping 使用原 MuxRequest 默认序列化格式。
        var expected = new MuxRequest(video,
            [new(subtitle, "中文 \"名称\"", "zh-Hans", true, true)], "", output, "final.mkv");
        var result = await new SingleMuxService(new(root)).RunAsync(request,
            new Sink(value => { if (value.Stage == MuxStage.Validating) subtitles[0] = new("changed.srt", "", "en", false); }),
            TestContext.Current.CancellationToken);
        Assert.Equal(MuxStatus.Failed, result.Status);
        Assert.Equal("目标已存在，拒绝覆盖：" + target, result.Error);
        var lines = await File.ReadAllLinesAsync(result.LogPath!, TestContext.Current.CancellationToken);
        var requestLine = Assert.Single(lines, line => line.Contains(" 请求 ", StringComparison.Ordinal));
        Assert.Equal("请求 " + JsonSerializer.Serialize(expected), requestLine[(requestLine.IndexOf(" 请求 ", StringComparison.Ordinal) + 1)..]);
        Assert.Contains(lines, line => line.Contains(" SPEC ", StringComparison.Ordinal));
        Assert.True(Array.FindIndex(lines, line => line.Contains("阶段 Validating", StringComparison.Ordinal)) <
            Array.FindIndex(lines, line => line.Contains(" 请求 ", StringComparison.Ordinal)));
        Assert.Equal("original", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
    }

    private sealed class Sink(Action<MuxProgress> action) : IProgress<MuxProgress>
    { public void Report(MuxProgress value) => action(value); }
}
