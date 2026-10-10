using System.Diagnostics;
using System.Text;

namespace MuxDeck.Core;

public sealed record ProcessRequest(string Executable, IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null, Encoding? OutputEncoding = null,
    IReadOnlyDictionary<string, string>? Environment = null, int MaxCapturedCharacters = 1_048_576);
public sealed record ProcessLine(bool IsError, string Text, bool IsPartial = false);
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError,
    bool CaptureLimitExceeded = false);

/// <summary>不经过 shell 执行工具，同时排空两路输出，取消时终止进程树。</summary>
public sealed class ProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessRequest request,
        IProgress<ProcessLine>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(request.Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = request.OutputEncoding ?? Encoding.UTF8,
            StandardErrorEncoding = request.OutputEncoding ?? Encoding.UTF8
        };
        if (request.WorkingDirectory is not null) start.WorkingDirectory = request.WorkingDirectory;
        if (request.MaxCapturedCharacters < 1) throw new ArgumentOutOfRangeException(nameof(request));
        if (request.Environment is not null)
            foreach (var entry in request.Environment) start.Environment[entry.Key] = entry.Value;
        foreach (var argument in request.Arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        process.Start();
        using var job = WindowsProcessJob.TryCreate(process);

        using var registration = cancellationToken.Register(() =>
        {
            job?.Terminate();
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        });
        var stdout = DrainAsync(process.StandardOutput, false, progress, request.MaxCapturedCharacters, cancellationToken);
        var stderr = DrainAsync(process.StandardError, true, progress, request.MaxCapturedCharacters, cancellationToken);
        var streams = Task.WhenAll(stdout, stderr);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            // 工具父进程已经结束，残存后代不能继续占用输出管道或写任务目录。
            job?.Terminate();
            var output = await streams.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new ProcessResult(process.ExitCode, output[0].Text, output[1].Text,
                output[0].Truncated || output[1].Truncated);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            job?.Terminate();
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            process.StandardOutput.Dispose();
            process.StandardError.Dispose();
            try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); }
            catch (TimeoutException) { throw new IOException("工具取消后仍未退出，不能确认已停止。"); }
            try { await streams.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (Exception streamError) when (streamError is OperationCanceledException or ObjectDisposedException or IOException or TimeoutException) { }
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException("工具父进程已退出，但输出管道未在限定时间内关闭，疑似仍有后代进程。", ex);
        }
    }

    private static async Task<(string Text, bool Truncated)> DrainAsync(StreamReader reader, bool isError,
        IProgress<ProcessLine>? progress, int limit, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var line = new StringBuilder();
        var buffer = new char[8192];
        var partialLine = false;
        var truncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            // 分块读取避免无换行输出先分配巨型字符串；捕获完整 JSON，超限只标记、不解析截断值。
            var remaining = limit - text.Length;
            if (read > remaining) truncated = true;
            if (remaining > 0) text.Append(buffer.AsSpan(0, Math.Min(read, remaining)));
            for (var i = 0; i < read; i++)
            {
                if (buffer[i] == '\n')
                {
                    if (line.Length > 0 && line[^1] == '\r') line.Length--;
                    progress?.Report(new ProcessLine(isError, line.ToString()));
                    line.Clear();
                    partialLine = false;
                }
                else
                {
                    line.Append(buffer[i]);
                    if (line.Length == buffer.Length)
                    {
                        progress?.Report(new ProcessLine(isError, line.ToString(), IsPartial: true));
                        line.Clear();
                        partialLine = true;
                    }
                }
            }
        }
        if (line.Length > 0 || partialLine) progress?.Report(new ProcessLine(isError, line.ToString()));
        return (text.ToString(), truncated);
    }
}
