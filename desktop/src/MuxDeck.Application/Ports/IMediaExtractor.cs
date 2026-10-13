using MuxDeck.Domain;

namespace MuxDeck.Application.Ports;

public interface IMediaExtractor
{
    Task ExtractTracksAsync(string sourcePath, EquatableArray<TrackExtraction> tracks,
        ToolCallContext context, CancellationToken cancellationToken = default);
    Task ExtractAttachmentsAsync(string sourcePath, EquatableArray<AttachmentExtraction> attachments,
        ToolCallContext context, CancellationToken cancellationToken = default);
}
