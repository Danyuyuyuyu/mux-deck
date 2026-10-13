using MuxDeck.Domain;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MuxDeck.Core.Muxing;

public sealed partial class SingleMuxService
{
    private Workspace workspace;
    private readonly IMuxToolRunner runner;

    public SingleMuxService(Workspace workspace) : this(workspace, new MuxToolRunner()) { }
    public SingleMuxService(Workspace workspace, IMuxToolRunner runner)
    {
        this.workspace = workspace;
        this.runner = runner;
    }

    /// <summary>更新后续任务使用的工具和工作目录；当前运行中的任务仍持有自己的快照。</summary>
    public void UpdateWorkspace(Workspace nextWorkspace)
    {
        if (string.IsNullOrWhiteSpace(nextWorkspace.Root)) throw new ArgumentException("工作区不能为空。", nameof(nextWorkspace));
        workspace = nextWorkspace;
    }

    public async Task<MediaProbe> ProbeAsync(string videoPath, CancellationToken cancellationToken = default)
    {
        var mkvmerge = RequireTool("mkvmerge");
        var result = await RunToolAsync(mkvmerge, ["-J", Path.GetFullPath(videoPath)], null,
            null, cancellationToken, TimeSpan.FromMinutes(2), json: true).ConfigureAwait(false);
        return ParseProbe(videoPath, result.StandardOutput);
    }

