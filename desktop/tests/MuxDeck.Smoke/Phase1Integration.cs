using MuxDeck.Infrastructure;
using MuxDeck.Infrastructure.Muxing;
using MuxDeck.Infrastructure.Components;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MuxDeck.Core;
using MuxDeck.Core.Muxing;

internal static class Phase1Integration
{
    public static async Task<int> RunAsync()
    {
        var workspace = Workspace.Discover();
        var runRoot = Path.Combine(workspace.Root, "desktop", "artifacts", "phase1",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runRoot);
        var observations = new List<CheckRecord>();
        var inputHashesBefore = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inputHashesAfter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var targetHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var probeSummaries = new List<object>();
        var runner = new ProcessRunner();
        var tools = new ToolLocator(workspace).Inspect();
        string? Tool(string name) => tools.FirstOrDefault(t => t.Name == name)?.Path;
        var ffmpeg = Tool("ffmpeg");
        var mkvmerge = Tool("mkvmerge");
        var mkvextract = Tool("mkvextract");
        var subsetter = Tool("AssFontSubset");

        void Check(bool condition, string name, string? detail = null)
        {
            observations.Add(new CheckRecord(name, condition, detail));
            Console.WriteLine($"{(condition ? "PASS" : "FAIL")}: {name}{(detail is null ? "" : " — " + detail)}");
        }

        async Task<ProcessResult> RunTool(string executable, IReadOnlyList<string> arguments,
            string logName, string? workingDirectory = null)
        {
            var result = await runner.RunAsync(new ProcessRequest(executable, arguments,
                workingDirectory, Encoding.UTF8));
            await File.WriteAllTextAsync(Path.Combine(runRoot, logName),
                $"Executable: {executable}{Environment.NewLine}Arguments: {string.Join(" | ", arguments)}" +
                Environment.NewLine + result.StandardOutput + Environment.NewLine + result.StandardError,
                new UTF8Encoding(false));
            if (result.ExitCode != 0)
                throw new InvalidOperationException($"{Path.GetFileName(executable)} exit={result.ExitCode}; see {logName}");
            return result;
        }

        async Task SaveReport()
        {
            var report = new
            {
                RunDirectory = runRoot,
                ToolPaths = new { ffmpeg, mkvmerge, mkvextract, subsetter },
                InputHashesBefore = inputHashesBefore,
                InputHashesAfter = inputHashesAfter,
                ProtectedTargetHashes = targetHashes,
                Probes = probeSummaries,
                Checks = observations,
                Passed = observations.Count(x => x.Passed),
                Failed = observations.Count(x => !x.Passed)
            };
            await File.WriteAllTextAsync(Path.Combine(runRoot, "report.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
        }

        var missing = new[] { ("ffmpeg", ffmpeg), ("mkvmerge", mkvmerge), ("mkvextract", mkvextract),
            ("AssFontSubset", subsetter) }.Where(x => x.Item2 is null).Select(x => x.Item1).ToArray();
        if (missing.Length > 0)
        {
            Check(false, "真实工具可用", "未找到：" + string.Join(", ", missing));
            await SaveReport();
            Console.Error.WriteLine($"未验收：请将缺少的工具加入 PATH 或 ToolLocator 工作区。产物目录：{runRoot}");
            return 2;
        }

        try
        {
            var fonts = Path.Combine(runRoot, "fonts-yahei");
            var missingFonts = Path.Combine(runRoot, "fonts-empty");
            var latinOnlyFonts = Path.Combine(runRoot, "fonts-arial-only");
            var inputDirectory = Path.Combine(runRoot, "input");
            Directory.CreateDirectory(fonts);
            Directory.CreateDirectory(missingFonts);
            Directory.CreateDirectory(latinOnlyFonts);
            Directory.CreateDirectory(inputDirectory);
            var source = Path.Combine(inputDirectory, "phase1-source.mkv");
            var mp4Source = Path.Combine(inputDirectory, "phase1-mp4-source.mp4");
            var baseVideo = Path.Combine(inputDirectory, "base-video.mkv");
            var legacySubtitle = Path.Combine(inputDirectory, "legacy.srt");
            var legacyAttachment = Path.Combine(inputDirectory, "LegacyFont.ttf");
            var scAss = Path.Combine(inputDirectory, "sc.ass");
            var tcAss = Path.Combine(inputDirectory, "tc.ass");
            var missingGlyphAss = Path.Combine(inputDirectory, "glyph-test.ass");
            var missingFontAss = Path.Combine(inputDirectory, "font-test.ass");

            CopyFont("msyh.ttc", fonts);
            CopyFont("msyhbd.ttc", fonts);
            CopyFont("arial.ttf", fonts);
            CopyFont("arialbd.ttf", fonts);
            CopyFont("ariali.ttf", fonts);
            CopyFont("arialbi.ttf", fonts);
            CopyFont("arial.ttf", latinOnlyFonts);
            File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "arial.ttf"),
                legacyAttachment, overwrite: false);

            static void CopyFont(string name, string destinationDirectory)
            {
                var windowsFontsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", name);
                if (!File.Exists(windowsFontsPath)) throw new FileNotFoundException("缺少 Windows 字体文件", windowsFontsPath);
                Directory.CreateDirectory(destinationDirectory);
                File.Copy(windowsFontsPath, Path.Combine(destinationDirectory, name), overwrite: false);
            }

            await File.WriteAllTextAsync(legacySubtitle,
                "1\r\n00:00:00,000 --> 00:00:08,000\r\nLegacy embedded subtitle\r\n",
                new UTF8Encoding(false));
            await File.WriteAllTextAsync(scAss, MakeAss("简体中文测试", "Simplified Chinese"), new UTF8Encoding(false));
            await File.WriteAllTextAsync(tcAss, MakeAss("繁體中文測試", "Traditional Chinese"), new UTF8Encoding(false));
            await File.WriteAllTextAsync(missingGlyphAss, MakeAss("缺字形测试", "Missing glyph", "Arial"),
                new UTF8Encoding(false));
            await File.WriteAllTextAsync(missingFontAss,
                MakeAss("缺少字体测试", "Missing font", "MuxDeck Phase1 Missing Font"), new UTF8Encoding(false));

            await RunTool(ffmpeg!, ["-hide_banner", "-y", "-f", "lavfi", "-i",
                "color=c=0x1c2b3d:s=1280x720:r=24:d=10", "-f", "lavfi", "-i",
                "sine=frequency=440:sample_rate=48000:duration=10", "-c:v", "libx264", "-preset", "ultrafast",
                "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", baseVideo], "fixture-ffmpeg.log");
            await RunTool(mkvmerge!, ["--output", source, "--no-subtitles", "--no-attachments", baseVideo,
                "--language", "0:en", "--track-name", "0:Legacy source subtitle", legacySubtitle,
                "--attachment-name", "LegacyFont.ttf", "--attachment-mime-type", "application/x-truetype-font",
                "--attach-file", legacyAttachment], "fixture-mkvmerge.log");
            await RunTool(ffmpeg!, ["-hide_banner", "-y", "-i", baseVideo, "-map", "0:v:0", "-map", "0:a:0",
                "-c", "copy", mp4Source], "fixture-mp4.log");

            Check(File.Exists(source), "生成隔离的短时视频源");
            var service = new SingleMuxService(workspace);
            var sourceProbe = await service.ProbeAsync(source, CancellationToken.None);
            probeSummaries.Add(SummarizeProbe("source-mkv", sourceProbe));
            Check(sourceProbe.Tracks.Any(IsType("video")) && sourceProbe.Tracks.Any(IsType("audio")) &&
                sourceProbe.Tracks.Any(IsType("subtitles")), "源 MKV 同时包含视频、音轨和旧字幕");
            Check(sourceProbe.Attachments.Any(a => a.FileName.Equals("LegacyFont.ttf", StringComparison.OrdinalIgnoreCase)),
                "源 MKV 包含旧字体附件");
            var mp4Probe = await service.ProbeAsync(mp4Source, CancellationToken.None);
            probeSummaries.Add(SummarizeProbe("source-mp4", mp4Probe));
            Check(mp4Probe.Tracks.Any(IsType("video")) && mp4Probe.Tracks.Any(IsType("audio")),
                "MP4 输入含视频和音轨");

            var sourceAndInputs = new[] { source, mp4Source, scAss, tcAss, missingGlyphAss, missingFontAss }
                .Concat(Directory.EnumerateFiles(fonts)).Concat(Directory.EnumerateFiles(latinOnlyFonts))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(p => p, HashFile, StringComparer.OrdinalIgnoreCase);
            foreach (var hash in sourceAndInputs) inputHashesBefore[hash.Key] = hash.Value;

            var sc = new SubtitleInput(scAss, "简体中文", "zh-Hans", IsDefault: true, IsForced: false);
            var tc = new SubtitleInput(tcAss, "繁體中文", "zh-Hant", IsDefault: false, IsForced: true);
            var tcOnly = new SubtitleInput(tcAss, "繁體中文", "zh-Hant", IsDefault: true, IsForced: false);
            var successResults = new Dictionary<string, (MuxResult Result, MediaProbe Probe, string Directory)>(StringComparer.Ordinal);

            async Task<(MuxResult Result, MediaProbe? Probe, string OutputDirectory)> RunMux(
                string label, IReadOnlyList<SubtitleInput> subtitles, string fontsDirectory,
                MuxStatus expectedStatus = MuxStatus.Succeeded, bool cancelAtSubsetting = false,
                string? videoPath = null, MediaProbe? sourceProbeOverride = null,
                Action<MuxProgress>? observeProgress = null, bool allowTargetAfterFailure = false,
                bool cancelAtCompleted = false)
            {
                var inputVideo = videoPath ?? source;
                var outputDirectory = Path.Combine(runRoot, "outputs", label);
                Directory.CreateDirectory(outputDirectory);
                using var cancellation = new CancellationTokenSource();
                var stages = new List<string>();
                var progress = new InlineProgress<MuxProgress>(p =>
                {
                    stages.Add(p.Stage.ToString());
                    if (cancelAtSubsetting && p.Stage == MuxStage.Subsetting) cancellation.Cancel();
                    if (cancelAtCompleted && p.Stage == MuxStage.Completed) cancellation.Cancel();
                    observeProgress?.Invoke(p);
                });
                var result = await service.RunAsync(new MuxRequest(inputVideo, subtitles, fontsDirectory, outputDirectory),
                    progress, cancellation.Token);
                await File.WriteAllTextAsync(Path.Combine(runRoot, $"{label}-stages.log"),
                    string.Join(Environment.NewLine, stages), new UTF8Encoding(false));
                var defaultName = Path.GetFileNameWithoutExtension(inputVideo) + ".muxed.mkv";
                var expectedPath = Path.Combine(outputDirectory, defaultName);
                Check(Path.GetFullPath(result.OutputPath ?? expectedPath).Equals(Path.GetFullPath(expectedPath),
                    StringComparison.OrdinalIgnoreCase), $"{label}: 默认输出文件名");
                Check(result.Status == (cancelAtSubsetting ? MuxStatus.Cancelled : expectedStatus),
                    $"{label}: 任务状态", $"{result.Status}; {result.Error}");
                Check(result.LogPath is not null && File.Exists(result.LogPath), $"{label}: Core 任务日志存在", result.LogPath);
                if (result.LogPath is not null && File.Exists(result.LogPath))
                {
                    var copiedLogs = Path.Combine(runRoot, "task-logs");
                    Directory.CreateDirectory(copiedLogs);
                    File.Copy(result.LogPath, Path.Combine(copiedLogs, label + ".log"), overwrite: true);
                }
                if (result.Status != MuxStatus.Succeeded || !File.Exists(expectedPath))
                {
                    if (!allowTargetAfterFailure)
                        Check(!File.Exists(expectedPath), $"{label}: 未成功时没有发布目标");
                    return (result, null, outputDirectory);
                }

                var probe = await service.ProbeAsync(expectedPath, CancellationToken.None);
                probeSummaries.Add(SummarizeProbe(label, probe));
                VerifyOutput(label, sourceProbeOverride ?? sourceProbe, probe, subtitles);
                return (result, probe, outputDirectory);
            }

            void VerifyOutput(string label, MediaProbe before, MediaProbe after, IReadOnlyList<SubtitleInput> expectedSubtitles)
            {
                Check(AvTracksRetained(before, after), $"{label}: 原视频和全部音轨保留",
                    DescribeAvTrackDifference(before, after));
                var subtitleTracks = after.Tracks.Where(IsType("subtitles")).ToArray();
                Check(subtitleTracks.Length == expectedSubtitles.Count, $"{label}: 新字幕轨数量", $"实际 {subtitleTracks.Length} / 预期 {expectedSubtitles.Count}");
                foreach (var expected in expectedSubtitles)
                {
                    var track = subtitleTracks.SingleOrDefault(t => string.Equals(t.Name, expected.Name, StringComparison.Ordinal));
                    Check(track is not null, $"{label}: 字幕轨名称 {expected.Name}");
                    if (track is null) continue;
                    Check(string.Equals(track.Language, expected.Language, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(track.LanguageIetf, expected.Language, StringComparison.OrdinalIgnoreCase),
                        $"{label}: {expected.Name} 语言", $"{track.Language} / {track.LanguageIetf}");
                    Check(track.IsDefault == expected.IsDefault, $"{label}: {expected.Name} default={expected.IsDefault}");
                    Check(track.IsForced == expected.IsForced, $"{label}: {expected.Name} forced={expected.IsForced}");
                }
                Check(!after.Tracks.Any(t => string.Equals(t.Name, "Legacy source subtitle", StringComparison.Ordinal)),
                    $"{label}: 移除源字幕轨");
                Check(!after.Attachments.Any(a => a.FileName.Equals("LegacyFont.ttf", StringComparison.OrdinalIgnoreCase)),
                    $"{label}: 移除源附件");
                Check(after.Attachments.Any(a => a.Size > 0 && IsFontAttachment(a)), $"{label}: 子集字体已作为附件封装");
                Check(after.Attachments.All(IsFontAttachment), $"{label}: 成品附件全部来自本任务子集字体");
            }

            var single = await RunMux("single-sc", [sc], fonts);
            if (single.Probe is not null) successResults.Add("single-sc", (single.Result, single.Probe, single.OutputDirectory));
            var dual = await RunMux("dual-sc-tc", [sc, tc], fonts);
            if (dual.Probe is not null) successResults.Add("dual-sc-tc", (dual.Result, dual.Probe, dual.OutputDirectory));
            var tcOnlyResult = await RunMux("tc-only", [tcOnly], fonts);
            if (tcOnlyResult.Probe is not null) successResults.Add("tc-only", (tcOnlyResult.Result, tcOnlyResult.Probe, tcOnlyResult.OutputDirectory));
            var mp4 = await RunMux("mp4-to-mkv", [sc], fonts, videoPath: mp4Source, sourceProbeOverride: mp4Probe);
            Check(mp4.Probe is not null && string.Equals(Path.GetExtension(mp4.Result.OutputPath ?? ""), ".mkv", StringComparison.OrdinalIgnoreCase),
                "MP4 源成功封装为 MKV", mp4.Result.Error);

            var installRaceDirectory = Path.Combine(runRoot, "outputs", "install-race");
            var installRaceName = Path.GetFileNameWithoutExtension(source) + ".muxed.mkv";
            var installRacePath = Path.Combine(installRaceDirectory, installRaceName);
            var installRaceBytes = Encoding.UTF8.GetBytes("installed-by-another-writer");
            var installRaceHash = Convert.ToHexString(SHA256.HashData(installRaceBytes));
            var installRaceWritten = false;
            var installRace = await RunMux("install-race", [sc], fonts, MuxStatus.Failed,
                observeProgress: progress =>
                {
                    if (progress.Stage != MuxStage.Installing || installRaceWritten) return;
                    Directory.CreateDirectory(installRaceDirectory);
                    File.WriteAllBytes(installRacePath, installRaceBytes);
                    installRaceWritten = true;
                }, allowTargetAfterFailure: true);
            Check(installRaceWritten, "安装阶段观察到最终目标并制造并发冲突");
            Check(installRace.Result.Status == MuxStatus.Failed, "安装阶段目标竞态导致失败", installRace.Result.Error);
            Check(File.Exists(installRacePath) && HashFile(installRacePath) == installRaceHash,
                "安装阶段目标竞态保留并发创建者的原始字节");
            if (File.Exists(installRacePath)) targetHashes[installRacePath] = HashFile(installRacePath);

            var completedOutputPath = Path.Combine(runRoot, "outputs", "cancel-at-completed",
                Path.GetFileNameWithoutExtension(source) + ".muxed.mkv");
            var cancelledAfterCommit = false;
            var completed = await RunMux("cancel-at-completed", [sc], fonts,
                observeProgress: progress =>
                {
                    if (progress.Stage == MuxStage.Completed) cancelledAfterCommit = true;
                }, cancelAtCompleted: true);
            Check(cancelledAfterCommit, "已提交后触发 Completed 取消回调");
            Check(completed.Result.Status == MuxStatus.Succeeded && File.Exists(completedOutputPath),
                "提交点之后的取消不推翻成功结果", completed.Result.Error);

            var conflictDirectory = Path.Combine(runRoot, "outputs", "existing-output");
            Directory.CreateDirectory(conflictDirectory);
            var conflictPath = Path.Combine(conflictDirectory, Path.GetFileNameWithoutExtension(source) + ".muxed.mkv");
            await File.WriteAllTextAsync(conflictPath, "keep-existing-output", new UTF8Encoding(false));
            var conflictHash = HashFile(conflictPath);
            targetHashes[conflictPath] = conflictHash;
            var conflict = await service.RunAsync(new MuxRequest(source, [sc], fonts, conflictDirectory),
                cancellationToken: CancellationToken.None);
            Check(conflict.Status == MuxStatus.Failed, "已有输出时任务失败", conflict.Error);
            Check(HashFile(conflictPath) == conflictHash, "已有目标字节保持不变");
            targetHashes[conflictPath] = HashFile(conflictPath);
            if (conflict.LogPath is not null && File.Exists(conflict.LogPath))
            {
                var copiedLogs = Path.Combine(runRoot, "task-logs");
                Directory.CreateDirectory(copiedLogs);
                File.Copy(conflict.LogPath, Path.Combine(copiedLogs, "existing-output.log"), overwrite: true);
            }

            var missingFontInput = new SubtitleInput(missingFontAss, "缺少字体检查", "zh-Hans", IsDefault: true);
            var missingFontResult = await RunMux("missing-font", [missingFontInput], missingFonts, MuxStatus.Failed);
            Check(missingFontResult.Result.Status == MuxStatus.Failed &&
                ContainsAny(missingFontResult.Result.Error, "Not found font", "找不到字体"),
                "字体缺失以明确的字体诊断失败", missingFontResult.Result.Error);
            var missingGlyphInput = new SubtitleInput(missingGlyphAss, "缺字形检查", "zh-Hans", IsDefault: true);
            var missingGlyphResult = await RunMux("missing-glyph", [missingGlyphInput], latinOnlyFonts, MuxStatus.Failed);
            Check(missingGlyphResult.Result.Status == MuxStatus.Failed &&
                ContainsAny(missingGlyphResult.Result.Error, "缺字形") &&
                Regex.IsMatch(missingGlyphResult.Result.Error ?? "", @"U\+[0-9A-F]{4,}", RegexOptions.IgnoreCase),
                "Arial 缺中文字形以码位诊断拒绝继续", missingGlyphResult.Result.Error);

            var cancelled = await RunMux("cancel-subsetting", [sc], fonts, cancelAtSubsetting: true);
            Check(cancelled.Result.Status == MuxStatus.Cancelled, "子集化阶段同步取消");

            await VerifyProcessTreeCancellation(runRoot, subsetter!, Check);
            await VerifyIetfAudioMetadataIsChecked(runRoot, source, scAss, fonts, Check);

            var sourceHashesAfter = sourceAndInputs.ToDictionary(x => x.Key, x => HashFile(x.Key), StringComparer.OrdinalIgnoreCase);
            foreach (var hash in sourceHashesAfter) inputHashesAfter[hash.Key] = hash.Value;
            Check(sourceAndInputs.All(x => sourceHashesAfter[x.Key] == x.Value), "视频、ASS 与字体源文件 SHA-256 不变");

            if (successResults.TryGetValue("dual-sc-tc", out var renderSource))
            {
                var outputPath = Path.Combine(renderSource.Directory, Path.GetFileNameWithoutExtension(source) + ".muxed.mkv");
                await RenderTrack(0, "render-sc.png", renderSource.Probe, outputPath, renderSource.Result);
                await RenderTrack(1, "render-tc.png", renderSource.Probe, outputPath, renderSource.Result);
            }

            await SaveReport();
            Console.WriteLine($"验收文件：{runRoot}");
            Console.WriteLine($"{observations.Count(x => x.Passed)}/{observations.Count} PASS");
            return observations.Any(x => !x.Passed) ? 1 : 0;

            async Task RenderTrack(int subtitleIndex, string imageName, MediaProbe outputProbe, string outputPath, MuxResult muxResult)
            {
                var imagePath = Path.Combine(runRoot, imageName);
                var filter = $"subtitles=filename='{Path.GetFileName(outputPath)}':si={subtitleIndex}";
                ProcessResult rendered;
                try
                {
                    rendered = await RunTool(ffmpeg!, ["-hide_banner", "-loglevel", "debug",
                        "-i", outputPath, "-map", "0:v:0", "-vf", filter, "-ss", "00:00:02",
                        "-frames:v", "1", "-y", imagePath],
                        imageName + ".log", Path.GetDirectoryName(outputPath));
                }
                catch (Exception ex)
                {
                    Check(false, $"{imageName}: ffmpeg 渲染", ex.Message);
                    return;
                }

                Check(File.Exists(imagePath) && new FileInfo(imagePath).Length > 0, $"生成 {imageName}");
                var debug = rendered.StandardError;
                var loadedFonts = debug.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
                    .Where(line => line.Contains("Loading attached font", StringComparison.OrdinalIgnoreCase)).ToArray();
                var fontSelect = debug.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
                    .Where(line => line.Contains("fontselect:", StringComparison.OrdinalIgnoreCase)).ToArray();
                var attachments = outputProbe.Attachments.Where(IsFontAttachment).ToArray();
                Check(loadedFonts.Length > 0, $"{imageName}: libass 加载了 MKV 内封字体", string.Join(" | ", loadedFonts));
                Check(fontSelect.Length > 0, $"{imageName}: libass fontselect 有选择记录", string.Join(" | ", fontSelect));
                if (loadedFonts.Length > 0 && attachments.Length > 0)
                    Check(loadedFonts.Any(line => attachments.Any(font => line.Contains(font.FileName, StringComparison.OrdinalIgnoreCase))),
                        $"{imageName}: 已加载的文件对应输出子集附件", string.Join(", ", attachments.Select(a => a.FileName)));

                var extracted = Path.Combine(runRoot, "extracted", $"subtitle-{subtitleIndex}.ass");
                Directory.CreateDirectory(Path.GetDirectoryName(extracted)!);
                var subtitle = outputProbe.Tracks.Where(IsType("subtitles")).ElementAtOrDefault(subtitleIndex);
                if (subtitle is not null)
                {
                    try
                    {
                        await RunTool(mkvextract!, [outputPath, "tracks", $"{subtitle.Id}:{extracted}"],
                            $"extract-subtitle-{subtitleIndex}.log");
                        if (File.Exists(extracted))
                        {
                            var text = await File.ReadAllTextAsync(extracted, Encoding.UTF8);
                            var embeddedFamilies = ReadAssFontFamilies(text);
                            var requestedFamilies = fontSelect.Select(ParseFontRequest).Where(x => x is not null)
                                .Select(x => x!).ToArray();
                            var renamed = embeddedFamilies.Where(f => !string.Equals(f, "Microsoft YaHei", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(f, "Arial", StringComparison.OrdinalIgnoreCase)).ToArray();
                            Check(renamed.Length > 0, $"{imageName}: ASS 引用了 AFS 子集重命名字体", string.Join(", ", embeddedFamilies));
                            Check(renamed.Any(name => requestedFamilies.Any(requested =>
                                string.Equals(name, requested, StringComparison.OrdinalIgnoreCase))),
                                $"{imageName}: fontselect 选择 ASS 中的重命名子集字体",
                                $"ASS={string.Join(", ", embeddedFamilies)}; fontselect={string.Join(" | ", fontSelect)}");
                            Check(muxResult.LogPath is not null && File.Exists(muxResult.LogPath),
                                $"{imageName}: Core 子集化/封装日志保留", muxResult.LogPath);
                        }
                    }
                    catch (Exception ex) { Check(false, $"{imageName}: 提取并核对 ASS 字体名", ex.Message); }
                }
                else Check(false, $"{imageName}: 找到渲染字幕轨");
            }
        }
        catch (Exception ex)
        {
            Check(false, "集成验收执行", ex.ToString());
        }

        await SaveReport();
        Console.WriteLine($"验收文件：{runRoot}");
        Console.WriteLine($"{observations.Count(x => x.Passed)}/{observations.Count} PASS");
        return observations.Any(x => !x.Passed) ? 1 : 0;
    }

    private static Func<MediaTrack, bool> IsType(string expected) =>
        track => string.Equals(track.Type, expected, StringComparison.OrdinalIgnoreCase) ||
            expected == "subtitles" && string.Equals(track.Type, "subtitle", StringComparison.OrdinalIgnoreCase);

    private static bool IsAv(string type) =>
        string.Equals(type, "video", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "audio", StringComparison.OrdinalIgnoreCase);

    private static bool AvTracksRetained(MediaProbe source, MediaProbe output) =>
        new[] { "video", "audio" }.All(type =>
        {
            var before = source.Tracks.Where(track => string.Equals(track.Type, type, StringComparison.OrdinalIgnoreCase)).ToArray();
            var after = output.Tracks.Where(track => string.Equals(track.Type, type, StringComparison.OrdinalIgnoreCase)).ToArray();
            return before.Length == after.Length && before.Zip(after).All(pair => TrackRetained(pair.First, pair.Second));
        });

    private static bool TrackRetained(MediaTrack source, MediaTrack output) =>
        source.Codec == output.Codec &&
        (source.CodecId is null || source.CodecId == output.CodecId) &&
        source.Language == output.Language &&
        (source.LanguageIetf is null || source.LanguageIetf == output.LanguageIetf) &&
        source.Name == output.Name &&
        (!source.HasDefaultFlag || source.IsDefault == output.IsDefault) &&
        (!source.HasForcedFlag || source.IsForced == output.IsForced) &&
        (source.PixelDimensions is null || source.PixelDimensions == output.PixelDimensions) &&
        (source.AudioChannels is null || source.AudioChannels == output.AudioChannels) &&
        (source.AudioSamplingFrequency is null || source.AudioSamplingFrequency == output.AudioSamplingFrequency);

    private static string DescribeAvTrackDifference(MediaProbe source, MediaProbe output)
    {
        foreach (var type in new[] { "video", "audio" })
        {
            var before = source.Tracks.Where(track => string.Equals(track.Type, type, StringComparison.OrdinalIgnoreCase)).ToArray();
            var after = output.Tracks.Where(track => string.Equals(track.Type, type, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (before.Length != after.Length) return $"{type} 轨数：源 {before.Length} / 成品 {after.Length}";
            for (var i = 0; i < before.Length; i++)
                if (!TrackRetained(before[i], after[i]))
                    return $"{type} 第 {i + 1} 轨源/成品：{JsonSerializer.Serialize(before[i])} / {JsonSerializer.Serialize(after[i])}";
        }
        return "已知的编码、媒体属性、语言、轨名及已声明旗标一致；MP4 未声明的 Matroska 字段不作猜测比较。";
    }

    private static bool ContainsAny(string? value, params string[] patterns) => value is not null &&
        patterns.Any(pattern => value.Contains(pattern, StringComparison.OrdinalIgnoreCase));

    private static async Task VerifyProcessTreeCancellation(string runRoot, string subsetterPath,
        Action<bool, string, string?> check)
    {
        var fixture = Path.Combine(runRoot, "process-tree-cancellation");
        var inputDirectory = Path.Combine(fixture, "input");
        var fontsDirectory = Path.Combine(fixture, "fonts");
        var outputDirectory = Path.Combine(fixture, "output");
        Directory.CreateDirectory(inputDirectory);
        Directory.CreateDirectory(fontsDirectory);
        Directory.CreateDirectory(outputDirectory);
        var fakeSource = Path.Combine(inputDirectory, "tree-cancel.mkv");
        var fakeSubtitle = Path.Combine(inputDirectory, "tree-cancel.ass");
        await File.WriteAllTextAsync(fakeSource, "fixture source", new UTF8Encoding(false));
        await File.WriteAllTextAsync(fakeSubtitle, MakeAss("测试", "Tree cancel"), new UTF8Encoding(false));
        var fakeRunner = new ProcessTreeMuxToolRunner(subsetterPath);
        var service = new SingleMuxService(Workspace.Discover(), fakeRunner);
        using var cancellation = new CancellationTokenSource();
        var run = service.RunAsync(new MuxRequest(fakeSource,
                [new SubtitleInput(fakeSubtitle, "取消测试", "zh-Hans", true)], fontsDirectory, outputDirectory),
            cancellationToken: cancellation.Token);
        try
        {
            var descendantId = await fakeRunner.DescendantPid.Task.WaitAsync(TimeSpan.FromSeconds(20));
            cancellation.Cancel();
            var result = await run.WaitAsync(TimeSpan.FromSeconds(20));
            check(result.Status == MuxStatus.Cancelled, "AFS 进程运行中取消得到 Cancelled", result.Error);
            check(!File.Exists(Path.Combine(outputDirectory, "tree-cancel.muxed.mkv")),
                "AFS 进程运行中取消没有发布成品", null);
            check(await WaitForProcessExit(descendantId, TimeSpan.FromSeconds(10)),
                "AFS 子进程树取消后后代退出", $"descendant PID={descendantId}");
        }
        catch (Exception ex)
        {
            cancellation.Cancel();
            try { await run.WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
            check(false, "AFS 进程运行中取消用例完成", ex.Message);
        }

        var pipeRunner = new ProcessRunner();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var elapsed = Stopwatch.StartNew();
        try
        {
            var result = await pipeRunner.RunAsync(new ProcessRequest(Environment.ProcessPath!,
                CurrentProgramArguments("--tree-parent-exit")), cancellationToken: timeout.Token);
            var pidLine = result.StandardOutput.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(line => line.StartsWith("descendant-pid:", StringComparison.Ordinal));
            var childPid = pidLine is not null && int.TryParse(pidLine["descendant-pid:".Length..], out var parsed)
                ? parsed : -1;
            check(result.ExitCode == 0 && elapsed.Elapsed < TimeSpan.FromSeconds(8),
                "父进程退出且后代保持重定向管道时 ProcessRunner 有界返回",
                $"exit={result.ExitCode}, elapsed={elapsed.Elapsed}, pid={childPid}");
            check(childPid > 0 && await WaitForProcessExit(childPid, TimeSpan.FromSeconds(5)),
                "父进程退出后管道后代被收尾", $"descendant PID={childPid}");
        }
        catch (Exception ex)
        {
            check(false, "父进程退出且后代持有管道的 ProcessRunner 回归", ex.Message);
        }
    }

    private static async Task VerifyIetfAudioMetadataIsChecked(string runRoot, string sourcePath, string subtitlePath,
        string fontsPath,
        Action<bool, string, string?> check)
    {
        var outputDirectory = Path.Combine(runRoot, "outputs", "ietf-mismatch");
        Directory.CreateDirectory(outputDirectory);
        var runner = new IetfMismatchMuxToolRunner(sourcePath);
        var service = new SingleMuxService(Workspace.Discover(), runner);
        var result = await service.RunAsync(new MuxRequest(sourcePath,
            [new SubtitleInput(subtitlePath, "IETF 检查", "zh-Hans", true)], fontsPath, outputDirectory));
        var metadataFailure = result.Error is { } error &&
            error.Contains("audio 轨", StringComparison.OrdinalIgnoreCase) &&
            error.Contains("元数据", StringComparison.OrdinalIgnoreCase);
        check(runner.SourceAudioIetfMutated && runner.OutputAudioIetfMutated &&
            result.Status == MuxStatus.Failed && metadataFailure,
            "源音轨已知 IETF 与成品不一致时 QC 阻止发布", result.Error);
        check(!File.Exists(Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(sourcePath) + ".muxed.mkv")),
            "IETF 不一致的任务没有发布成品", null);
    }

    private static async Task<bool> WaitForProcessExit(int processId, TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited) return true;
            }
            catch (ArgumentException) { return true; }
            await Task.Delay(50);
        }
        return false;
    }

    private static string[] CurrentProgramArguments(string mode)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法识别 Smoke 执行文件。");
        var prefix = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? new[] { System.Reflection.Assembly.GetEntryAssembly()!.Location } : Array.Empty<string>();
        return [.. prefix, mode];
    }

