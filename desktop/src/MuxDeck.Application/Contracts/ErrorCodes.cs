namespace MuxDeck.Contracts;

/// <summary>集中保存任务错误与过渡检查码；工具诊断码沿用 diagnostics.md，不在这里另立白名单。</summary>
public static class ErrorCodes
{

    public const string LegacyCheck = "MUX_LEGACY_CHECK";
    public const string LegacyFailure = "MUX_LEGACY_FAILURE";
    public const string Cancelled = "MUX_CANCELLED";
}
