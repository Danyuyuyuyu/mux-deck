using System.Diagnostics;
using System.Text;

namespace MuxDeck.Core;

public sealed record ProcessRequest(string Executable, IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null, Encoding? OutputEncoding = null);
public sealed record ProcessLine(bool IsError, string Text);
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Runs tools without a shell, drains both streams, and cancels the entire process tree.</summary>
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
        foreach (var argument in request.Arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        process.Start();

        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        });
        var stdout = DrainAsync(process.StandardOutput, false, progress);
        var stderr = DrainAsync(process.StandardError, true, progress);
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        var output = await stdout.ConfigureAwait(false);
        var error = await stderr.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new ProcessResult(process.ExitCode, output, error);
    }

    private static async Task<string> DrainAsync(StreamReader reader, bool isError, IProgress<ProcessLine>? progress)
    {
        var text = new StringBuilder();
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            text.AppendLine(line);
            progress?.Report(new ProcessLine(isError, line));
        }
        return text.ToString();
    }
}
