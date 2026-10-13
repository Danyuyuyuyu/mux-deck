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
}