    public async Task<MuxResult> RunAsync(MuxRequest request, IProgress<MuxProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<string>();
        var warnings = new List<MuxWarning>();
        var sourceMetadataCache = new Dictionary<(string Path, int Index), string>();
        string? logPath = null;
        string? work = null;
        string? prepared = null;
        TaskLog? log = null;
        var committed = false;
        string? destination = null;
        try
        {
            // 在任何进度回调和异步等待前复制集合，任务只消费本次提交的参数快照。
            ArgumentNullException.ThrowIfNull(request);
            request = request with { Subtitles = request.Subtitles?.ToArray()!, Audios = request.Audios?.ToArray()! };
            var runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
            var run = Path.Combine(workspace.TaskLogsDirectory, runId);
            Directory.CreateDirectory(run);
            logPath = Path.Combine(run, "task.log");
            log = new TaskLog(logPath);
            // 缓存与日志分离后仍保留拥有权明确的 work 子目录，与清理守卫一致。
            work = Path.Combine(workspace.TaskCacheDirectory, runId, "work");
            Directory.CreateDirectory(work);
            Stage(MuxStage.Validating, "检查输入与真实工具依赖");
            var snapshot = Validate(request);
            destination = Path.Combine(snapshot.OutputDirectory, snapshot.OutputFileName!);
            log.Write("请求 " + JsonSerializer.Serialize(snapshot));
            if (File.Exists(destination) || Directory.Exists(destination))
                throw new IOException("目标已存在，拒绝覆盖：" + destination);
            var mkvmerge = RequireTool("mkvmerge");
            var mkvextract = RequireTool("mkvextract");
            var afs = ""; var fontTools = "";
            if (snapshot.Subtitles.Any(s => MediaFileTypes.IsAss(s.Path)))
            {
                afs = RequireTool("AssFontSubset"); fontTools = LocateFontTools();
                await RunToolAsync(Path.Combine(fontTools, Executable("pyftsubset")), ["--help"], work,
                    log, cancellationToken, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
                await RunToolAsync(Path.Combine(fontTools, Executable("ttx")), ["--version"], work,
                    log, cancellationToken, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
            }
            Stage(MuxStage.Probing, "探测源视频和所有音轨");
            var source = await ProbeLogged(snapshot.VideoPath).ConfigureAwait(false);
            if (!source.Tracks.Any(t => t.Type == "video")) throw new InvalidDataException("源文件没有视频轨。");
            var audioSources = new List<MediaProbe>();
            foreach (var audio in snapshot.Audios)
            {
                var probe = await ProbeLogged(audio.Path).ConfigureAwait(false);
                if (!probe.Tracks.Any(t => t.Type == "audio" && t.Id == audio.TrackId)) throw new InvalidDataException("外加音频 Track ID 不存在：" + audio.Path);
                audioSources.Add(probe);
            }

            var corrected = new List<string>();
            var fonts = new List<string>();
            for (var i = 0; i < snapshot.Subtitles.Count; i++)
            {
                if (!MediaFileTypes.IsAss(snapshot.Subtitles[i].Path))
                {
                    var srt = Path.Combine(work, $"subtitle-{i}.srt");
                    File.Copy(snapshot.Subtitles[i].Path, srt);
                    corrected.Add(srt);
                    continue;
                }
                Stage(MuxStage.Subsetting, $"子集化字幕 {i + 1}/{snapshot.Subtitles.Count}");
                var input = Path.Combine(work, $"input-{i}", Path.GetFileName(snapshot.Subtitles[i].Path));
                Directory.CreateDirectory(Path.GetDirectoryName(input)!);
                File.Copy(snapshot.Subtitles[i].Path, input);
                var originalUsage = AssGlyphValidator.ReadUsage(AssGlyphValidator.ReadAss(input));
                var output = Path.Combine(work, $"afs-{i}");
                await RunToolAsync(afs,
                    [input, "--fonts", snapshot.FontsDirectory, "--output", output,
                     "--subset-backend", "PyFontTools", "--bin-path", fontTools], work,
                    log, cancellationToken, TimeSpan.FromMinutes(20), rejectDiagnostics: true,
                    warnings: warnings).ConfigureAwait(false);
                var fixedAss = Path.Combine(output, Path.GetFileName(input));
                if (!File.Exists(fixedAss)) throw new InvalidDataException("AFS 未产出修正 ASS。");
                var text = AssGlyphValidator.ReadAss(fixedAss);
                if (!text.Contains("; Font Subset:", StringComparison.Ordinal))
                    throw new InvalidDataException("AFS 未写入字体子集映射，不能确认是真实子集化结果。");
                var generated = Directory.EnumerateFiles(output).Where(IsFont).Order().ToArray();
                if (generated.Length == 0 || generated.Any(x => new FileInfo(x).Length == 0))
                    throw new InvalidDataException("AFS 子集字体产物不全。");
                var xmlFiles = new List<string>();
                foreach (var font in generated)
                {
                    var xml = font + ".xml";
                    await RunToolAsync(Path.Combine(fontTools, Executable("ttx")),
                        ["-q", "-t", "name", "-t", "cmap", "-t", "OS/2", "-o", xml, font], work,
                        log, cancellationToken, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
                    if (!File.Exists(xml)) throw new InvalidDataException("无法读取子集字体的 name/cmap 表。");
                    xmlFiles.Add(xml);
                }
                var usage = AssGlyphValidator.ReadUsage(text);
                var intrinsicProofs = new Dictionary<string, AssGlyphValidator.IntrinsicFaceProof>();
                foreach (var needed in AssGlyphValidator.GetIntrinsicFaceRequests(usage, xmlFiles, text, originalUsage))
                {
                    var sourceFont = LocateSourceFont(snapshot.FontsDirectory, needed.SourceFileName);
                    var collection = Path.GetExtension(sourceFont).ToLowerInvariant() is ".ttc" or ".otc";
                    if (!collection && needed.FaceIndex != 0)
                        throw new InvalidDataException("独立源字体的 faceIndex 必须为 0。");
                    var cacheKey = (sourceFont, needed.FaceIndex);
                    if (!sourceMetadataCache.TryGetValue(cacheKey, out var metadataXml))
                    {
                        metadataXml = Path.Combine(work, $"source-names-{sourceMetadataCache.Count}.xml");
                        var metadataArgs = new List<string> { "-q", "-t", "name", "-t", "OS/2" };
                        if (collection) metadataArgs.AddRange(["-y", needed.FaceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
                        metadataArgs.AddRange(["-o", metadataXml, sourceFont]);
                        await RunToolAsync(Path.Combine(fontTools, Executable("ttx")), metadataArgs, work,
                            log, cancellationToken, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
                        if (!File.Exists(metadataXml)) throw new InvalidDataException("未产出源字体 name/OS2 元数据。");
                        sourceMetadataCache.Add(cacheKey, metadataXml);
                    }
                    intrinsicProofs[needed.Token] = new AssGlyphValidator.IntrinsicFaceProof(
                        needed.OriginalName, sourceFont, metadataXml);
                }
                AssGlyphValidator.Validate(usage, xmlFiles, warnings, intrinsicProofs);
                checks.Add($"字幕 {i + 1}：修正 ASS 与 {generated.Length} 个子集字体的引用、字形完整性通过");
                corrected.Add(fixedAss);
                fonts.AddRange(generated);
            }
            if (warnings.Count > 0)
                Stage(MuxStage.Subsetting, "子集化注意提示：" + string.Join("；", warnings.Select(w =>
                    $"{w.Message}（累计 {w.Occurrences} 次）")));

            // 附件名与字幕序号关联，两个字幕生成同名字体时也不产生身份歧义。
            var attachmentNames = fonts.Select((font, i) => $"{i + 1:D3}-{Path.GetFileName(font)}").ToArray();
            var temporaryMkv = Path.Combine(work, "result.mkv");
            var args = new List<string> { "-o", temporaryMkv, "--no-subtitles", "--no-attachments", snapshot.VideoPath };
            for (var i = 0; i < corrected.Count; i++)
            {
                var subtitle = snapshot.Subtitles[i];
                args.AddRange(["--sub-charset", "0:UTF-8", "--language", "0:" + subtitle.Language, "--track-name", "0:" + subtitle.Name,
                    "--default-track-flag", "0:" + (subtitle.IsDefault ? "1" : "0"),
                    "--forced-display-flag", "0:" + (subtitle.IsForced ? "1" : "0"), corrected[i]]);
            }
            foreach (var audio in snapshot.Audios) args.AddRange(AudioArguments(audio));
            for (var i = 0; i < fonts.Count; i++)
                args.AddRange(["--attachment-name", attachmentNames[i], "--attachment-mime-type",
                    Path.GetExtension(fonts[i]).Equals(".otf", StringComparison.OrdinalIgnoreCase)
                        ? "font/otf" : "font/ttf", "--attach-file", fonts[i]]);
            Stage(MuxStage.Muxing, "保留视频和音轨，嵌入新增字幕与子集字体");
            await RunToolAsync(mkvmerge, args, work, log, cancellationToken, TimeSpan.FromHours(2)).ConfigureAwait(false);
            Stage(MuxStage.Checking, "核验轨道、旗标、字体附件和成品可读性");
            if (!File.Exists(temporaryMkv) || new FileInfo(temporaryMkv).Length == 0)
                throw new InvalidDataException("封装工具未产出有效 MKV。");
            var outputProbe = await ProbeLogged(temporaryMkv).ConfigureAwait(false);
            CheckTracks(source, outputProbe, snapshot, checks, audioSources);
            if (outputProbe.Attachments.Count != fonts.Count) throw new InvalidDataException("成品字体附件数量不符。");
            for (var i = 0; i < fonts.Count; i++)
            {
                var attachment = outputProbe.Attachments[i];
                if (attachment.FileName != attachmentNames[i] || attachment.Size != new FileInfo(fonts[i]).Length ||
                    attachment.ContentType is not ("font/ttf" or "font/otf"))
                    throw new InvalidDataException($"字体附件 {i + 1} 的身份、类型或大小不符。");
            }
            var extracted = Path.Combine(work, "extracted");
            Directory.CreateDirectory(extracted);
            var subtitleTracks = outputProbe.Tracks.Where(t => t.Type == "subtitles").ToArray();
            var trackArgs = new List<string> { temporaryMkv, "tracks" };
            for (var i = 0; i < subtitleTracks.Length; i++)
                trackArgs.Add($"{subtitleTracks[i].Id}:{Path.Combine(extracted, $"subtitle-{i}" + Path.GetExtension(corrected[i]))}");
            if (subtitleTracks.Length > 0) await RunToolAsync(mkvextract, trackArgs, work, log, cancellationToken, TimeSpan.FromMinutes(10)).ConfigureAwait(false);
            for (var i = 0; i < corrected.Count; i++)
            {
                // ASS 在 Matroska 中由头部和带 ReadOrder/时序的 Dialogue 块组成；
                // MKVToolNix 会把 Comment 移回头部。用同工具做仅字幕往返作为参考，
                // 不跳过任何样式、映射、事件或文本字段，也不重封源视频。
                var referenceMks = Path.Combine(work, $"ass-reference-{i}.mks");
                var referenceAss = Path.Combine(extracted, $"reference-{i}" + Path.GetExtension(corrected[i]));
                await RunToolAsync(mkvmerge, ["-o", referenceMks, "--sub-charset", "0:UTF-8", corrected[i]], work, log,
                    cancellationToken, TimeSpan.FromMinutes(10)).ConfigureAwait(false);
                if (!File.Exists(referenceMks) || new FileInfo(referenceMks).Length == 0)
                    throw new InvalidDataException($"新增字幕 {i + 1} 未产出有效 ASS 校验参考容器。");
                await RunToolAsync(mkvextract, [referenceMks, "tracks", $"0:{referenceAss}"], work, log,
                    cancellationToken, TimeSpan.FromMinutes(10)).ConfigureAwait(false);
                if (!File.Exists(referenceAss) || new FileInfo(referenceAss).Length == 0)
                    throw new InvalidDataException($"新增字幕 {i + 1} 未产出有效 ASS 校验参考字幕。");
                if (!OutputQcRules.SubtitleContentEquals(SubtitleText.Read(referenceAss),
                    SubtitleText.Read(Path.Combine(extracted, $"subtitle-{i}" + Path.GetExtension(corrected[i])))))
                    throw new InvalidDataException($"新增字幕 {i + 1} 提取后内容与同工具字幕参考不符。");
            }
            var attachmentArgs = new List<string> { temporaryMkv, "attachments" };
            for (var i = 0; i < fonts.Count; i++)
                attachmentArgs.Add($"{outputProbe.Attachments[i].Id}:{Path.Combine(extracted, attachmentNames[i])}");
            if (fonts.Count > 0) await RunToolAsync(mkvextract, attachmentArgs, work, log, cancellationToken, TimeSpan.FromMinutes(10)).ConfigureAwait(false);
            for (var i = 0; i < fonts.Count; i++)
                if (!HashFile(fonts[i]).SequenceEqual(HashFile(Path.Combine(extracted, attachmentNames[i]))))
                    throw new InvalidDataException($"字体附件 {i + 1} 提取后校验不一致。");
            checks.Add($"源字幕和附件已移除；{fonts.Count} 个子集字体附件提取与 SHA-256 校验通过");
            checks.Add("MKV 可探测，新增字幕提取后与同工具往返参考全文一致");
            for (var i = 0; i < snapshot.Audios.Count; i++)
            {
                var reference = Path.Combine(work, $"audio-reference-{i}.mka");
                await RunToolAsync(mkvmerge, ["-o", reference, .. AudioArguments(snapshot.Audios[i])], work, log, cancellationToken, TimeSpan.FromMinutes(20)).ConfigureAwait(false);
                var referenceProbe = await ProbeLogged(reference).ConfigureAwait(false);
                var referenceTrack = referenceProbe.Tracks.Single(t => t.Type == "audio");
                var actual = outputProbe.Tracks.Where(t => t.Type == "audio").Skip(source.Tracks.Count(t => t.Type == "audio")).ElementAt(i);
                var beforeRaw = Path.Combine(extracted, $"audio-reference-{i}.raw"); var afterRaw = Path.Combine(extracted, $"audio-output-{i}.raw");
                await RunToolAsync(mkvextract, [reference, "tracks", "--fullraw", $"{referenceTrack.Id}:{beforeRaw}"], work, log, cancellationToken, TimeSpan.FromMinutes(20)).ConfigureAwait(false);
                await RunToolAsync(mkvextract, [temporaryMkv, "tracks", "--fullraw", $"{actual.Id}:{afterRaw}"], work, log, cancellationToken, TimeSpan.FromMinutes(20)).ConfigureAwait(false);
                if (!HashFile(beforeRaw).SequenceEqual(HashFile(afterRaw))) throw new InvalidDataException($"外加音频 {i + 1} 数据与参考不符。");
                checks.Add($"外加音频 {i + 1} 原始数据及 CodecPrivate SHA-256 一致");
            }

            Stage(MuxStage.Installing, "在输出目录准备成品并拒绝覆盖地提交");
            Directory.CreateDirectory(snapshot.OutputDirectory);
            prepared = Path.Combine(snapshot.OutputDirectory, ".muxdeck-" + Guid.NewGuid().ToString("N") + ".partial");
            await using (var from = new FileStream(temporaryMkv, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var to = new FileStream(prepared, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await from.CopyToAsync(to, cancellationToken).ConfigureAwait(false);
                await to.FlushAsync(cancellationToken).ConfigureAwait(false);
                to.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // 最后一次取消检查之后进入提交点；同盘 rename 且不覆盖期间新出现的目标。
            File.Move(prepared, destination, overwrite: false);
            committed = true;
            prepared = null;
            log.Write("成功提交 " + destination);
            SafeReport(new MuxProgress(MuxStage.Completed, "封装完成", 100));
            return new MuxResult(MuxStatus.Succeeded, destination, null, logPath, checks.ToArray()) { Warnings = warnings.ToArray() };

            async Task<MediaProbe> ProbeLogged(string path)
            {
                var result = await RunToolAsync(mkvmerge, ["-J", path], work, log,
                    cancellationToken, TimeSpan.FromMinutes(2), json: true).ConfigureAwait(false);
                return ParseProbe(path, result.StandardOutput);
            }
            void Stage(MuxStage stage, string message)
            {
                cancellationToken.ThrowIfCancellationRequested();
                log.Write($"阶段 {stage}：{message}");
                SafeReport(new MuxProgress(stage, message));
                cancellationToken.ThrowIfCancellationRequested();
            }
            void SafeReport(MuxProgress value)
            {
                try { progress?.Report(value); }
                catch (Exception ex) { log.Write("进度订阅者异常：" + ex.Message); }
            }
        }
        catch (OperationCanceledException) when (!committed && cancellationToken.IsCancellationRequested)
        {
            log?.Write("任务已取消");
            return new MuxResult(MuxStatus.Cancelled, null, "任务已取消", logPath, checks.ToArray()) { Warnings = warnings.ToArray() };
        }
        catch (Exception ex) when (!committed)
        {
            log?.Write("失败 " + ex);
            return new MuxResult(MuxStatus.Failed, null, ex.Message, logPath, checks.ToArray()) { Warnings = warnings.ToArray() };
        }
        finally
        {
            if (prepared is not null) TryDelete(() => File.Delete(prepared), log);
            if (work is not null) TryDelete(() => DeleteWorkDirectory(work), log);
            log?.Dispose();
        }
    }

    private MuxRequest Validate(MuxRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Subtitles is null || request.Audios is null || request.Subtitles.Count + request.Audios.Count == 0)
            throw new ArgumentException("至少需要一条启用的外加字幕或音频。");
        var video = Path.GetFullPath(request.VideoPath);
        if (!File.Exists(video)) throw new FileNotFoundException("视频不存在", video);
        var fonts = string.IsNullOrWhiteSpace(request.FontsDirectory) ? "" : Path.GetFullPath(request.FontsDirectory);
        if (request.Subtitles.Any(s => MediaFileTypes.IsAss(s.Path)) && !Directory.Exists(fonts)) throw new DirectoryNotFoundException("字体目录不存在：" + fonts);
        var output = Path.GetFullPath(request.OutputDirectory);
        if (SamePath(output.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), Path.GetDirectoryName(video)!))
            throw new ArgumentException("请使用与源视频不同的独立输出目录。");
        var name = OutputNaming.ResolveForJob(request.OutputFileName, Path.GetFileNameWithoutExtension(video));
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) ||
            !name.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("输出文件名必须是有效的 MKV 文件名，不能包含目录。");
        if (SamePath(Path.Combine(output, name), video)) throw new ArgumentException("输出不能覆盖源视频。");
        var subtitles = request.Subtitles.Select(subtitle =>
        {
            if (subtitle is null) throw new ArgumentException("字幕参数不能为空。");
            var path = Path.GetFullPath(subtitle.Path);
            if (!File.Exists(path) || !MediaFileTypes.IsSubtitle(path))
                throw new ArgumentException("字幕必须是存在的 ASS/SRT 文件：" + path);
            SubtitleText.Validate(path);
            if (!ValidLanguage(subtitle.Language))
                throw new ArgumentException("字幕语言必须使用有效的语言代码或 BCP 47 标签。");
            if (subtitle.Name is null || subtitle.Name.Contains('\0')) throw new ArgumentException("字幕轨名称无效。");
            return subtitle with { Path = path };
        }).ToArray();
        var audios = request.Audios.Select(audio =>
        {
            if (audio is null) throw new ArgumentException("音频参数不能为空。");
            var path = Path.GetFullPath(audio.Path);
            if (!File.Exists(path) || !(MediaFileTypes.IsAudio(path) || MediaFileTypes.IsVideo(path)) || audio.TrackId < 0) throw new ArgumentException("音频路径或 Track ID 无效。");
            if (!ValidLanguage(audio.Language) || audio.Name is null || audio.Name.Contains('\0')) throw new ArgumentException("音频名称或语言无效。");
            return audio with { Path = path };
        }).ToArray();
        return new MuxRequest(video, subtitles, fonts, output, name) { Audios = audios };
    }

    private string RequireTool(string name) => new ToolLocator(workspace).Inspect().First(x => x.Name == name).Path
        ?? throw new FileNotFoundException("找不到工具：" + name);

    private static string LocateSourceFont(string directory, string subsetSourceName)
    {
        var candidates = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Where(IsFont)
            .Where(path => string.Equals(Path.GetFileName(path), subsetSourceName, StringComparison.OrdinalIgnoreCase) ||
                ((Path.GetExtension(path).ToLowerInvariant() is ".ttc" or ".otc") &&
                 string.Equals(Path.GetFileNameWithoutExtension(path), Path.GetFileNameWithoutExtension(subsetSourceName), StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (candidates.Length != 1)
            throw new InvalidDataException("固有字重的源字体文件不存在或不唯一，不能推测源 face 身份：" + subsetSourceName);
        return candidates[0];
    }

    private void DeleteWorkDirectory(string directory)
    {
        var resolved = Path.GetFullPath(directory);
        var ownedRoot = Path.GetFullPath(workspace.TaskCacheDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(ownedRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || Path.GetFileName(resolved) != "work")
            throw new IOException("拒绝清理任务目录以外的路径：" + resolved);
        Directory.Delete(resolved, recursive: true);
    }

    private string LocateFontTools() => Components.FontToolsLocator.Find(workspace)
        ?? throw new FileNotFoundException("找不到同目录的 pyftsubset 与 ttx，AFS 需要可运行的 fontTools 后端。");

    private async Task<ProcessResult> RunToolAsync(string executable, IReadOnlyList<string> args,
        string? work, TaskLog? log, CancellationToken token, TimeSpan timeout,
        bool json = false, bool rejectDiagnostics = false, List<MuxWarning>? warnings = null)
    {
        log?.Write("工具 " + executable + " " + JsonSerializer.Serialize(args));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(timeout);
        var diagnostics = rejectDiagnostics ? new AfsDiagnosticCollector(warnings) : null;
        var sink = new InlineProgress<ProcessLine>(line =>
        {
            log?.Write((line.IsError ? "stderr " : "stdout ") + line.Text);
            diagnostics?.Observe(line);
        });
        try
        {
            IReadOnlyDictionary<string, string>? environment = work is null ? null :
                new Dictionary<string, string> { ["TEMP"] = work, ["TMP"] = work };
            var result = await runner.RunAsync(new ProcessRequest(executable, args, work, Encoding.UTF8,
                environment, json ? 8 * 1024 * 1024 : 1024 * 1024), sink, lifetime.Token).ConfigureAwait(false);
            diagnostics?.Complete(result);
            if (result.ExitCode != 0 || diagnostics?.FirstFatal is not null)
                throw new InvalidDataException($"{Path.GetFileName(executable)} " +
                    (result.ExitCode == 0 ? "诊断阻止封装" : "执行失败") + $"（退出码 {result.ExitCode}）：" +
                    (diagnostics?.FirstFatal?.Message ?? Tail(result.StandardError + result.StandardOutput)));
            if (json && result.CaptureLimitExceeded) throw new InvalidDataException("探测 JSON 超出 8 MiB 限制，未使用截断内容判定。");
            return result;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException($"工具 {Path.GetFileName(executable)} 超时（{timeout}）。");
        }
    }

    private static MediaProbe ParseProbe(string path, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("errors", out var errors) && errors.GetArrayLength() != 0)
            throw new InvalidDataException("mkvmerge 探测报告错误：" + errors);
        if (root.TryGetProperty("container", out var container) &&
            container.TryGetProperty("recognized", out var recognized) && !recognized.GetBoolean())
            throw new InvalidDataException("文件容器不可识别。");
        var tracks = new List<MediaTrack>();
        foreach (var track in root.GetProperty("tracks").EnumerateArray())
        {
            var p = track.GetProperty("properties");
            tracks.Add(new MediaTrack(track.GetProperty("id").GetInt32(), String(track, "type"), String(track, "codec"),
                OptionalString(p, "codec_id"), String(p, "language"), OptionalString(p, "language_ietf"),
                String(p, "track_name"), Bool(p, "default_track"), Bool(p, "forced_track"),
                OptionalString(p, "pixel_dimensions"), (int?)Number(p, "audio_channels"), Number(p, "audio_sampling_frequency"),
                p.TryGetProperty("default_track", out _), p.TryGetProperty("forced_track", out _)));
        }
        var attachments = new List<MediaAttachment>();
        if (root.TryGetProperty("attachments", out var attached))
            foreach (var a in attached.EnumerateArray())
                attachments.Add(new MediaAttachment(a.GetProperty("id").GetInt32(), String(a, "file_name"),
                    String(a, "content_type"), Number(a, "size") ?? 0));
        var duration = root.TryGetProperty("container", out container) && container.TryGetProperty("properties", out var properties)
            ? Number(properties, "duration") : null;
        return new MediaProbe(Path.GetFullPath(path), tracks.ToArray(), attachments.ToArray(), duration);
    }

    private static void CheckTracks(MediaProbe source, MediaProbe output, MuxRequest request, List<string> checks, IReadOnlyList<MediaProbe> audioSources)
    {
        var subtitles = new EquatableArray<SubtitleQcFacts>(request.Subtitles.Select(input =>
            new SubtitleQcFacts(MediaFileTypes.IsAss(input.Path), input.Name, input.Language, input.IsDefault, input.IsForced)));
        var audios = new EquatableArray<AudioQcFacts>(request.Audios.Select(input =>
            new AudioQcFacts(input.TrackId, input.Name, input.Language, input.IsDefault, input.IsForced)));
        var sources = new EquatableArray<MediaQcFacts>(audioSources.Select(QcFacts));
        var result = OutputQcRules.CheckTracks(QcFacts(source), QcFacts(output), subtitles, audios, sources);
        checks.AddRange(result.Checks);
        if (result.Error is not null) throw new InvalidDataException(result.Error);
    }

    private static MediaQcFacts QcFacts(MediaProbe probe) => new(
        new(probe.Tracks.Select(LegacyMuxContractConverter.ToTrackInfo)),
        probe.DurationNanoseconds);

    private static bool IsFont(string path) => InputRules.IsFontExtension(Path.GetExtension(path));
    private static byte[] HashFile(string path) { using var stream = File.OpenRead(path); return SHA256.HashData(stream); }
    private static string Executable(string name) => name + (OperatingSystem.IsWindows() ? ".exe" : "");
    private static bool SamePath(string a, string b) =>
        InputRules.SameNormalizedPath(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows());
    private static string Tail(string value) => value.Length <= 600 ? value.Trim() : value[^600..].Trim();
    private static string String(JsonElement element, string key) => OptionalString(element, key) ?? "";
    private static string? OptionalString(JsonElement element, string key) => element.TryGetProperty(key, out var value) ? value.GetString() : null;
    private static bool Bool(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.GetBoolean();
    private static long? Number(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.TryGetInt64(out var number) ? number : null;
    private static void TryDelete(Action action, TaskLog? log) { try { action(); } catch (Exception ex) { log?.Write("临时资源清理失败：" + ex.Message); } }

    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }
    private sealed class TaskLog(string path) : IDisposable
    {
        private readonly StreamWriter writer = new(path, append: false, new UTF8Encoding(false)) { AutoFlush = true };
        private readonly object gate = new();
        // 日志磁盘异常不能推翻已完成的原子提交，也不能在清理时掩盖原始失败。
        public void Write(string text)
        {
            lock (gate)
                try { writer.WriteLine(DateTimeOffset.Now.ToString("O") + " " + text); }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
        }
        public void Dispose()
        {
            lock (gate)
                try { writer.Dispose(); }
                catch (IOException) { }
        }
    }
}
