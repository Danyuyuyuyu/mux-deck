using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using MuxDeck.Domain;

namespace MuxDeck.Infrastructure.Muxing;

public sealed class MkvmergeMuxer(IMuxToolRunner? runner = null, IToolCatalog? catalog = null) : IMuxer
{
    public Task<MediaMuxResult> MuxAsync(MediaMuxRequest request, ToolCallContext context,
        IProgress<JobProgress>? progress = null, CancellationToken cancellationToken = default) => PortBoundary.Guard(async () =>
    {
        var executable = await ToolCatalog.Require(catalog ?? new ToolCatalog(), ToolCapability.MuxMedia, context.Tools, cancellationToken).ConfigureAwait(false);
        await new ToolExecutor(runner).RunAsync(executable, BuildArguments(request), context, cancellationToken, TimeSpan.FromHours(2)).ConfigureAwait(false);
        if (!File.Exists(request.TemporaryOutput.Path) || new FileInfo(request.TemporaryOutput.Path).Length == 0)
            throw new InvalidDataException("封装工具未产出有效 MKV。");
        return new MediaMuxResult(request.TemporaryOutput, []);
    });

    public static IReadOnlyList<string> BuildArguments(MediaMuxRequest request)
    {
        var args = new List<string> { "-o", request.TemporaryOutput.Path, "--no-subtitles", "--no-attachments", request.VideoPath };
        foreach (var track in request.Tracks)
        {
            if (track.Kind == TrackKind.Audio) args.AddRange(AudioArguments(track));
            else if (track.Kind == TrackKind.Subtitle)
                args.AddRange(["--sub-charset", "0:UTF-8", "--language", "0:" + track.Language, "--track-name", "0:" + track.Name,
                    "--default-track-flag", "0:" + (track.Flags.IsDefault ? "1" : "0"),
                    "--forced-display-flag", "0:" + (track.Flags.IsForced ? "1" : "0"), track.Path]);
            else throw new InvalidDataException("外加轨道只能是字幕或音频。");
        }
        for (var i = 0; i < request.Fonts.Length; i++)
            args.AddRange(["--attachment-name", $"{i + 1:D3}-{Path.GetFileName(request.Fonts[i].Path)}", "--attachment-mime-type",
                Path.GetExtension(request.Fonts[i].Path).Equals(".otf", StringComparison.OrdinalIgnoreCase) ? "font/otf" : "font/ttf", "--attach-file", request.Fonts[i].Path]);
        return args;
    }
    public static IReadOnlyList<string> AudioArguments(ExternalTrackSpec audio)
    {
        var id = (audio.SourceTrackId ?? throw new InvalidDataException("外加音频必须指定真实 Track ID。")).Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return ["--no-video", "--no-subtitles", "--no-attachments", "--no-chapters", "--no-global-tags", "--no-track-tags", "--audio-tracks", id,
            "--language", id + ":" + audio.Language, "--track-name", id + ":" + audio.Name,
            "--default-track-flag", id + ":" + (audio.Flags.IsDefault ? "1" : "0"), "--forced-display-flag", id + ":" + (audio.Flags.IsForced ? "1" : "0"), audio.Path];
    }
}
