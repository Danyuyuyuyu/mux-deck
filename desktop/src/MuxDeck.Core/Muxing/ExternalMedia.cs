using MuxDeck.Infrastructure.Muxing;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MuxDeck.Core.Muxing;

public sealed partial class SingleMuxService
{
    private static IReadOnlyList<string> AudioArguments(AudioInput audio) => MkvmergeMuxer.AudioArguments(
        new(audio.Path, MuxDeck.Domain.TrackKind.Audio, new MuxDeck.Domain.TrackId(audio.TrackId),
            audio.Name, new(audio.Language), new(audio.IsDefault, audio.IsForced)));
    private static bool ValidLanguage(string value) => !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, "^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$");
}
