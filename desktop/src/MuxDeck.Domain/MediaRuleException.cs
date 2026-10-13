namespace MuxDeck.Domain;

// Domain 不依赖 System.IO；旧边界将规则拒绝转换为原有数据异常与文案。
public sealed class MediaRuleException(string message) : Exception(message);
