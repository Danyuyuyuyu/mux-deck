using MuxDeck.Application.Ports;
using MuxDeck.Domain;

namespace MuxDeck.Infrastructure.Muxing;

public sealed class MkvextractExtractor(IMuxToolRunner? runner = null, IToolCatalog? catalog = null) : IMediaExtractor
{
    public async Task ExtractTracksAsync(string sourcePath, EquatableArray<TrackExtraction> tracks,
        ToolCallContext context, CancellationToken cancellationToken = default)
    {
        await PortBoundary.Guard(async () =>
        {
            foreach (var group in tracks.GroupBy(track => track.Format))
            {
                var args = new List<string> { sourcePath, "tracks" };
                if (group.Key == TrackExtractionFormat.FullRaw) args.Add("--fullraw");
                args.AddRange(group.Select(track => $"{track.TrackId.Value}:{track.Destination.Path}"));
                await Extract(args, context, cancellationToken, group.Key == TrackExtractionFormat.FullRaw ? 20 : 10).ConfigureAwait(false);
                foreach (var track in group) RequireFile(track.Destination);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }).ConfigureAwait(false);
    }
    public async Task ExtractAttachmentsAsync(string sourcePath, EquatableArray<AttachmentExtraction> attachments,
        ToolCallContext context, CancellationToken cancellationToken = default)
    {
        await PortBoundary.Guard(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attachments.IsEmpty) return true;
            await Extract([sourcePath, "attachments", .. attachments.Select(item => $"{item.AttachmentId}:{item.Destination.Path}")],
                context, cancellationToken, 10).ConfigureAwait(false);
            foreach (var attachment in attachments) RequireFile(attachment.Destination);
            return true;
        }).ConfigureAwait(false);
    }
    private async Task Extract(IReadOnlyList<string> args, ToolCallContext context, CancellationToken token, int minutes)
    {
        var executable = await ToolCatalog.Require(catalog ?? new ToolCatalog(), ToolCapability.ExtractMedia, context.Tools, token).ConfigureAwait(false);
        await new ToolExecutor(runner).RunAsync(executable, args, context, token, TimeSpan.FromMinutes(minutes)).ConfigureAwait(false);
    }
    private static void RequireFile(WorkFile file)
    {
        if (!File.Exists(file.Path)) throw new InvalidDataException("提取工具未产出文件：" + file.Path);
    }
}
