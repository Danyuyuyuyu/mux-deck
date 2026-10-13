namespace MuxDeck.Application.Ports;

// 中间文件只是任务工作区引用；仅发布成功后由 Application 转换为界面的最终产物。
public sealed record WorkFile(string Path);
