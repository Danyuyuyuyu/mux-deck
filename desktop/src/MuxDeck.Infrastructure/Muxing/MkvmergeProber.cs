using System.Text.Json;
using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using MuxDeck.Domain;

namespace MuxDeck.Infrastructure.Muxing;

public sealed class MkvmergeProber(IMuxToolRunner? runner = null, IToolCatalog? catalog = null) : IMediaProber
{
    public Task<MediaProbeResult> ProbeAsync(string path, ToolCallContext context, CancellationToken cancellationToken = default) =>
        PortBoundary.Guard(async () =>
        {
            var executable = await ToolCatalog.Require(catalog ?? new ToolCatalog(), ToolCapability.ProbeMedia, context.Tools, cancellationToken).ConfigureAwait(false);
            var result = await new ToolExecutor(runner).RunAsync(executable, ["-J", Path.GetFullPath(path)], context,
                cancellationToken, TimeSpan.FromMinutes(2), json: true).ConfigureAwait(false);
            return Parse(path, result.StandardOutput);
        });

    public static MediaProbeResult Parse(string path, string json) =>
        PortBoundary.Guard(() =>
        {
            try { return Task.FromResult(ParseCore(path, json)); }
            catch (InvalidOperationException ex) { throw PortBoundary.Failure(ErrorCodes.ToolOutputInvalid, ex); }
        }).GetAwaiter().GetResult();
    private static TrackKind Kind(string type) => type switch
    { "video" => TrackKind.Video, "audio" => TrackKind.Audio, "subtitles" => TrackKind.Subtitle, _ => TrackKind.Unknown };
    private static MediaProbeResult ParseCore(string path, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("errors", out var errors) && errors.GetArrayLength() != 0)
            throw new InvalidDataException("mkvmerge 探测报告错误：" + errors);
        if (root.TryGetProperty("container", out var container) &&
            container.TryGetProperty("recognized", out var recognized) && !recognized.GetBoolean())
            throw new InvalidDataException("文件容器不可识别。");
        var tracks = new List<MediaTrackInfo>();
        foreach (var track in root.GetProperty("tracks").EnumerateArray())
        {
            var p = track.GetProperty("properties");
            tracks.Add(new MediaTrackInfo(new(track.GetProperty("id").GetInt32()), Kind(String(track, "type")), String(track, "type"), String(track, "codec"),
                OptionalString(p, "codec_id"), new(String(p, "language")), OptionalString(p, "language_ietf") is { } ietf ? new LanguageTag(ietf) : null,
                String(p, "track_name"), new(Bool(p, "default_track"), Bool(p, "forced_track")),
                OptionalString(p, "pixel_dimensions"), (int?)Number(p, "audio_channels"), Number(p, "audio_sampling_frequency"),
                p.TryGetProperty("default_track", out _), p.TryGetProperty("forced_track", out _)));
        }
        var attachments = new List<MediaAttachmentInfo>();
        if (root.TryGetProperty("attachments", out var attached))
            foreach (var a in attached.EnumerateArray())
                attachments.Add(new MediaAttachmentInfo(a.GetProperty("id").GetInt32(), String(a, "file_name"),
                    String(a, "content_type"), Number(a, "size") ?? 0));
        var duration = root.TryGetProperty("container", out container) && container.TryGetProperty("properties", out var properties)
            ? Number(properties, "duration") : null;
        return new MediaProbeResult(Path.GetFullPath(path), new(tracks), new(attachments), duration);
    }

    private static string String(JsonElement element, string key) => OptionalString(element, key) ?? "";
    private static string? OptionalString(JsonElement element, string key) => element.TryGetProperty(key, out var value) ? value.GetString() : null;
    private static bool Bool(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.GetBoolean();
    private static long? Number(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.TryGetInt64(out var number) ? number : null;
}
