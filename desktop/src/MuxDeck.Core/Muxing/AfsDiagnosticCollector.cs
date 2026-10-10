using System.Text;

namespace MuxDeck.Core.Muxing;

/// <summary>按真实行边界重组有界进程块；完整日志另行保存，不能靠截断尾部猜测告警。</summary>
internal sealed class AfsDiagnosticCollector(List<MuxWarning>? warnings)
{
    private const int MaximumLineLength = 64 * 1024;
    private readonly StringBuilder[] lines = [new(), new()];
    private readonly bool[] observed = new bool[2];
    private readonly bool[] oversized = new bool[2];
    private readonly Dictionary<(string Code, string Message), int>[] realtimeCounts = [new(), new()];
    private readonly Dictionary<(string Code, string Message), int>[] returnedCounts = [new(), new()];
    private readonly object gate = new();
    public AfsDiagnostic? FirstFatal { get; private set; }

    public void Observe(ProcessLine line)
    {
        lock (gate)
        {
            var stream = line.IsError ? 1 : 0;
            observed[stream] = true;
            Append(stream, line.Text, !line.IsPartial);
        }
    }

    public void Complete(ProcessResult result)
    {
        lock (gate)
        {
            foreach (var stream in new[] { 0, 1 })
            {
                var text = stream == 0 ? result.StandardOutput : result.StandardError;
                if (observed[stream])
                {
                    if (lines[stream].Length > 0 || oversized[stream]) Append(stream, "", complete: true);
                    // 返回缓存可能包含未实时报告的行，独立计数后按每流最大值去重。
                    ScanReturned(stream, text, result.CaptureLimitExceeded);
                    MergeReturnedWarnings(stream);
                    continue;
                }
                // 可替换 runner 没有报告实时输出时仍分类其返回值；截断返回值无法可靠排除后续错误。
                if (result.CaptureLimitExceeded && text.Length > 0)
                {
                    SetFatal(new(AfsDiagnosticKind.Fatal, "AFS_DIAGNOSTIC_TRUNCATED",
                        "AFS 诊断输出已截断且未提供完整实时行，无法可靠分类，已阻止。"));
                    continue;
                }
                ScanReturned(stream, text, truncated: false);
                MergeReturnedWarnings(stream);
            }
        }
    }

    private void ScanReturned(int stream, string text, bool truncated)
    {
        using var reader = new StringReader(text);
        var buffer = new char[8192];
        int count;
        while ((count = reader.Read(buffer)) > 0)
        {
            var start = 0;
            for (var i = 0; i < count; i++)
                if (buffer[i] == '\n')
                {
                    Append(stream, new string(buffer, start, i - start), complete: true, returned: true);
                    start = i + 1;
                }
            if (start < count) Append(stream, new string(buffer, start, count - start), complete: false, returned: true);
        }
        if (lines[stream].Length > 0 || oversized[stream])
        {
            // 有实时完整行时，缓存前缀最后一条可能被截在中间，不能拿半条消息重新误判。
            if (truncated) { lines[stream].Clear(); oversized[stream] = false; }
            else Append(stream, "", complete: true, returned: true);
        }
    }

    private void Append(int stream, string text, bool complete, bool returned = false)
    {
        if (!oversized[stream])
        {
            if (text.Length > MaximumLineLength - lines[stream].Length)
            {
                oversized[stream] = true;
                lines[stream].Clear();
                SetFatal(new(AfsDiagnosticKind.Fatal, "AFS_DIAGNOSTIC_TOO_LONG",
                    "AFS 单行诊断超过 64 KiB，无法可靠分类，已阻止。"));
            }
            else lines[stream].Append(text);
        }
        if (!complete) return;
        if (!oversized[stream])
        {
            var diagnostic = AfsDiagnosticClassifier.Classify(lines[stream].ToString());
            if (diagnostic.Kind == AfsDiagnosticKind.Fatal) SetFatal(diagnostic);
            else if (diagnostic.Kind == AfsDiagnosticKind.Warning)
            {
                var key = (diagnostic.Code, diagnostic.Message);
                var counts = returned ? returnedCounts[stream] : realtimeCounts[stream];
                counts[key] = checked(counts.GetValueOrDefault(key) + 1);
                // 实时完整行立即可见，即使工具随后取消或抛错也保留此前提示；部分行不分类。
                if (!returned) AddWarning(key, 1);
            }
        }
        lines[stream].Clear();
        oversized[stream] = false;
    }

    private void MergeReturnedWarnings(int stream)
    {
        foreach (var (key, count) in returnedCounts[stream])
        {
            // 实时计数已经写入任务结果，只补最终返回值比实时多出的次数。
            var additional = Math.Max(realtimeCounts[stream].GetValueOrDefault(key), count) -
                realtimeCounts[stream].GetValueOrDefault(key);
            if (additional > 0) AddWarning(key, additional);
        }
    }

    private void AddWarning((string Code, string Message) key, int occurrences)
    {
        if (warnings is null) return;
        var index = warnings.FindIndex(warning => warning.Code == key.Code && warning.Message == key.Message);
        if (index < 0) warnings.Add(new MuxWarning(key.Code, key.Message, occurrences));
        else warnings[index] = warnings[index] with { Occurrences = checked(warnings[index].Occurrences + occurrences) };
    }

    private void SetFatal(AfsDiagnostic diagnostic) => FirstFatal ??= diagnostic;
}
