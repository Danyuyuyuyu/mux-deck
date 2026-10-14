using MuxDeck.Contracts;
using MuxDeck.Core;
using MuxDeck.Infrastructure;
using MuxDeck.Core.Muxing;
using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Core.Tests;

public sealed class LegacyMuxContractConverterTests
{
    private static readonly ToolConfiguration Tools = new("tools", "logs", "cache", "独立 fonttools");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("电影 输出.mkv")]
    public void Request_round_trip_preserves_collections_languages_flags_and_output_name(string? name)
    {
        var subtitles = new[]
        {
            new SubtitleInput("简体.ass", "", "zh-Hans", false, false),
            new SubtitleInput("繁体.srt", "繁體", "zh-Hant", true, false),
            new SubtitleInput("日本語.ass", "  原名  ", "jpn", false, true),
            new SubtitleInput("未知.srt", "字幕", "x-private", true, true)
        };
        var audios = new[]
        {
            new AudioInput("双轨.mka", 7, "中文", "zho", true, false),
            new AudioInput("双轨.mka", 21, "", "", false, true)
        };
        var original = new MuxRequest("中文 空格/video.mkv", subtitles, "", "输出", name) { Audios = audios };
        var spec = LegacyMuxContractConverter.ToSpec(original, Tools);
        var restored = LegacyMuxContractConverter.ToLegacyRequest(spec);
        Assert.Equal(original.VideoPath, restored.VideoPath);
        Assert.Equal(original.FontsDirectory, restored.FontsDirectory);
        Assert.Equal(original.OutputDirectory, restored.OutputDirectory);
        Assert.Equal(name, restored.OutputFileName);
        Assert.Equal(subtitles, restored.Subtitles);
        Assert.Equal(audios, restored.Audios);
        Assert.Equal(6, spec.ExternalTracks.Length);
        Assert.Empty(spec.SourceTracks);
        Assert.Equal(Tools, spec.Tools);
    }

    [Fact]
    public void Empty_collections_round_trip_without_inventing_tracks()
    {
        var original = new MuxRequest("video", [], "", "output") { Audios = [] };
        var restored = LegacyMuxContractConverter.ToLegacyRequest(LegacyMuxContractConverter.ToSpec(original, Tools));
        Assert.Empty(restored.Subtitles);
        Assert.Empty(restored.Audios);
        Assert.Null(restored.OutputFileName);
    }

    [Fact]
    public void Request_conversion_copies_mutable_source_collections()
    {
        var subtitles = new List<SubtitleInput> { new("one.ass", "原名", "und", true) };
        var audios = new List<AudioInput> { new("one.mka", 3, "原音", "en", false, true) };
        var spec = LegacyMuxContractConverter.ToSpec(new MuxRequest("video", subtitles, "fonts", "output")
            { Audios = audios }, Tools);
        subtitles[0] = subtitles[0] with { Language = "ja", IsDefault = false };
        audios.Clear();
        var restored = LegacyMuxContractConverter.ToLegacyRequest(spec);
        Assert.Equal("und", Assert.Single(restored.Subtitles).Language);
        Assert.True(restored.Subtitles[0].IsDefault);
        Assert.Equal(3, Assert.Single(restored.Audios).TrackId);
    }

    [Fact]
    public void Converted_legacy_collections_cannot_be_mutated()
    {
        var request = new MuxRequest("video", [new("one.srt", "", "", true)], "", "output")
            { Audios = [new("audio.mka", 5, "", "", false, true)] };
        var restored = LegacyMuxContractConverter.ToLegacyRequest(LegacyMuxContractConverter.ToSpec(request, Tools));
        Assert.Throws<NotSupportedException>(() => ((IList<SubtitleInput>)restored.Subtitles).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<AudioInput>)restored.Audios).Clear());
    }

    [Fact]
    public void Tool_and_directory_settings_are_captured_from_the_supplied_workspace()
    {
        var workspace = new Workspace("root") { ToolsDirectoryOverride = "old-tools",
            StorageOverride = new StoragePaths("data", "old-tools", "config", "old-logs", "old-cache"),
            FontToolsDirectoryOverride = "old-fonttools" };
        var spec = LegacyMuxContractConverter.ToSpec(new MuxRequest("video", [], "", "output"), workspace);
        workspace = workspace with { ToolsDirectoryOverride = "new-tools", FontToolsDirectoryOverride = null };
        Assert.Equal(new ToolConfiguration("old-tools", "old-logs", "old-cache", "old-fonttools"), spec.Tools);
        Assert.NotEqual(workspace.ToolsDirectory, spec.Tools.ToolsDirectory);
    }

    [Fact]
    public void Source_selection_is_rejected_instead_of_lost_at_the_legacy_boundary()
    {
        var spec = EmptySpec() with { SourceTracks = [new(new TrackId(1), TrackKind.Audio, false)] };
        Assert.Throws<NotSupportedException>(() => LegacyMuxContractConverter.ToLegacyRequest(spec));
    }

    [Theory]
    [InlineData(TrackKind.Video, null)]
    [InlineData(TrackKind.Unknown, null)]
    [InlineData(TrackKind.Attachment, null)]
    [InlineData(TrackKind.Audio, null)]
    [InlineData(TrackKind.Subtitle, 4)]
    public void Unsupported_track_shapes_are_rejected(TrackKind kind, int? id)
    {
        var spec = EmptySpec() with { ExternalTracks = [new("input", kind,
            id is { } value ? new TrackId(value) : null, "", new LanguageTag("und"), new(false, false))] };
        Assert.Throws<NotSupportedException>(() => LegacyMuxContractConverter.ToLegacyRequest(spec));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Uninitialized_collections_are_rejected(bool external)
    {
        var spec = external ? EmptySpec() with { ExternalTracks = default } : EmptySpec() with { SourceTracks = default };
        Assert.Throws<ArgumentException>(() => LegacyMuxContractConverter.ToLegacyRequest(spec));
    }

    [Theory]
    [InlineData(MuxStatus.Succeeded, MuxJobStatus.Succeeded)]
    [InlineData(MuxStatus.Failed, MuxJobStatus.Failed)]
    [InlineData(MuxStatus.Cancelled, MuxJobStatus.Cancelled)]
    public void Results_preserve_status_checks_diagnostics_artifact_and_log(MuxStatus oldStatus, MuxJobStatus status)
    {
        var text = new[] { "通过：视频", "失败？这只是旧文本\r\n第二行  ", "", "通过：视频" };
        var warnings = new[] { new MuxWarning("AFS_COLOR_HIGH_BYTE_IGNORED", "原始消息", 3),
            new MuxWarning("FONT_SYNTHETIC_BOLD", "另一条", 2) };
        var result = new MuxResult(oldStatus, "成品.mkv", oldStatus == MuxStatus.Succeeded ? null : "原始原因",
            "task.log", text) { Warnings = warnings };
        var converted = LegacyMuxContractConverter.ToResult(result);
        Assert.Equal(status, converted.Status);
        Assert.Equal(text, converted.Checks.Select(check => check.LegacyText));
        Assert.All(converted.Checks, check =>
        {
            Assert.Equal(ErrorCodes.LegacyCheck, check.Code);
            Assert.Equal(CheckStatus.NotEvaluated, check.Status);
            Assert.Null(check.Sequence);
            Assert.Null(check.ExpectedHash);
        });
        Assert.Equal(warnings.Select(w => (w.Code, w.Message, w.Occurrences)),
            converted.Diagnostics.Select(d => (d.Code, d.Message, d.Occurrences)));
        Assert.All(converted.Diagnostics, diagnostic => Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity));
        Assert.Equal("成品.mkv", Assert.Single(converted.Outputs).Path);
        Assert.Equal("task.log", converted.Log?.Path);
        if (oldStatus == MuxStatus.Succeeded)
            Assert.Null(converted.Failure);
        else
        {
            Assert.Equal(oldStatus == MuxStatus.Cancelled ? ErrorCodes.Cancelled : ErrorCodes.LegacyFailure,
                converted.Failure?.Code);
            Assert.Equal(JobStage.Unknown, converted.Failure?.Stage);
            Assert.Equal("原始原因", converted.Failure?.Message);
        }
    }

    [Theory]
    [InlineData(MuxStatus.Failed)]
    [InlineData(MuxStatus.Cancelled)]
    public void Missing_legacy_error_log_and_output_are_not_invented(MuxStatus status)
    {
        var converted = LegacyMuxContractConverter.ToResult(new MuxResult(status, null, null, null, []));
        Assert.Null(converted.Failure?.Message);
        Assert.Null(converted.Log);
        Assert.Empty(converted.Outputs);
        Assert.Empty(converted.Checks);
        Assert.Empty(converted.Diagnostics);
    }

    [Fact]
    public void Result_conversion_copies_legacy_collections()
    {
        var checks = new List<string> { "检查原文  " };
        var warnings = new List<MuxWarning> { new("FONT_INTRINSIC_WEIGHT", "消息", 2) };
        var converted = LegacyMuxContractConverter.ToResult(new(MuxStatus.Succeeded, null, null, null, checks)
            { Warnings = warnings });
        checks.Clear();
        warnings.Clear();
        Assert.Equal("检查原文  ", Assert.Single(converted.Checks).LegacyText);
        Assert.Equal(2, Assert.Single(converted.Diagnostics).Occurrences);
    }

    [Fact]
    public void Unknown_legacy_status_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LegacyMuxContractConverter.ToResult(
            new((MuxStatus)99, null, null, null, [])));
    }


    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("自定义.mkv")]
    public void Representable_spec_round_trip_preserves_all_fields(string? fileName)
    {
        var original = EmptySpec() with
        {
            ExternalTracks = [
                new("subtitle.srt", TrackKind.Subtitle, null, "", new(" zh-Hant "), new(false, true)),
                new("audio.mka", TrackKind.Audio, new TrackId(-1), "原音", new(""), new(true, false))],
            Output = new("目录", fileName),
            Fonts = new("字体目录")
        };
        var restored = LegacyMuxContractConverter.ToSpec(LegacyMuxContractConverter.ToLegacyRequest(original), original.Tools);
        Assert.Equal(original.VideoPath, restored.VideoPath);
        Assert.Equal(original.ExternalTracks.ToArray(), restored.ExternalTracks.ToArray());
        Assert.Equal(original.SourceTracks.ToArray(), restored.SourceTracks.ToArray());
        Assert.Equal(original.Output, restored.Output);
        Assert.Equal(original.Fonts, restored.Fonts);
        Assert.Equal(original.Tools, restored.Tools);
    }

    [Fact]
    public void Cross_kind_order_that_legacy_cannot_represent_is_rejected()
    {
        var spec = EmptySpec() with { ExternalTracks = [
            new("audio.mka", TrackKind.Audio, new TrackId(1), "", new("und"), new(false, false)),
            new("subtitle.srt", TrackKind.Subtitle, null, "", new("und"), new(false, false))] };
        Assert.Throws<NotSupportedException>(() => LegacyMuxContractConverter.ToLegacyRequest(spec));
    }

    private static MuxJobSpec EmptySpec() => new("video", [], [], new(""), new("output"), Tools);
}
