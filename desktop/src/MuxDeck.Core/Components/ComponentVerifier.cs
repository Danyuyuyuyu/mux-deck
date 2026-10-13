using System.Text.RegularExpressions;

namespace MuxDeck.Core.Components;

public sealed class ComponentVerifier(IComponentProbeRunner? probeRunner = null, Func<Workspace, string?>? fontToolsLocator = null) : IComponentVerifier
{
    private readonly IComponentProbeRunner runner = probeRunner ?? new ComponentProbeRunner();

    public async Task<ComponentInspection> VerifyAsync(ComponentDefinition component, Workspace workspace, CancellationToken cancellationToken)
    {
        var located = new ToolLocator(workspace).Inspect();
        var parts = component.ToolNames.Select(name => located.Single(t => t.Name == name)).ToArray();
        if (parts.All(p => p.Path is null)) return new(ComponentState.Missing, parts, Reason: "未检测到可执行文件。");
        if (parts.Any(p => p.Path is null)) return new(ComponentState.Partial, parts, Reason: "缺少子工具：" + string.Join("、", parts.Where(p => p.Path is null).Select(p => p.Name)));
        string? version = null;
        try
        {
            foreach (var part in parts)
            {
                var args = part.Name switch { "ffmpeg" => new[] { "-version" }, "assfonts" => ["--help"], _ => ["--version"] };
                var output = await ProbeAsync(part.Path!, args, cancellationToken).ConfigureAwait(false);
                var current = ReadVersion(part.Name, output);
                if (current is null) throw new InvalidDataException(part.Name + " 未返回可识别的版本信息。");
                if (version is not null && component.Id == "mkvtoolnix" && current != version)
                    return new(ComponentState.Invalid, parts, version, "MKVToolNix 子工具版本不一致，请使用同一组件包。");
                version = current;
            }
            if (component.Id == "afs")
            {
                var backend = (fontToolsLocator ?? FontToolsLocator.Find)(workspace);
                if (backend is null) return new(ComponentState.DependencyMissing, parts, version, "缺少 fonttools 后端：需要同目录且可运行的 pyftsubset 与 ttx。");
                await ProbeAsync(Path.Combine(backend, Executable("pyftsubset")), ["--help"], cancellationToken).ConfigureAwait(false);
                await ProbeAsync(Path.Combine(backend, Executable("ttx")), ["--version"], cancellationToken).ConfigureAwait(false);
            }
            if (component.Id == "ffmpeg")
            {
                var filters = await ProbeAsync(parts[0].Path!, ["-hide_banner", "-filters"], cancellationToken).ConfigureAwait(false);
                if (!Regex.IsMatch(filters, @"(?m)^\s*\S+\s+subtitles\s+"))
                    return new(ComponentState.Invalid, parts, version, "此 FFmpeg 未提供 subtitles 滤镜，无法用于字幕预览。");
            }
            return new(ComponentState.Available, parts, version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { return new(ComponentState.Invalid, parts, version, ex.Message); }
    }

    private async Task<string> ProbeAsync(string path, string[] args, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var result = await runner.RunAsync(new ProcessRequest(path, args, MaxCapturedCharacters: 128 * 1024), timeout.Token).ConfigureAwait(false);
            if (result.ExitCode != 0) throw new InvalidDataException($"{Path.GetFileName(path)} 启动失败（退出码 {result.ExitCode}）。" + Tail(result.StandardError + result.StandardOutput));
            if (result.CaptureLimitExceeded) throw new InvalidDataException("工具验证输出超出限制。");
            return result.StandardOutput + "\n" + result.StandardError;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException(Path.GetFileName(path) + " 验证超时。"); }
    }

    private static string? ReadVersion(string name, string output)
    {
        var pattern = name switch
        {
            "ffmpeg" => @"ffmpeg version\s+(\S+)",
            "assfonts" => @"assfonts v([\d.]+)",
            "AssFontSubset" => @"(?m)^\s*(\d+\.\d+\.\d+(?:[^\s]*)?)",
            _ => Regex.Escape(name) + @" v([\d.]+)"
        };
        var match = Regex.Match(output, pattern);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string Executable(string name) => name + (OperatingSystem.IsWindows() ? ".exe" : "");
    private static string Tail(string value) => value.Length <= 600 ? value.Trim() : value[^600..].Trim();
}