    private sealed class ProcessTreeMuxToolRunner(string subsetterPath) : IMuxToolRunner
    {
        private readonly ProcessRunner processRunner = new();
        public TaskCompletionSource<int> DescendantPid { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessLine>? progress,
            CancellationToken cancellationToken)
        {
            if (Path.GetFullPath(request.Executable).Equals(Path.GetFullPath(subsetterPath), StringComparison.OrdinalIgnoreCase))
            {
                var sink = new InlineProgress<ProcessLine>(line =>
                {
                    if (!line.IsError && line.Text.StartsWith("descendant-pid:", StringComparison.Ordinal) &&
                        int.TryParse(line.Text["descendant-pid:".Length..], out var pid))
                        DescendantPid.TrySetResult(pid);
                    progress?.Report(line);
                });
                return processRunner.RunAsync(request with
                {
                    Executable = Environment.ProcessPath!,
                    Arguments = CurrentProgramArguments("--tree-parent"),
                    MaxCapturedCharacters = 4096
                }, sink, cancellationToken);
            }
            if (request.Arguments.Contains("-J", StringComparer.Ordinal))
                return Task.FromResult(new ProcessResult(0, MinimalProbeJson(), ""));
            return Task.FromResult(new ProcessResult(0, "", ""));
        }
    }

