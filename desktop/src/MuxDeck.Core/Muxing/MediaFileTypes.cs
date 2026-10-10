namespace MuxDeck.Core.Muxing;

// 扩展名只做入口筛选，实际容器和轨道能力由 mkvmerge 探测确认。
public static class MediaFileTypes
{
    public static bool IsVideo(string path) => Path.GetExtension(path).ToLowerInvariant() is ".mkv" or ".mp4" or ".mov" or ".avi" or ".webm" or ".m4v" or ".ts" or ".m2ts" or ".mts" or ".mpg" or ".mpeg";
    public static bool IsAudio(string path) => Path.GetExtension(path).ToLowerInvariant() is ".flac" or ".aac" or ".m4a" or ".wav" or ".opus" or ".ogg" or ".mp3" or ".mp2" or ".ac3" or ".eac3" or ".dts" or ".mka" or ".w64";
    public static bool IsSubtitle(string path) => Path.GetExtension(path).ToLowerInvariant() is ".ass" or ".srt";
    public static bool IsAss(string path) => Path.GetExtension(path).Equals(".ass", StringComparison.OrdinalIgnoreCase);
    public static bool IsFont(string path) => Path.GetExtension(path).ToLowerInvariant() is ".ttf" or ".otf" or ".ttc" or ".otc";
}
