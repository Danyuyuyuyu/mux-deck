using MuxDeck.Domain;

namespace MuxDeck.Infrastructure.Muxing;

public enum AfsDiagnosticKind { LogOnly, Warning, Fatal }
public sealed record AfsDiagnostic(AfsDiagnosticKind Kind, string Code, string Message);

// 保留旧入口与数据形状，分类和唯一白名单只由 Domain 维护。
public static class AfsDiagnosticClassifier
{
    public const string ColorHighByteIgnored = DiagnosticPolicy.ColorHighByteIgnored;

    public static AfsDiagnostic Classify(string line)
    {
        var diagnostic = DiagnosticPolicy.Classify(line);
        var kind = diagnostic.Kind switch
        {
            DiagnosticDisposition.LogOnly => AfsDiagnosticKind.LogOnly,
            DiagnosticDisposition.Warning => AfsDiagnosticKind.Warning,
            _ => AfsDiagnosticKind.Fatal
        };
        return new(kind, diagnostic.Code, diagnostic.Message);
    }
}
