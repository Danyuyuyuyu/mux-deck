using MuxDeck.Domain;
using Xunit;

namespace MuxDeck.Domain.Tests;

public sealed class DiagnosticPolicyTests
{
    // 对应 --warning-policy 的唯一颜色白名单、精确匹配与危险后缀拒绝。
    [Theory]
    [InlineData(@"\c")]
    [InlineData(@"\1c")]
    [InlineData(@"\2c")]
    [InlineData(@"\3c")]
    [InlineData(@"\4c")]
    public void Only_verified_color_tags_can_continue(string tag)
    {
        var result = DiagnosticPolicy.Classify("|WRN|ASS color tag " + tag + " has more than 6 hex digits; high byte ignored.");
        Assert.Equal(DiagnosticDisposition.Warning, result.Kind);
        Assert.Equal("AFS_COLOR_HIGH_BYTE_IGNORED", result.Code);
    }

    [Theory]
    [InlineData("|WRN|missing font", "AFS_UNKNOWN_WARNING")]
    [InlineData("|WRN|ASS color tag \\c has more than 6 hex digits; high byte ignored. missing font", "AFS_UNKNOWN_WARNING")]
    [InlineData("|WRN|ASS color tag \\5c has more than 6 hex digits; high byte ignored.", "AFS_UNKNOWN_WARNING")]
    [InlineData("|WRN|ASS color tag \\c has more than 6 hex digits; high byte ignored.\nERR: missing font", "AFS_INVALID_DIAGNOSTIC_LINE")]
    [InlineData("|WARNING|missing font", "AFS_UNKNOWN_LOG_LEVEL")]
    [InlineData("|ERR|missing font", "AFS_ERROR")]
    [InlineData("|FTL|failed", "AFS_ERROR")]
    [InlineData("WARNING: unknown", "AFS_UNKNOWN_WARNING")]
    [InlineData("Not found font file: Arial", "AFS_ERROR")]
    [InlineData("Duplicate fonts: Arial", "AFS_ERROR")]
    [InlineData("2026-10-13 00:00:00|WRN|unsafe", "AFS_UNPARSEABLE_DIAGNOSTIC")]
    public void Unknown_or_unreliable_diagnostics_stop_the_job(string line, string code)
    {
        var result = DiagnosticPolicy.Classify(line);
        Assert.Equal(DiagnosticDisposition.Fatal, result.Kind);
        Assert.Equal(code, result.Code);
    }

    // 对应普通 INF/DBG 不被宽泛关键词误判、ANSI 与 ISO 物理行。
    [Theory]
    [InlineData("|INF|missing WRN text")]
    [InlineData("|DBG|details")]
    [InlineData("|TRC|details")]
    [InlineData("|VRB|details")]
    [InlineData("C:/folder/WRN/missing.ass")]
    [InlineData("\u001b[32m2026-10-13T00:00:00+08:00|INF|details\u001b[0m\r\n")]
    public void Ordinary_records_remain_log_only(string line) =>
        Assert.Equal(DiagnosticDisposition.LogOnly, DiagnosticPolicy.Classify(line).Kind);

    [Fact]
    public void Null_diagnostic_is_rejected() =>
        Assert.Throws<ArgumentNullException>(() => DiagnosticPolicy.Classify(null!));
}
