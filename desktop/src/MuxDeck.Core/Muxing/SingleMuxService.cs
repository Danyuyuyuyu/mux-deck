using MuxDeck.Infrastructure.Muxing;
using MuxDeck.Infrastructure;
using MuxDeck.Domain;
using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using System.Security.Cryptography;
using System.Text.Json;

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
        var warnings = new List<Diagnostic>();
        var sourceMetadataCache = new Dictionary<(string Path, int Index), string>();
        string? logPath = null;
        string? work = null;
        FileTaskWorkspace? log = null;
        var committed = false;
        string? destination = null;
        try
        {
            // 在任何进度回调和异步等待前复制集合，任务只消费本次提交的参数快照。
            ArgumentNullException.ThrowIfNull(request);
            request = request with { Subtitles = request.Subtitles?.ToArray()!, Audios = request.Audios?.ToArray()! };
            log = (FileTaskWorkspace)await new FileTaskWorkspaceFactory().CreateAsync(workspace, cancellationToken).ConfigureAwait(false);
            logPath = log.Log.Path;
            work = log.WorkDirectory;
            var context = new ToolCallContext(workspace, log);
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
                var subtitle = snapshot.Subtitles[i];
                var result = await new AfsSubsetter(runner, subtitleIndex: i, diagnostics: warnings, metadataCache: sourceMetadataCache)
                    .SubsetAsync(new(new ExternalTrackSpec(subtitle.Path, TrackKind.Subtitle, null, subtitle.Name,
                        new(subtitle.Language), new(subtitle.IsDefault, subtitle.IsForced)), new(snapshot.FontsDirectory)),
                        context, cancellationToken: cancellationToken).ConfigureAwait(false);
                checks.Add($"字幕 {i + 1}：修正 ASS 与 {result.Fonts.Length} 个子集字体的引用、字形完整性通过");
                corrected.Add(result.CorrectedSubtitle.Path);
                fonts.AddRange(result.Fonts.Select(font => font.Path));
            }
            if (warnings.Count > 0)
                Stage(MuxStage.Subsetting, "子集化注意提示：" + string.Join("；", warnings.Select(w =>
                    $"{w.Message}（累计 {w.Occurrences} 次）")));

            // 附件名与字幕序号关联，两个字幕生成同名字体时也不产生身份歧义。
            var attachmentNames = fonts.Select((font, i) => $"{i + 1:D3}-{Path.GetFileName(font)}").ToArray();
            var temporaryMkv = Path.Combine(work, "result.mkv");
            var spec = LegacyMuxContractConverter.ToSpec(snapshot, workspace);
            var muxTracks = spec.ExternalTracks.Select((track, index) => track.Kind == TrackKind.Subtitle
                ? track with { Path = corrected[index] } : track).ToEquatableArray();
            Stage(MuxStage.Muxing, "保留视频和音轨，嵌入新增字幕与子集字体");
            await new MkvmergeMuxer(runner).MuxAsync(new(snapshot.VideoPath, muxTracks,
                fonts.Select(path => new WorkFile(path)).ToEquatableArray(), new(temporaryMkv)), context,
                cancellationToken: cancellationToken).ConfigureAwait(false);
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
            var extractor = new MkvextractExtractor(runner);
            await extractor.ExtractTracksAsync(temporaryMkv, subtitleTracks.Select((track, i) => new TrackExtraction(
                new(track.Id), new(Path.Combine(extracted, $"subtitle-{i}" + Path.GetExtension(corrected[i]))),
                TrackExtractionFormat.ContainerDefault)).ToEquatableArray(), context, cancellationToken).ConfigureAwait(false);
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
                await extractor.ExtractTracksAsync(referenceMks, [new(new(0), new(referenceAss),
                    TrackExtractionFormat.ContainerDefault)], context, cancellationToken).ConfigureAwait(false);
                if (!File.Exists(referenceAss) || new FileInfo(referenceAss).Length == 0)
                    throw new InvalidDataException($"新增字幕 {i + 1} 未产出有效 ASS 校验参考字幕。");
                if (!OutputQcRules.SubtitleContentEquals(SubtitleText.Read(referenceAss),
                    SubtitleText.Read(Path.Combine(extracted, $"subtitle-{i}" + Path.GetExtension(corrected[i])))))
                    throw new InvalidDataException($"新增字幕 {i + 1} 提取后内容与同工具字幕参考不符。");
            }
            await extractor.ExtractAttachmentsAsync(temporaryMkv, fonts.Select((_, i) => new AttachmentExtraction(
                outputProbe.Attachments[i].Id, new(Path.Combine(extracted, attachmentNames[i])))).ToEquatableArray(),
                context, cancellationToken).ConfigureAwait(false);
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
                await extractor.ExtractTracksAsync(reference, [new(new(referenceTrack.Id), new(beforeRaw),
                    TrackExtractionFormat.FullRaw)], context, cancellationToken).ConfigureAwait(false);
                await extractor.ExtractTracksAsync(temporaryMkv, [new(new(actual.Id), new(afterRaw),
                    TrackExtractionFormat.FullRaw)], context, cancellationToken).ConfigureAwait(false);
                if (!HashFile(beforeRaw).SequenceEqual(HashFile(afterRaw))) throw new InvalidDataException($"外加音频 {i + 1} 数据与参考不符。");
                checks.Add($"外加音频 {i + 1} 原始数据及 CodecPrivate SHA-256 一致");
            }

            Stage(MuxStage.Installing, "在输出目录准备成品并拒绝覆盖地提交");
            await new NoOverwritePublisher().PublishNoOverwriteAsync(new(temporaryMkv), destination, cancellationToken).ConfigureAwait(false);
            committed = true;
            log.Write("成功提交 " + destination);
            SafeReport(new MuxProgress(MuxStage.Completed, "封装完成", 100));
            return new MuxResult(MuxStatus.Succeeded, destination, null, logPath, checks.ToArray()) { Warnings = warnings.Select(warning => new MuxWarning(warning.Code, warning.Message, warning.Occurrences)).ToArray() };

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
            return new MuxResult(MuxStatus.Cancelled, null, "任务已取消", logPath, checks.ToArray()) { Warnings = warnings.Select(warning => new MuxWarning(warning.Code, warning.Message, warning.Occurrences)).ToArray() };
        }
        catch (Exception ex) when (!committed)
        {
            log?.Write("失败 " + ex);
            return new MuxResult(MuxStatus.Failed, null, ex.Message, logPath, checks.ToArray()) { Warnings = warnings.Select(warning => new MuxWarning(warning.Code, warning.Message, warning.Occurrences)).ToArray() };
        }
        finally
        {
            if (log is not null)
                try { await log.DisposeAsync().ConfigureAwait(false); }
                catch (MuxDeck.Application.Ports.Failures.ToolFailureException) { }

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

    private string LocateFontTools() => MuxDeck.Infrastructure.Components.FontToolsLocator.Find(workspace)
        ?? throw new FileNotFoundException("找不到同目录的 pyftsubset 与 ttx，AFS 需要可运行的 fontTools 后端。");

    // 旧入口保留编排；真实进程、超时、诊断和失败映射只有 Infrastructure 一份实现。
    private Task<ProcessResult> RunToolAsync(string executable, IReadOnlyList<string> args,
        string? work, FileTaskWorkspace? log, CancellationToken token, TimeSpan timeout,
        bool json = false, bool rejectDiagnostics = false, List<Diagnostic>? warnings = null) =>
        new ToolExecutor(runner).RunAsync(executable, args, work, log is null ? null : log.Write,
            token, timeout, json, rejectDiagnostics, warnings);

    private static MediaProbe ParseProbe(string path, string json)
    {
        var probe = MkvmergeProber.Parse(path, json);
        return new(probe.Path, probe.Tracks.Select(track => new MediaTrack(track.Id.Value,
            track.RawType, track.Codec, track.CodecId, track.Language.Value, track.LanguageIetf?.Value,
            track.Name, track.Flags.IsDefault, track.Flags.IsForced, track.PixelDimensions,
            track.AudioChannels, track.AudioSamplingFrequency, track.HasDefaultFlag, track.HasForcedFlag)).ToArray(),
            probe.Attachments.Select(attachment => new MediaAttachment(attachment.Id, attachment.FileName,
                attachment.ContentType, attachment.Size)).ToArray(), probe.DurationNanoseconds);
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
}
