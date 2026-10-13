namespace MuxDeck.Infrastructure.Muxing;

/// <summary>只替换外部进程边界，供真实工具调用和失败场景验证共用。</summary>
public interface IMuxToolRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessLine>? progress,
        CancellationToken cancellationToken);
}

public sealed class MuxToolRunner : IMuxToolRunner
{
    private readonly ProcessRunner runner = new();
    public Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessLine>? progress,
        CancellationToken cancellationToken) => runner.RunAsync(request, progress, cancellationToken);
}
