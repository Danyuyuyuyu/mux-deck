using MuxDeck.Application.Ports;
using MuxDeck.Contracts;

namespace MuxDeck.Infrastructure.Muxing;

public sealed class MediaReferenceBuilder(IMuxToolRunner? runner = null, IToolCatalog? catalog = null) : IMediaReferenceBuilder
{
    public Task<WorkFile> BuildSubtitleReferenceAsync(WorkFile subtitle, int index, ToolCallContext context, CancellationToken cancellationToken = default) =>
        PortBoundary.Guard(async () =>
        {
            var path = Path.Combine(context.Workspace.WorkDirectory, $"ass-reference-{index}.mks");
            await Run(["-o", path, "--sub-charset", "0:UTF-8", subtitle.Path], context, cancellationToken, 10).ConfigureAwait(false);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                throw new InvalidDataException($"新增字幕 {index + 1} 未产出有效 ASS 校验参考容器。");
            return new WorkFile(path);
        });
    public Task<WorkFile> BuildAudioReferenceAsync(ExternalTrackSpec audio, int index, ToolCallContext context, CancellationToken cancellationToken = default) =>
        PortBoundary.Guard(async () =>
        {
            var path = Path.Combine(context.Workspace.WorkDirectory, $"audio-reference-{index}.mka");
            await Run(["-o", path, .. MkvmergeMuxer.AudioArguments(audio)], context, cancellationToken, 20).ConfigureAwait(false);
            return new WorkFile(path);
        });
    private async Task Run(IReadOnlyList<string> args, ToolCallContext context, CancellationToken token, int minutes)
    {
        var executable = await ToolCatalog.Require(catalog ?? new ToolCatalog(), ToolCapability.MuxMedia, context.Tools, token).ConfigureAwait(false);
        await new ToolExecutor(runner).RunAsync(executable, args, context, token, TimeSpan.FromMinutes(minutes)).ConfigureAwait(false);
    }
}
