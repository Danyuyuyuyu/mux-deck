namespace MuxDeck.Core.Components;

public sealed record ComponentRelease(string Directory, string Version, ComponentPackage Package, string ArchiveRoot, string[] RequiredFiles, string? ReportedVersion = null);

public static class ComponentReleases
{
    // 固定包经独立下载核对；保留发布目录结构，不使用浮动 latest 下载地址。
    public static IReadOnlyDictionary<string, ComponentRelease> All { get; } = new Dictionary<string, ComponentRelease>
    {
        ["assfonts"] = new("assfonts", "0.7.3", new(
            "https://github.com/wyzdwdz/assfonts/releases/download/v0.7.3/assfonts-v0.7.3-x86_64-Windows.zip",
            "4DD11A3AA86CF03B8428FFD644B54C25D9A0AB7AB212C1460E6A34EAF22D7FA4", 14448047), "", ["assfonts.exe"]),
        ["mkvtoolnix"] = new("mkvtoolnix", "101.0", new(
            "https://mkvtoolnix.download/windows/releases/101.0/mkvtoolnix-64-bit-101.0.zip",
            "59A807B45B34AC524DFDEF61EDCF0642EE9BD6B08C130CFC078532C362569ABC", 88618153, 512 * 1024 * 1024), "mkvtoolnix", ["mkvmerge.exe", "mkvextract.exe", "mkvpropedit.exe"]),
        ["ffmpeg"] = new("ffmpeg", "8.1.1", new(
            "https://github.com/GyanD/codexffmpeg/releases/download/8.1.1/ffmpeg-8.1.1-essentials_build.zip",
            "6F58CE889F59C311410F7D2B18895B33C03456463486F3B1EBC93D97A0F54541", 109282242, 768 * 1024 * 1024), "ffmpeg-8.1.1-essentials_build", ["bin/ffmpeg.exe", "bin/ffprobe.exe", "bin/ffplay.exe"]),
        // 上游 v2.2.0 包的内部版本仍为 2.1.1；页面保留工具实际返回值。
        ["afs"] = new("assfontsubset", "2.2.0", new(
            "https://github.com/AmusementClub/AssFontSubset/releases/download/v2.2.0/AssFontSubset.Console_v2.2.0_win-x64.zip",
            "097A3B23D95780FB6C41C931352CC75EBAEB41A6831432CE8F818CA4C7714854", 4955764), "", ["AssFontSubset.Console.exe"], "2.1.1")
    };

    public static ComponentPackage Python { get; } = new(
        "https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip",
        "4ACBED6DD1C744B0376E3B1CF57CE906F9DC9E95E68824584C8099A63025A3C3", 11133606);
    public static ComponentPackage FontTools { get; } = new(
        "https://files.pythonhosted.org/packages/c7/4e/ce75a57ff3aebf6fc1f4e9d508b8e5810618a33d900ad6c19eb30b290b97/fonttools-4.61.1-py3-none-any.whl",
        "17D2BF5D541ADD43822BCF0C43D7D847B160C9BB01D15D5007D84E2217AAA371", 1148996);
    public static ComponentPackage Launchers { get; } = new(
        "https://files.pythonhosted.org/packages/44/3c/d717024885424591d5376220b5e836c2d5293ce2011523c9de23ff7bf068/pip-25.3-py3-none-any.whl",
        "9655943313A94722B7774661C21049070F6BBB0A1516BF02F7C8D5D9201514CD", 1778622);
}
