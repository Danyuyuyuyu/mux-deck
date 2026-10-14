namespace MuxDeck.Application;

// 用例自身的业务拒绝，区别于外部工具/文件端口失败；仅在 Application 内部传播。
internal sealed class MuxRuleFailureException(string code, string? path = null, int? sequence = null,
    string? detail = null) : Exception
{
    public string Code { get; } = code;
    public string? Path { get; } = path;
    public int? Sequence { get; } = sequence;
    public string? Detail { get; } = detail;
}
