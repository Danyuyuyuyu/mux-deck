using MuxDeck.Infrastructure.Muxing;
using System.Text;
using System.Text.RegularExpressions;

namespace MuxDeck.Core.Muxing;

public static class SubtitleText
{
    public static string Read(string path) => MediaFileTypes.IsAss(path) ? AssGlyphValidator.ReadAss(path) :
        new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path)).TrimStart('\uFEFF');
    public static void Validate(string path)
    {
        if (!MediaFileTypes.IsSubtitle(path)) throw new InvalidDataException("字幕仅支持 ASS/SRT。");
        var text = Read(path);
        if (MediaFileTypes.IsAss(path)) AssGlyphValidator.ReadUsage(text);
        else if (!Regex.IsMatch(text, @"\d+:\d{2}:\d{2},\d{3}\s*-->\s*\d+:\d{2}:\d{2},\d{3}")) throw new InvalidDataException("SRT 没有有效时间轴：" + path);
    }
}
