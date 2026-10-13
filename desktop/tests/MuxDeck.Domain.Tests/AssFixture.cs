namespace MuxDeck.Domain.Tests;

internal static class AssFixture
{
    private const string RawText = """
[Script Info]
Title: fixture
[V4+ Styles]
Format: Name, Fontname, Bold, Italic
Style: Default,Arial,0,0
Style: Other,Times New Roman,-1,-1
[Events]
Format: Layer, Start, End, Style, Text
Dialogue: 0,0:00:00.00,0:00:01.00,Default,Hello
Dialogue: 0,0:00:01.00,0:00:02.00,Default,World
""";
    internal static string Text => RawText.Replace("\r\n", "\n");
    internal static string WithText(string text) => Text[..Text.IndexOf("Dialogue:", StringComparison.Ordinal)] +
        "Dialogue: 0,0:00:00.00,0:00:01.00,Default," + text;
}
