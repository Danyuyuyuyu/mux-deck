namespace MuxDeck.Domain;

public static class OutputNaming
{
    // 路径拆分由调用边界完成；null 表示尚无源视频，空文件名仍保留原拼接行为。
    public static string ResolvePreview(string? requestedName, string? sourceBaseName) =>
        string.IsNullOrWhiteSpace(requestedName) ? DefaultFileName(sourceBaseName) : requestedName;

    // 旧服务只将 null 视为自动命名；空白名称继续交给原校验拒绝。
    public static string ResolveForJob(string? requestedName, string sourceBaseName) =>
        requestedName ?? DefaultFileName(sourceBaseName);

    private static string DefaultFileName(string? sourceBaseName) =>
        (sourceBaseName ?? "源文件名") + ".muxed.mkv";
}
