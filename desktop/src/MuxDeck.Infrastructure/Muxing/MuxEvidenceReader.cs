using System.Security.Cryptography;
using System.Text;
using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using MuxDeck.Domain;

namespace MuxDeck.Infrastructure.Muxing;

public sealed class MuxEvidenceReader : IMuxEvidenceReader
{
    public Task<WorkFile> CopySubtitleAsync(ExternalTrackSpec track, int index, ToolCallContext context, CancellationToken cancellationToken = default) =>
        PortBoundary.Guard(() => { cancellationToken.ThrowIfCancellationRequested(); var path = Path.Combine(context.Workspace.WorkDirectory, $"subtitle-{index}.srt"); File.Copy(track.Path, path); return Task.FromResult(new WorkFile(path)); });
    public Task<string> PrepareExtractionAsync(ToolCallContext context, CancellationToken cancellationToken = default) =>
        PortBoundary.Guard(() => { cancellationToken.ThrowIfCancellationRequested(); var path = Path.Combine(context.Workspace.WorkDirectory, "extracted"); Directory.CreateDirectory(path); return Task.FromResult(path); });
    public Task<MuxAttachmentEvidence> ReadAttachmentAsync(WorkFile file, CancellationToken cancellationToken = default) =>
        PortBoundary.Guard(() => { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(new MuxAttachmentEvidence(new FileInfo(file.Path).Length, Hash(file.Path))); });
    public Task<MuxSubtitleEvidence> ReadSubtitleAsync(WorkFile file, CancellationToken cancellationToken = default) =>
        PortBoundary.Guard(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(file.Path)) return Task.FromResult(new MuxSubtitleEvidence(false, 0, null));
            var length = new FileInfo(file.Path).Length;
            var text = InputRules.IsAssExtension(Path.GetExtension(file.Path)) ? AssGlyphValidator.ReadAss(file.Path) :
                new UTF8Encoding(false, true).GetString(File.ReadAllBytes(file.Path)).TrimStart('\uFEFF');
            return Task.FromResult(new MuxSubtitleEvidence(true, length, text));
        });
    public Task<string> ReadAudioPayloadAsync(WorkFile file, CancellationToken cancellationToken = default) =>
        PortBoundary.Guard(() => { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Hash(file.Path)); });
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
}
