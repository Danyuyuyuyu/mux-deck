using MuxDeck.Contracts;
using MuxDeck.Infrastructure;
using System.Runtime.InteropServices;

namespace MuxDeck.Infrastructure.Components;

public enum ComponentState { Unknown, Found, Checking, Missing, Partial, Available, Invalid, DependencyMissing }
public enum ComponentInstallPhase { Idle, Preparing, Downloading, Installing, Verifying, Completed, Failed, Cancelled }
public sealed record ComponentDefinition(string Id, string Name, string Description, bool IsCore, string[] ToolNames, string GuideUrl);
public sealed record ComponentInspection(ComponentState State, IReadOnlyList<ToolStatus> Parts, string? Version = null, string? Reason = null);
public sealed record ComponentInstallProgress(ComponentInstallPhase Phase, string Message, long Bytes = 0, long? TotalBytes = null);
public sealed record ComponentInstallResult(string Directory, string LogPath);
public sealed record ComponentPackage(string Url, string Sha256, long Size, long MaxExpandedBytes = 100 * 1024 * 1024);
public sealed class ComponentInstallException(string message, string logPath, Exception? inner = null) : IOException(message, inner)
{
    public string LogPath { get; } = logPath;
}

public static class ComponentCatalog
{
    public static IReadOnlyList<ComponentDefinition> All { get; } = [
        new("mkvtoolnix", "MKVToolNix", "MKV 封装、轨道读取与元数据修补", true, ["mkvmerge", "mkvextract", "mkvpropedit"], "https://mkvtoolnix.download/downloads.html#windows"),
        new("afs", "AssFontSubset", "ASS 字体子集化主工具", true, ["AssFontSubset"], "https://github.com/AmusementClub/AssFontSubset#readme"),
        new("ffmpeg", "FFmpeg", "字幕渲染、媒体信息读取与预览", false, ["ffmpeg"], "https://ffmpeg.org/download.html"),
        new("assfonts", "assfonts", "字体子集化备用工具（当前封装暂未接入）", false, ["assfonts"], "https://github.com/wyzdwdz/assfonts/releases/tag/v0.7.3")
    ];
    public static bool SupportsInstall(string id) => All.Any(c => c.Id == id) && OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64;
}

public interface IComponentVerifier
{
    Task<ComponentInspection> VerifyAsync(ComponentDefinition component, ComponentConfiguration configuration, CancellationToken cancellationToken);
}

public interface IComponentInstaller
{
    Task<ComponentInstallResult> InstallAsync(string id, StoragePaths storage, IProgress<ComponentInstallProgress> progress, CancellationToken cancellationToken);
}

// 测试只替换外部进程边界，不在 UI 拼接或解析工具命令。
public interface IComponentProbeRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
}

public sealed class ComponentProbeRunner : IComponentProbeRunner
{
    private readonly ProcessRunner runner = new();
    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken) => runner.RunAsync(request, cancellationToken: cancellationToken);
}
