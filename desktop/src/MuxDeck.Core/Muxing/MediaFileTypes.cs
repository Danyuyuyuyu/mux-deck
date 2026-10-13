using MuxDeck.Domain;

namespace MuxDeck.Core.Muxing;

// 保留旧调用入口；路径拆分留在边界，类型规则只由 Domain 维护。
public static class MediaFileTypes
{
    public static bool IsVideo(string path) => InputRules.IsVideoExtension(Path.GetExtension(path));
    public static bool IsAudio(string path) => InputRules.IsAudioExtension(Path.GetExtension(path));
    public static bool IsSubtitle(string path) => InputRules.IsSubtitleExtension(Path.GetExtension(path));
    public static bool IsAss(string path) => InputRules.IsAssExtension(Path.GetExtension(path));
    public static bool IsFont(string path) => InputRules.IsFontExtension(Path.GetExtension(path));
}
