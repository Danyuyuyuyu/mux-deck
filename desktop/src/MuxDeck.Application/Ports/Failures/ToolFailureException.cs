using System.Reflection;
using MuxDeck.Contracts;

namespace MuxDeck.Application.Ports.Failures;

/// <summary>端口的统一失败；原始输出、退出码或 Fatal 原因放入 Detail，不根据文案猜测错误码。</summary>
public sealed class ToolFailureException : Exception
{
    // 唯一来源是 Contracts 集中常量，不在端口维护另一份字符串错误码表。
    private static readonly HashSet<string> KnownCodes = typeof(ErrorCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToHashSet(StringComparer.Ordinal);

    public ToolFailureException(string code, string message, string? detail = null)
        : this(code, message, detail, null) { }

    // 保留原始异常与堆栈，适配边界不把系统错误压成不可追溯的文案。
    public ToolFailureException(string code, string message, string? detail, Exception? innerException)
        : base(message, innerException)
    {
        if (!KnownCodes.Contains(code))
            throw new ArgumentOutOfRangeException(nameof(code), code, "端口错误码必须来自 Contracts.ErrorCodes。");
        Code = code;
        Detail = detail;
    }

    public string Code { get; }
    public string? Detail { get; }
}
