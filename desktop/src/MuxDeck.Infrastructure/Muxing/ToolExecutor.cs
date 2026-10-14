using System.Text;
using System.Text.Json;
using MuxDeck.Contracts;
using MuxDeck.Application.Ports;
using MuxDeck.Application.Ports.Failures;

namespace MuxDeck.Infrastructure.Muxing;

public sealed class ToolExecutor(IMuxToolRunner? runner = null)
{
    private readonly IMuxToolRunner runner = runner ?? new MuxToolRunner();
    public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> args, string? work,
        Action<string>? log, CancellationToken token, TimeSpan timeout,
        bool json = false, bool rejectDiagnostics = false, List<Diagnostic>? warnings = null) =>
        PortBoundary.Guard(() => RunCoreAsync(executable, args, work, log, token, timeout, json, rejectDiagnostics, warnings));

    internal Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> args, ToolCallContext context,
        CancellationToken token, TimeSpan timeout, bool json = false, bool rejectDiagnostics = false,
        List<Diagnostic>? warnings = null) =>
        RunAsync(executable, args, context.Workspace.WorkDirectory,
            message => WriteLogSafely(context.Workspace, message), token, timeout, json, rejectDiagnostics, warnings);
    // 日志只提供诊断证据；磁盘或已释放句柄不能改变工具退出、诊断与取消结果。
    private static void WriteLogSafely(ITaskWorkspace workspace, string message)
    {
        try { workspace.WriteLogAsync(message).GetAwaiter().GetResult(); }
        catch (Exception ex) when (ex is ToolFailureException or IOException or ObjectDisposedException) { }
    }

    private async Task<ProcessResult> RunCoreAsync(string executable, IReadOnlyList<string> args,
        string? work, Action<string>? log, CancellationToken token, TimeSpan timeout,
        bool json = false, bool rejectDiagnostics = false, List<Diagnostic>? warnings = null)
    {
        log?.Invoke("工具 " + executable + " " + JsonSerializer.Serialize(args));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(timeout);
        var diagnostics = rejectDiagnostics ? new AfsDiagnosticCollector(warnings) : null;
        var sink = new InlineProgress<ProcessLine>(line =>
        {
            log?.Invoke((line.IsError ? "stderr " : "stdout ") + line.Text);
            diagnostics?.Observe(line);
        });
        try
        {
            IReadOnlyDictionary<string, string>? environment = work is null ? null :
                new Dictionary<string, string> { ["TEMP"] = work, ["TMP"] = work };
            var result = await runner.RunAsync(new ProcessRequest(executable, args, work, Encoding.UTF8,
                environment, json ? 8 * 1024 * 1024 : 1024 * 1024), sink, lifetime.Token).ConfigureAwait(false);
            diagnostics?.Complete(result);
            if (result.ExitCode != 0 || diagnostics?.FirstFatal is not null)
                throw new ToolFailureException(result.ExitCode != 0 ? ErrorCodes.ToolExitFailed : ErrorCodes.ToolFatalDiagnostic, $"{Path.GetFileName(executable)} " +
                    (result.ExitCode == 0 ? "诊断阻止封装" : "执行失败") + $"（退出码 {result.ExitCode}）：" +
                    (diagnostics?.FirstFatal?.Message ?? Tail(result.StandardError + result.StandardOutput)),
                    result.StandardError + result.StandardOutput);
            if (json && result.CaptureLimitExceeded) throw new ToolFailureException(ErrorCodes.ToolOutputInvalid, "探测 JSON 超出 8 MiB 限制，未使用截断内容判定。");
            return result;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            var ex = new TimeoutException($"工具 {Path.GetFileName(executable)} 超时（{timeout}）。");
            throw PortBoundary.Failure(ErrorCodes.ToolTimeout, ex);
        }
    }

    private static string Tail(string value) => value.Length <= 600 ? value.Trim() : value[^600..].Trim();
    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }
}
