namespace MuxDeck.Domain;

public static class OutputNaming
{
    // 路径拆分与占位文案由调用边界负责；没有源视频且未指定文件名时返回 null。
    public static string? ResolvePreview(string? requestedName, string? sourceBaseName) =>
        string.IsNullOrWhiteSpace(requestedName)
            ? (sourceBaseName is null ? null : DefaultFileName(sourceBaseName))
            : requestedName;

    // 旧服务只将 null 视为自动命名；空白名称继续交给原校验拒绝。
    public static string ResolveForJob(string? requestedName, string sourceBaseName) =>
        requestedName ?? DefaultFileName(sourceBaseName);

    /// <summary>默认成品名拼接的唯一实现；占位文案由界面提供。</summary>
    public static string DefaultFileName(string sourceBaseName) => sourceBaseName + ".muxed.mkv";
}