    private sealed class IetfMismatchMuxToolRunner(string sourcePath) : IMuxToolRunner
    {
        private readonly ProcessRunner processRunner = new();
        public bool SourceAudioIetfMutated { get; private set; }
        public bool OutputAudioIetfMutated { get; private set; }

        public async Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessLine>? progress,
            CancellationToken cancellationToken)
        {
            var result = await processRunner.RunAsync(request, progress, cancellationToken);
            if (request.Arguments.Count < 2 || request.Arguments[0] != "-J" || result.ExitCode != 0) return result;
            var probedPath = Path.GetFullPath(request.Arguments[1]);
            string? ietf = null;
            if (probedPath.Equals(Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
            {
                ietf = "en-GB";
                SourceAudioIetfMutated = true;
            }
            else if (Path.GetFileName(probedPath).Equals("result.mkv", StringComparison.OrdinalIgnoreCase))
            {
                ietf = "en-US";
                OutputAudioIetfMutated = true;
            }
            if (ietf is null) return result;
            var root = JsonNode.Parse(result.StandardOutput) ?? throw new InvalidDataException("缺少探测 JSON。");
            var audio = root["tracks"]!.AsArray().First(track =>
                string.Equals(track!["type"]!.GetValue<string>(), "audio", StringComparison.OrdinalIgnoreCase));
            audio!["properties"]!["language_ietf"] = ietf;
            return result with { StandardOutput = root.ToJsonString() };
        }
    }

    private static string MinimalProbeJson() => """
        {"tracks":[
          {"id":0,"type":"video","codec":"AVC/H.264/MPEG-4p10","properties":{"codec_id":"V_MPEG4/ISO/AVC","language":"und","language_ietf":"und","track_name":"","default_track":true,"forced_track":false,"pixel_dimensions":"1280x720"}},
          {"id":1,"type":"audio","codec":"AAC","properties":{"codec_id":"A_AAC-2","language":"eng","language_ietf":"en-US","track_name":"","default_track":true,"forced_track":false,"audio_channels":1,"audio_sampling_frequency":48000}}
        ],"attachments":[]}
        """;

    private static object SummarizeProbe(string label, MediaProbe probe) => new
    {
        Label = label,
        probe.Path,
        probe.DurationNanoseconds,
        Tracks = probe.Tracks.Select(track => new
        {
            track.Id,
            track.Type,
            track.Codec,
            track.CodecId,
            track.Language,
            track.LanguageIetf,
            track.Name,
            track.IsDefault,
            track.IsForced,
            track.HasDefaultFlag,
            track.HasForcedFlag,
            track.PixelDimensions,
            track.AudioChannels,
            track.AudioSamplingFrequency
        }).ToArray(),
        Attachments = probe.Attachments
    };

    private static bool IsFontAttachment(MediaAttachment attachment) =>
        attachment.FileName.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
        attachment.FileName.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ||
        attachment.FileName.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase) ||
        attachment.ContentType.Contains("font", StringComparison.OrdinalIgnoreCase);

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string MakeAss(string cjkText, string englishLabel, string fontName = "Microsoft YaHei") =>
        $"[Script Info]\r\nScriptType: v4.00+\r\nPlayResX: 1280\r\nPlayResY: 720\r\nScaledBorderAndShadow: yes\r\nWrapStyle: 2\r\n\r\n" +
        "[V4+ Styles]\r\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\r\n" +
        $"Style: Default,{fontName},42,&H00FFFFFF,&H000000FF,&H00101820,&H80000000,0,0,1,2,1,2,70,70,58,1\r\n\r\n" +
        "[Events]\r\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\r\n" +
        $"Dialogue: 0,0:00:01.00,0:00:08.00,Default,,0,0,0,,{{\\fnArial}}{englishLabel}: English {{\\b1}}bold{{\\b0}} {{\\i1}}italic{{\\i0}}\\N{{\\fn{fontName}}}{cjkText}\r\n";

    private static IReadOnlyList<string> ReadAssFontFamilies(string ass)
    {
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in ass.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var style = Regex.Match(line, @"^Style:\s*[^,]+,([^,]+)", RegexOptions.IgnoreCase);
            if (style.Success) families.Add(style.Groups[1].Value.Trim());
            foreach (Match overrideTag in Regex.Matches(line, @"\\fn([^\\}]+)", RegexOptions.IgnoreCase))
                families.Add(overrideTag.Groups[1].Value.Trim());
        }
        return families.ToArray();
    }

    private static string? ParseFontRequest(string line)
    {
        var match = Regex.Match(line, @"fontselect:\s*\(([^,]+),", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private sealed record CheckRecord(string Name, bool Passed, string? Detail);

    private sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
