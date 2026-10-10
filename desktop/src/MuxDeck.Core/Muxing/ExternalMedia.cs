using System.Globalization;
using System.Text.RegularExpressions;

namespace MuxDeck.Core.Muxing;

public sealed partial class SingleMuxService
{
    private static IReadOnlyList<string> AudioArguments(AudioInput audio)
    {
        var id = audio.TrackId.ToString(CultureInfo.InvariantCulture);
        return ["--no-video", "--no-subtitles", "--no-attachments", "--no-chapters", "--no-global-tags", "--no-track-tags", "--audio-tracks", id,
            "--language", id + ":" + audio.Language, "--track-name", id + ":" + audio.Name,
            "--default-track-flag", id + ":" + (audio.IsDefault ? "1" : "0"), "--forced-display-flag", id + ":" + (audio.IsForced ? "1" : "0"), audio.Path];
    }
    private static bool ValidLanguage(string value) => !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, "^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$");
    private static void CheckExternalAudio(MediaProbe source, MediaProbe output, MuxRequest request, IReadOnlyList<MediaProbe> audioSources, List<string> checks)
    {
        var added = output.Tracks.Where(t => t.Type == "audio").Skip(source.Tracks.Count(t => t.Type == "audio")).ToArray();
        for (var i = 0; i < request.Audios.Count; i++)
        {
            var input = request.Audios[i]; var before = audioSources[i].Tracks.Single(t => t.Type == "audio" && t.Id == input.TrackId); var actual = added[i];
            if (before.Codec != actual.Codec || (before.CodecId is not null && before.CodecId != actual.CodecId) ||
                (before.AudioChannels is not null && before.AudioChannels != actual.AudioChannels) ||
                (before.AudioSamplingFrequency is not null && before.AudioSamplingFrequency != actual.AudioSamplingFrequency) ||
                actual.Name != input.Name || !LanguageMatches(actual, input.Language) || actual.IsDefault != input.IsDefault || actual.IsForced != input.IsForced)
                throw new InvalidDataException($"外加音频 {i + 1} 编码、音频属性或元数据不符。");
            checks.Add($"外加音频 {i + 1}（轨 ID {actual.Id}）：编码、声道、采样率、名称、语言与旗标一致");
        }
    }
}
