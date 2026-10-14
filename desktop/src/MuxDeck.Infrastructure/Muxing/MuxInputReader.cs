using System.Text;
using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using MuxDeck.Domain;

namespace MuxDeck.Infrastructure.Muxing;

public sealed class MuxInputReader : IMuxInputReader
{
    public Task<MuxVideoFacts> ReadVideoAsync(string path, CancellationToken cancellationToken = default) =>
        PortBoundary.Guard(() => { cancellationToken.ThrowIfCancellationRequested(); var full = Path.GetFullPath(path); return Task.FromResult(new MuxVideoFacts(full, File.Exists(full))); });
    public Task<MuxFontDirectoryFacts> ReadFontDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
        PortBoundary.Guard(() => { cancellationToken.ThrowIfCancellationRequested(); var full = string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(path); return Task.FromResult(new MuxFontDirectoryFacts(full, Directory.Exists(full))); });
    public Task<MuxOutputFacts> ReadOutputAsync(OutputSpec output, string videoPath, CancellationToken cancellationToken = default) =>
        PortBoundary.Guard(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Path.GetFullPath(output.Directory);
            var name = OutputNaming.ResolveForJob(output.FileName, Path.GetFileNameWithoutExtension(videoPath));
            var valid = !string.IsNullOrWhiteSpace(name) && name == Path.GetFileName(name) && name.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
            return Task.FromResult(new MuxOutputFacts(new(directory, name),
                valid, SamePath(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), Path.GetDirectoryName(videoPath)!),
                valid && SamePath(Path.Combine(directory, name), videoPath)));
        });
    public Task<MuxTrackFileFacts> ReadTrackAsync(ExternalTrackSpec track, CancellationToken cancellationToken = default) =>
        PortBoundary.Guard(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.GetFullPath(track.Path);
            var exists = File.Exists(path);
            string? text = null;
            if (exists && track.Kind == TrackKind.Subtitle && InputRules.IsSubtitleExtension(Path.GetExtension(path)))
                text = InputRules.IsAssExtension(Path.GetExtension(path)) ? AssGlyphValidator.ReadAss(path) :
                    new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path)).TrimStart('\uFEFF');
            return Task.FromResult(new MuxTrackFileFacts(track with { Path = path }, exists, text));
        });
    public Task<bool> DestinationExistsAsync(string path, CancellationToken cancellationToken = default) =>
        PortBoundary.Guard(() => { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(File.Exists(path) || Directory.Exists(path)); });
    private static bool SamePath(string left, string right) => InputRules.SameNormalizedPath(
        Path.GetFullPath(left), Path.GetFullPath(right), OperatingSystem.IsWindows());
}
