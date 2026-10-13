using System.Text.RegularExpressions;

namespace MuxDeck.Domain;

public enum DiagnosticDisposition { LogOnly, Warning, Fatal }
public sealed record ToolDiagnostic(DiagnosticDisposition Kind, string Code, string Message);

/// <summary>AFS 的可继续告警只能依据已验证的精确消息扩展，不能按宽泛关键词忽略错误。</summary>
public static partial class DiagnosticPolicy
{
    public const string ColorHighByteIgnored = "AFS_COLOR_HIGH_BYTE_IGNORED";

    public static ToolDiagnostic Classify(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var clean = AnsiEscape().Replace(line, "").TrimEnd('\r', '\n');
        if (clean.IndexOfAny(['\r', '\n']) >= 0)
            return new(DiagnosticDisposition.Fatal, "AFS_INVALID_DIAGNOSTIC_LINE",
                "AFS 单次诊断包含多个逻辑行，无法可靠分类，已阻止。");
        var header = Header().Match(clean);
        if (!header.Success)
        {
            if (DiagnosticShape().IsMatch(clean))
                return new(DiagnosticDisposition.Fatal, "AFS_UNPARSEABLE_DIAGNOSTIC",
                    "AFS 日志具有诊断头但格式无法可靠识别，已阻止：" + clean);
            var raw = clean.TrimStart();
            if (RawFailure().IsMatch(raw)) return new(DiagnosticDisposition.Fatal, "AFS_ERROR", raw);
            if (RawWarning().IsMatch(raw)) return new(DiagnosticDisposition.Fatal, "AFS_UNKNOWN_WARNING", raw);
            return new(DiagnosticDisposition.LogOnly, "", clean);
        }
        var message = header.Groups["message"].Value;
        return header.Groups["level"].Value.Trim().ToUpperInvariant() switch
        {
            "INF" or "DBG" or "TRC" or "VRB" => new(DiagnosticDisposition.LogOnly, "", message),
            "WRN" when ColorWarning().IsMatch(message) => new(DiagnosticDisposition.Warning,
                ColorHighByteIgnored, "ASS 颜色标签超过 6 位十六进制，AFS 已忽略高字节；请核对字幕颜色。"),
            "WRN" => new(DiagnosticDisposition.Fatal, "AFS_UNKNOWN_WARNING", "AFS 告警尚未确认可安全继续，已阻止：" + message),
            "ERR" or "FTL" => new(DiagnosticDisposition.Fatal, "AFS_ERROR", message),
            _ => new(DiagnosticDisposition.Fatal, "AFS_UNKNOWN_LOG_LEVEL", "无法可靠识别 AFS 日志级别：" + clean)
        };
    }

    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]")]
    private static partial Regex AnsiEscape();
    [GeneratedRegex(@"^(?:\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2}))?\|(?<level>[^|\r\n]*)\|(?<message>.*)$")]
    private static partial Regex Header();
    [GeneratedRegex(@"^\s*(?:\d{4}-\d{2}-\d{2}[T ][^|\r\n]*)?\|[^|\r\n]*\|")]
    private static partial Regex DiagnosticShape();
    [GeneratedRegex(@"^ASS color tag \\[1-4]?c has more than 6 hex digits; high byte ignored\.$")]
    private static partial Regex ColorWarning();
    [GeneratedRegex(@"^(?:ERROR:|ERR:|FATAL:|Error:|Not found font file:|Duplicate fonts(?:\s|:)|Command execution failed:)")]
    private static partial Regex RawFailure();
    [GeneratedRegex(@"^(?:WARNING:|WARN:|Warning:|Warn:)")]
    private static partial Regex RawWarning();
}
