using MuxDeck.Infrastructure;
using MuxDeck.Infrastructure.Muxing;
using MuxDeck.Infrastructure.Components;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using MuxDeck.Core;
using MuxDeck.Core.Muxing;
using MuxDeck.Desktop.ViewModels;

internal static class WarningPolicyChecks
{
    private const string ColorWarningCode = "AFS_COLOR_HIGH_BYTE_IGNORED";
    private const string SyntheticBoldWarningCode = "FONT_SYNTHETIC_BOLD";
    private const string IntrinsicWeightWarningCode = "FONT_INTRINSIC_WEIGHT";
    private const string Timestamp = "2026-10-09T19:59:20+08:00";
    private const string ColorWarning = "ASS color tag \\3c has more than 6 hex digits; high byte ignored.";

    public static async Task<int> RunAsync()
    {
        var repo = Workspace.Discover();
        var runRoot = Path.Combine(repo.Root, "desktop", "artifacts", "warning-policy",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runRoot);
        var checks = new List<CheckRecord>();

        void Check(bool passed, string name, string? detail = null)
        {
            checks.Add(new CheckRecord(name, passed, detail));
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}{(detail is null ? "" : " — " + detail)}");
        }

        async Task<Observation> RunCase(Scenario scenario)
        {
            var caseRoot = Path.Combine(runRoot, scenario.Name);
            var workspaceRoot = Path.Combine(caseRoot, "workspace");
            var inputRoot = Path.Combine(caseRoot, "input");
            var fonts = Path.Combine(caseRoot, "fonts");
            var output = Path.Combine(caseRoot, "output");
            Directory.CreateDirectory(inputRoot);
            Directory.CreateDirectory(fonts);
            Directory.CreateDirectory(output);
            if (scenario.IntrinsicFace)
            {
                var intrinsicFont = Path.Combine(fonts, "mux-intrinsic.ttf");
                await File.WriteAllBytesAsync(intrinsicFont, [8, 7, 6, 5, 4, 3, 2, 1]);
                if (scenario.DuplicateIntrinsicSource)
                {
                    var duplicateDirectory = Path.Combine(fonts, "duplicate-source");
                    Directory.CreateDirectory(duplicateDirectory);
                    await File.WriteAllBytesAsync(Path.Combine(duplicateDirectory, "mux-intrinsic.ttf"),
                        [1, 2, 3, 4, 5, 6, 7, 8]);
                }
            }
            PrepareFakeTools(workspaceRoot);

            var source = Path.Combine(inputRoot, scenario.SourceFileName);
            await File.WriteAllBytesAsync(source, Encoding.UTF8.GetBytes("source sentinel: " + scenario.Name));
            var subtitles = new List<SubtitleInput>();
            for (var i = 0; i < scenario.SubtitleCount; i++)
            {
                var path = Path.Combine(inputRoot, i == 0 ? "sc.ass" : "tc.ass");
                var fontName = scenario.IntrinsicFace
                    ? (scenario.IntrinsicGenericName ? "Mux Intrinsic" : "Mux Intrinsic Heavy")
                    : i == 1 && scenario.SecondFontName is { } secondaryFont ? secondaryFont : "Mux Synthetic";
                await File.WriteAllTextAsync(path, MakeAss(fontName, bold: scenario.BoldRequest), new UTF8Encoding(false));
                subtitles.Add(i == 0
                    ? new SubtitleInput(path, "简体字幕", "zh-Hans", true, false)
                    : new SubtitleInput(path, "繁體字幕", "zh-Hant", false, true));
            }

            using var cancellation = scenario.CancelAfterPartialWarning ? new CancellationTokenSource() : null;
            var runner = new WarningPolicyRunner(source, subtitles, scenario, fonts, cancellation);
            var service = new SingleMuxService(new Workspace(workspaceRoot), runner);
            var protectedHashes = new[] { source }.Concat(subtitles.Select(item => item.Path))
                .Concat(Directory.EnumerateFiles(fonts, "*", SearchOption.AllDirectories))
                .ToDictionary(path => path, Hash, StringComparer.OrdinalIgnoreCase);
            var result = await service.RunAsync(new MuxRequest(source, subtitles, fonts, output),
                cancellationToken: cancellation?.Token ?? CancellationToken.None);
            var expectedOutput = Path.Combine(output, Path.GetFileNameWithoutExtension(source) + ".muxed.mkv");
            var sourceUnchanged = protectedHashes.All(pair => File.Exists(pair.Key) && Hash(pair.Key) == pair.Value);
            return new Observation(result, runner, expectedOutput, sourceUnchanged);
        }

        var allowed = await RunCase(new Scenario("allowed-color", Diagnostics: _ =>
            [new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning)]));
        Check(allowed.Result.Status == MuxStatus.Succeeded, "精确颜色高字节告警允许任务继续", allowed.Result.Error);
        Check(allowed.Result.Warnings.Count == 1 && allowed.Result.Warnings[0].Code == ColorWarningCode &&
            allowed.Result.Warnings[0].Occurrences == 1 && allowed.Result.Warnings[0].Message.Contains("颜色", StringComparison.Ordinal),
            "成功结果暴露可见、可计数的颜色提示", JsonSerializer.Serialize(allowed.Result.Warnings));
        Check(allowed.Runner.TtxCalls > 0 && allowed.Runner.MuxCalls == 1 &&
            allowed.Runner.ExtractedTrackCount == 1 && File.Exists(allowed.ExpectedOutput),
            "允许的颜色提示后仍完成字形、封装、提取 QC 与发布", allowed.Result.Error);
        Check(allowed.SourceUnchanged, "允许提示的任务保持视频、字幕和字体输入 SHA-256 不变");

        var commentTransport = await RunCase(new Scenario("ass-reference-comment-relocation",
            RelocateCommentsDuringTransport: true));
        Check(commentTransport.Result.Status == MuxStatus.Succeeded &&
            commentTransport.Runner.ReferenceMuxCalls == 1 &&
            commentTransport.Runner.ReferenceExtractCalls == 1 &&
            commentTransport.Runner.MuxCalls == 1 && File.Exists(commentTransport.ExpectedOutput),
            "同一 MKVToolNix 往返参考允许 Comment 元数据的已验证位置重排", commentTransport.Result.Error);

        foreach (var (mutation, name) in new[]
        {
            (AssMutation.DialogueText, "dialogue-text"),
            (AssMutation.DialogueStartTime, "dialogue-time"),
            (AssMutation.StyleName, "style-name"),
            (AssMutation.FontName, "font-name"),
            (AssMutation.DialogueOrder, "dialogue-order"),
            (AssMutation.DialogueTextSpaceAdded, "dialogue-text-trailing-space-added"),
            (AssMutation.DialogueTextSpaceRemoved, "dialogue-text-trailing-space-removed"),
        })
        {
            var changed = await RunCase(new Scenario("ass-reference-mismatch-" + name,
                RelocateCommentsDuringTransport: true, Mutation: mutation));
            Check(changed.Result.Status == MuxStatus.Failed &&
                changed.Result.Error?.Contains("提取后内容", StringComparison.Ordinal) == true &&
                changed.Runner.ReferenceMuxCalls == 1 && changed.Runner.MuxCalls == 1 &&
                !File.Exists(changed.ExpectedOutput),
                $"字幕内容 QC 拒绝实际提取发生 {name} 变化并阻止发布", changed.Result.Error);
        }

        var referenceMissing = await RunCase(new Scenario("ass-reference-missing-container",
            OmitReferenceMks: true));
        Check(referenceMissing.Result.Status == MuxStatus.Failed &&
            referenceMissing.Result.Error?.Contains("参考容器", StringComparison.Ordinal) == true &&
            !File.Exists(referenceMissing.ExpectedOutput),
            "同工具 ASS 参考容器缺失时阻止发布", referenceMissing.Result.Error);

        var syntheticBold = await RunCase(new Scenario("synthetic-bold-unique-regular",
            Diagnostics: _ => [new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning)], BoldRequest: true));
        Check(syntheticBold.Result.Status == MuxStatus.Succeeded &&
            syntheticBold.Result.Warnings.Any(warning => warning.Code == SyntheticBoldWarningCode && warning.Occurrences == 1) &&
            syntheticBold.Result.Warnings.Any(warning => warning.Code == ColorWarningCode),
            "完整字形的唯一同家族 Regular face 支持合成粗体并给出独立提示", JsonSerializer.Serialize(syntheticBold.Result.Warnings));
        Check(syntheticBold.Runner.TtxCalls > 0 && syntheticBold.Runner.MuxCalls == 1 &&
            syntheticBold.Runner.ExtractedTrackCount == 1 && File.Exists(syntheticBold.ExpectedOutput),
            "合成粗体提示后仍完成字形、封装、提取 QC 与发布", syntheticBold.Result.Error);

        var syntheticBoldMissingGlyph = await RunCase(new Scenario("synthetic-bold-missing-glyph",
            BoldRequest: true, OmitGlyphA: true,
            Diagnostics: _ => [new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning)]));
        var syntheticGlyphError = syntheticBoldMissingGlyph.Result.Error ?? "";
        Check(syntheticBoldMissingGlyph.Result.Status == MuxStatus.Failed &&
            syntheticGlyphError.Contains("缺字形", StringComparison.Ordinal) &&
            syntheticGlyphError.Contains("U+0041", StringComparison.Ordinal) &&
            !syntheticBoldMissingGlyph.Result.Warnings.Any(warning => warning.Code == SyntheticBoldWarningCode) &&
            !File.Exists(syntheticBoldMissingGlyph.ExpectedOutput),
            "合成粗体候选仍须覆盖全部字形，缺字形不产生放行提示", syntheticBoldMissingGlyph.Result.Error);

        var syntheticBoldAmbiguous = await RunCase(new Scenario("synthetic-bold-ambiguous-regular",
            BoldRequest: true, DuplicateRegularCandidate: true,
            Diagnostics: _ => [new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning)]));
        Check(syntheticBoldAmbiguous.Result.Status == MuxStatus.Failed &&
            syntheticBoldAmbiguous.Result.Error?.Contains("多个同 family 候选", StringComparison.Ordinal) == true &&
            !syntheticBoldAmbiguous.Result.Warnings.Any(warning => warning.Code == SyntheticBoldWarningCode) &&
            syntheticBoldAmbiguous.Runner.MuxCalls == 0 && !File.Exists(syntheticBoldAmbiguous.ExpectedOutput),
            "多个同家族 Regular 候选无法唯一匹配时拒绝发布", syntheticBoldAmbiguous.Result.Error);

        var syntheticBoldItalicMismatch = await RunCase(new Scenario("synthetic-bold-italic-mismatch",
            BoldRequest: true, ItalicRegularCandidate: true,
            Diagnostics: _ => [new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning)]));
        Check(syntheticBoldItalicMismatch.Result.Status == MuxStatus.Failed &&
            syntheticBoldItalicMismatch.Result.Error?.Contains("字体 face 未产出", StringComparison.Ordinal) == true &&
            !syntheticBoldItalicMismatch.Result.Warnings.Any(warning => warning.Code == SyntheticBoldWarningCode) &&
            syntheticBoldItalicMismatch.Runner.MuxCalls == 0 && !File.Exists(syntheticBoldItalicMismatch.ExpectedOutput),
            "斜体状态不匹配的 Regular 候选不能用于合成粗体", syntheticBoldItalicMismatch.Result.Error);

        var reverseBoldMismatch = await RunCase(new Scenario("reverse-bold-mismatch",
            BoldCandidate: true, RegularWeight: 800,
            Diagnostics: _ => [new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning)]));
        Check(reverseBoldMismatch.Result.Status == MuxStatus.Failed &&
            !reverseBoldMismatch.Result.Warnings.Any(warning => warning.Code == SyntheticBoldWarningCode) &&
            reverseBoldMismatch.Runner.MuxCalls == 0 && !File.Exists(reverseBoldMismatch.ExpectedOutput),
            "ASS Bold=false 时不通过相反方向的字体 face 替代继续", reverseBoldMismatch.Result.Error);

        var syntheticBoldWeightMismatch = await RunCase(new Scenario("synthetic-bold-weight-mismatch",
            BoldRequest: true, RegularWeight: 600,
            Diagnostics: _ => [new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning)]));
        Check(syntheticBoldWeightMismatch.Result.Status == MuxStatus.Failed &&
            syntheticBoldWeightMismatch.Result.Error?.Contains("字重信息缺失或不符合", StringComparison.Ordinal) == true &&
            !syntheticBoldWeightMismatch.Result.Warnings.Any(warning => warning.Code == SyntheticBoldWarningCode) &&
            syntheticBoldWeightMismatch.Runner.MuxCalls == 0 && !File.Exists(syntheticBoldWeightMismatch.ExpectedOutput),
            "不满足 libass 合成条件的 Regular 字重仍阻止发布", syntheticBoldWeightMismatch.Result.Error);

        var intrinsicWeight = await RunCase(new Scenario("intrinsic-full-face-bold", IntrinsicFace: true,
            BoldCandidate: true,
            Diagnostics: _ => [new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning)]));
        Check(intrinsicWeight.Result.Status == MuxStatus.Succeeded &&
            intrinsicWeight.Result.Warnings.Any(warning => warning.Code == IntrinsicWeightWarningCode && warning.Occurrences == 1) &&
            intrinsicWeight.Runner.MuxCalls == 1 && intrinsicWeight.Runner.ExtractedTrackCount == 1 &&
            File.Exists(intrinsicWeight.ExpectedOutput),
            "完整 face 名称、AFS 映射与唯一原生粗体字形证明允许 intrinsic weight 并完成全链路 QC",
            intrinsicWeight.Result.Error + " " + JsonSerializer.Serialize(intrinsicWeight.Result.Warnings));
        Check(intrinsicWeight.SourceUnchanged, "intrinsic weight 提示用例保持输入源哈希不变");

        var intrinsicMissingGlyph = await RunCase(new Scenario("intrinsic-bold-missing-glyph",
            IntrinsicFace: true, BoldCandidate: true, OmitGlyphA: true,
            Diagnostics: _ => [new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning)]));
        Check(intrinsicMissingGlyph.Result.Status == MuxStatus.Failed &&
            intrinsicMissingGlyph.Result.Error?.Contains("缺字形", StringComparison.Ordinal) == true &&
            !intrinsicMissingGlyph.Result.Warnings.Any(warning => warning.Code == IntrinsicWeightWarningCode) &&
            !File.Exists(intrinsicMissingGlyph.ExpectedOutput),
            "intrinsic weight 候选缺少任一字形仍失败且不发布", intrinsicMissingGlyph.Result.Error);

        var intrinsicAmbiguous = await RunCase(new Scenario("intrinsic-bold-ambiguous-source",
            IntrinsicFace: true, BoldCandidate: true, DuplicateIntrinsicSource: true,
            Diagnostics: _ => [new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning)]));
        Check(intrinsicAmbiguous.Result.Status == MuxStatus.Failed &&
            !intrinsicAmbiguous.Result.Warnings.Any(warning => warning.Code == IntrinsicWeightWarningCode) &&
            intrinsicAmbiguous.Runner.MuxCalls == 0 && !File.Exists(intrinsicAmbiguous.ExpectedOutput),
            "同 basename/faceIndex 的源字体候选不唯一时阻止 intrinsic weight 放行", intrinsicAmbiguous.Result.Error);

        var intrinsicMappingMismatch = await RunCase(new Scenario("intrinsic-bold-mapping-mismatch",
            IntrinsicFace: true, BoldCandidate: true, MismatchIntrinsicMapping: true,
            Diagnostics: _ => [new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning)]));
        Check(intrinsicMappingMismatch.Result.Status == MuxStatus.Failed &&
            !intrinsicMappingMismatch.Result.Warnings.Any(warning => warning.Code == IntrinsicWeightWarningCode) &&
            intrinsicMappingMismatch.Runner.MuxCalls == 0 && !File.Exists(intrinsicMappingMismatch.ExpectedOutput),
            "AFS subset 映射不匹配原 ASS full face 时阻止放行", intrinsicMappingMismatch.Result.Error);

        var intrinsicGenericRejected = await RunCase(new Scenario("intrinsic-generic-family-rejected",
            IntrinsicFace: true, IntrinsicGenericName: true, BoldCandidate: true,
            Diagnostics: _ => [new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning)]));
        Check(intrinsicGenericRejected.Result.Status == MuxStatus.Failed &&
            !intrinsicGenericRejected.Result.Warnings.Any(warning => warning.Code == IntrinsicWeightWarningCode) &&
            intrinsicGenericRejected.Runner.MuxCalls == 0 && !File.Exists(intrinsicGenericRejected.ExpectedOutput),
            "generic family 名称不能冒充显式 full/PostScript face provenance", intrinsicGenericRejected.Result.Error);

        var intrinsicGenericFallback = await RunCase(new Scenario("intrinsic-id1-generic-fallback",
            IntrinsicFace: true, IntrinsicGenericName: true, IntrinsicOmitNameId16: true, BoldCandidate: true,
            Diagnostics: _ => [new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning)]));
        Check(intrinsicGenericFallback.Result.Status == MuxStatus.Failed &&
            !intrinsicGenericFallback.Result.Warnings.Any(warning => warning.Code == IntrinsicWeightWarningCode) &&
            intrinsicGenericFallback.Runner.MuxCalls == 0 && !File.Exists(intrinsicGenericFallback.ExpectedOutput),
            "nameID 16 缺失时通过 nameID 1 阻止 generic family 冒充完整 face", intrinsicGenericFallback.Result.Error);

        var duplicate = await RunCase(new Scenario("duplicate-color-sc-tc", SubtitleCount: 2,
            Diagnostics: _ => [new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning)]));
        Check(duplicate.Result.Status == MuxStatus.Succeeded && duplicate.Result.Warnings.Count == 1 &&
            duplicate.Result.Warnings[0].Code == ColorWarningCode && duplicate.Result.Warnings[0].Occurrences == 2,
            "SC/TC 相同颜色提示按 code 去重并累计次数", JsonSerializer.Serialize(duplicate.Result.Warnings));

        var ansi = await RunCase(new Scenario("ansi-color", Diagnostics: _ =>
            [new ProcessLine(true, "\u001b[31m" + Timestamp + "|WRN|" + ColorWarning + "\u001b[0m")]));
        Check(ansi.Result.Status == MuxStatus.Succeeded && ansi.Result.Warnings.Count == 1 &&
            ansi.Result.Warnings[0].Code == ColorWarningCode,
            "ANSI 着色不会改变精确颜色告警分类", ansi.Result.Error);

        var split = await RunCase(new Scenario("split-8k-color", Diagnostics: _ => SplitColorLine()));
        Check(split.Result.Status == MuxStatus.Succeeded && split.Result.Warnings.Count == 1 &&
            split.Result.Warnings[0].Code == ColorWarningCode && split.Result.Warnings[0].Occurrences == 1,
            "跨 8 KiB 分块并被 ANSI 序列填充的颜色告警仍可识别", split.Result.Error);

        var returnedOnly = await RunCase(new Scenario("returned-warning-only",
            AfsStandardError: Timestamp + "|WRN|" + ColorWarning));
        Check(returnedOnly.Result.Status == MuxStatus.Succeeded && returnedOnly.Result.Warnings.Count == 1 &&
            returnedOnly.Result.Warnings[0].Code == ColorWarningCode && returnedOnly.Result.Warnings[0].Occurrences == 1,
            "仅从 runner 返回的 stderr 也分类并计数颜色告警", returnedOnly.Result.Error);

        var progressAndReturned = await RunCase(new Scenario("progress-and-returned-warning",
            Diagnostics: _ => [new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning)],
            AfsStandardError: Timestamp + "|WRN|" + ColorWarning));
        Check(progressAndReturned.Result.Status == MuxStatus.Succeeded && progressAndReturned.Result.Warnings.Count == 1 &&
            progressAndReturned.Result.Warnings[0].Occurrences == 1,
            "实时进度与返回 stderr 的同一告警不会重复计数", progressAndReturned.Result.Error);

        var progressAndRepeatedReturned = await RunCase(new Scenario("progress-one-returned-three-warnings",
            Diagnostics: _ => [new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning)],
            AfsStandardError: string.Join(Environment.NewLine, Enumerable.Repeat(Timestamp + "|WRN|" + ColorWarning, 3))));
        Check(progressAndRepeatedReturned.Result.Status == MuxStatus.Succeeded &&
            progressAndRepeatedReturned.Result.Warnings.Count == 1 &&
            progressAndRepeatedReturned.Result.Warnings[0].Occurrences == 3,
            "实时与最终 stderr 都提供时保留最终三次重复告警而不丢计数", JsonSerializer.Serialize(progressAndRepeatedReturned.Result.Warnings));

        var repeatedProgressAndReturned = await RunCase(new Scenario("progress-three-returned-one-warning",
            Diagnostics: _ => Enumerable.Repeat(new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning), 3).ToArray(),
            AfsStandardError: Timestamp + "|WRN|" + ColorWarning));
        Check(repeatedProgressAndReturned.Result.Status == MuxStatus.Succeeded &&
            repeatedProgressAndReturned.Result.Warnings.Count == 1 &&
            repeatedProgressAndReturned.Result.Warnings[0].Occurrences == 3,
            "实时三次与最终一次相同 warning 取较大观测次数而不丢失", JsonSerializer.Serialize(repeatedProgressAndReturned.Result.Warnings));

        var acrossStreams = await RunCase(new Scenario("warning-on-both-streams", Diagnostics: _ =>
        [
            new ProcessLine(false, Timestamp + "|WRN|" + ColorWarning),
            new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning)
        ]));
        Check(acrossStreams.Result.Status == MuxStatus.Succeeded && acrossStreams.Result.Warnings.Count == 1 &&
            acrossStreams.Result.Warnings[0].Occurrences == 2,
            "stdout/stderr 中各一次的告警按总次数相加", JsonSerializer.Serialize(acrossStreams.Result.Warnings));

        var acrossStreamsMixed = await RunCase(new Scenario("warning-counts-across-streams",
            Diagnostics: _ =>
            [
                new ProcessLine(false, Timestamp + "|WRN|" + ColorWarning),
                new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning),
                new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning),
            ],
            AfsStandardOutput: string.Join(Environment.NewLine, Enumerable.Repeat(Timestamp + "|WRN|" + ColorWarning, 2)),
            AfsStandardError: Timestamp + "|WRN|" + ColorWarning));
        Check(acrossStreamsMixed.Result.Status == MuxStatus.Succeeded && acrossStreamsMixed.Result.Warnings.Count == 1 &&
            acrossStreamsMixed.Result.Warnings[0].Occurrences == 4,
            "stdout 1/2 与 stderr 2/1 的实时/最终次数跨流相加为四", JsonSerializer.Serialize(acrossStreamsMixed.Result.Warnings));

        var partialProgressAndReturned = await RunCase(new Scenario("partial-progress-returned-warning",
            Diagnostics: _ => SplitColorLine(), AfsStandardError: Timestamp + "|WRN|" + ColorWarning));
        Check(partialProgressAndReturned.Result.Status == MuxStatus.Succeeded &&
            partialProgressAndReturned.Result.Warnings.Count == 1 &&
            partialProgressAndReturned.Result.Warnings[0].Occurrences == 1,
            "partial 实时告警与最终相同完整行只计一次", JsonSerializer.Serialize(partialProgressAndReturned.Result.Warnings));

        var cancelledPartial = await RunCase(new Scenario("cancel-incomplete-color-warning", CancelAfterPartialWarning: true));
        Check(cancelledPartial.Result.Status == MuxStatus.Cancelled && cancelledPartial.Result.Warnings.Count == 0 &&
            !File.Exists(cancelledPartial.ExpectedOutput),
            "取消时未结束的颜色 warning 行不计数且不发布", JsonSerializer.Serialize(cancelledPartial.Result.Warnings));

        var returnedFatal = await RunCase(new Scenario("returned-fatal-after-progress-warning",
            Diagnostics: _ => [new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning)],
            AfsStandardError: Timestamp + "|ERR|returned-stderr-fatal"));
        Check(returnedFatal.Result.Status == MuxStatus.Failed &&
            returnedFatal.Result.Error?.Contains("returned-stderr-fatal", StringComparison.Ordinal) == true &&
            !File.Exists(returnedFatal.ExpectedOutput),
            "返回 stderr 中的 fatal 不被实时进度 warning 遮蔽", returnedFatal.Result.Error);

        var info = await RunCase(new Scenario("info-debug-path", SourceFileName: "WRN-missing-source.mkv",
            Diagnostics: _ =>
            [
                new ProcessLine(false, Timestamp + "|INF|input path E:\\work\\WRN\\missing-font.ass; literal |WRN|Duplicate fonts and Warning: are path text"),
                new ProcessLine(false, Timestamp + "|DBG|diagnostic text contains WRN and missing-font"),
            ]));
        var infoLog = info.Result.LogPath is { } logPath && File.Exists(logPath) ? await File.ReadAllTextAsync(logPath) : "";
        Check(info.Result.Status == MuxStatus.Succeeded && info.Result.Warnings.Count == 0,
            "INF/DBG 中的 WRN 或 missing 路径文字仅记录日志", info.Result.Error);
        Check(infoLog.Contains("WRN\\missing-font.ass", StringComparison.Ordinal) &&
            infoLog.Contains("diagnostic text contains WRN", StringComparison.Ordinal) &&
            infoLog.Contains("Duplicate fonts", StringComparison.Ordinal),
            "INF/DBG 原文仍保留在任务日志", info.Result.LogPath);

        var unknownStructuredWarn = await RunCase(new Scenario("unknown-structured-warn", Diagnostics: _ =>
            [new ProcessLine(true, Timestamp + "|WARN|unknown long-form level") ]));
        Check(unknownStructuredWarn.Result.Status == MuxStatus.Failed &&
            unknownStructuredWarn.Result.Error?.Contains("WARN", StringComparison.Ordinal) == true &&
            !File.Exists(unknownStructuredWarn.ExpectedOutput),
            "非三字符 WARN 结构化级别 fail-closed", unknownStructuredWarn.Result.Error);

        var unknownStructuredFatal = await RunCase(new Scenario("unknown-structured-fatal", Diagnostics: _ =>
            [new ProcessLine(true, Timestamp + "|FATAL|unknown long-form level") ]));
        Check(unknownStructuredFatal.Result.Status == MuxStatus.Failed &&
            unknownStructuredFatal.Result.Error?.Contains("FATAL", StringComparison.Ordinal) == true &&
            !File.Exists(unknownStructuredFatal.ExpectedOutput),
            "非三字符 FATAL 结构化级别 fail-closed", unknownStructuredFatal.Result.Error);

        var timestampSpaceHeader = await RunCase(new Scenario("timestamp-space-header", Diagnostics: _ =>
            [new ProcessLine(true, "2026-10-09T19:59:20+08:00 |ERR|space before level") ]));
        Check(timestampSpaceHeader.Result.Status == MuxStatus.Failed &&
            timestampSpaceHeader.Result.Error?.Contains("space before level", StringComparison.Ordinal) == true &&
            !File.Exists(timestampSpaceHeader.ExpectedOutput),
            "带空白的时间戳结构化 ERR 仍按致命级别处理", timestampSpaceHeader.Result.Error);

        var leadingSpaceWarning = await RunCase(new Scenario("leading-space-warning-header", Diagnostics: _ =>
            [new ProcessLine(true, "   |WRN|leading space unknown warning") ]));
        Check(leadingSpaceWarning.Result.Status == MuxStatus.Failed &&
            leadingSpaceWarning.Result.Error?.Contains("leading space unknown warning", StringComparison.Ordinal) == true &&
            !File.Exists(leadingSpaceWarning.ExpectedOutput),
            "带前导空白的未知结构化 WRN fail-closed", leadingSpaceWarning.Result.Error);

        var timestampMissingZone = await RunCase(new Scenario("timestamp-without-zone", Diagnostics: _ =>
            [new ProcessLine(true, "2026-10-09T19:59:20|WRN|unknown missing-zone warning") ]));
        Check(timestampMissingZone.Result.Status == MuxStatus.Failed &&
            timestampMissingZone.Result.Error?.Contains("unknown missing-zone warning", StringComparison.Ordinal) == true &&
            !File.Exists(timestampMissingZone.ExpectedOutput),
            "缺少时区的时间戳结构化 WRN fail-closed", timestampMissingZone.Result.Error);

        var plainPathWarningText = await RunCase(new Scenario("plain-path-warning-token", Diagnostics: _ =>
            [new ProcessLine(false, @"E:\work\folder|WRN|filename.ass") ]));
        Check(plainPathWarningText.Result.Status == MuxStatus.Succeeded && plainPathWarningText.Result.Warnings.Count == 0,
            "普通路径包含 |WRN| 文本不被误判成工具告警", plainPathWarningText.Result.Error);

        var unsafeSuffix = await RunCase(new Scenario("color-warning-unsafe-suffix", Diagnostics: _ =>
            [new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning + " Duplicate fonts: extra untrusted text")]));
        Check(unsafeSuffix.Result.Status == MuxStatus.Failed &&
            unsafeSuffix.Result.Error?.Contains("extra untrusted text", StringComparison.Ordinal) == true &&
            !File.Exists(unsafeSuffix.ExpectedOutput),
            "白名单颜色提示追加危险文字后不再放行", unsafeSuffix.Result.Error);

        var multiLineSpoof = await RunCase(new Scenario("multiline-color-warning-spoof", Diagnostics: _ =>
            [new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning + "\r\n" + Timestamp + "|ERR|spoofed second diagnostic")]));
        Check(multiLineSpoof.Result.Status == MuxStatus.Failed &&
            multiLineSpoof.Result.Error?.Contains("多个逻辑行", StringComparison.Ordinal) == true &&
            !File.Exists(multiLineSpoof.ExpectedOutput),
            "单个 ProcessLine 携带 CR/LF 时不能借首行颜色提示绕过诊断", multiLineSpoof.Result.Error);

        var fatalAfterWarning = await RunCase(new Scenario("fatal-preserved", Diagnostics: _ =>
            [
                new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning),
                new ProcessLine(true, Timestamp + "|ERR|fatal-marker from AFS"),
                new ProcessLine(true, Timestamp + "|WRN|ordinary later warning"),
            ]));
        Check(fatalAfterWarning.Result.Status == MuxStatus.Failed &&
            (fatalAfterWarning.Result.Error is { } fatalError && fatalError.Contains("fatal-marker from AFS", StringComparison.Ordinal) &&
                !fatalError.Contains("ordinary later warning", StringComparison.Ordinal)),
            "允许提示后的 ERR 保持首要失败原因，不被后续普通告警覆盖", fatalAfterWarning.Result.Error);
        Check(!File.Exists(fatalAfterWarning.ExpectedOutput), "AFS fatal 诊断不发布成品");

        var unknown = await RunCase(new Scenario("unknown-warning", Diagnostics: _ =>
            [new ProcessLine(true, Timestamp + "|WRN|unknown policy marker") ]));
        Check(unknown.Result.Status == MuxStatus.Failed &&
            unknown.Result.Error?.Contains("unknown policy marker", StringComparison.Ordinal) == true &&
            !File.Exists(unknown.ExpectedOutput),
            "未知 WRN 采取 fail-closed 且不发布成品", unknown.Result.Error);

        var missingFont = await RunCase(new Scenario("missing-font-warning", Diagnostics: _ =>
            [new ProcessLine(true, Timestamp + "|WRN|Not found font file: Mux Synthetic.ttf")]));
        Check(missingFont.Result.Status == MuxStatus.Failed &&
            missingFont.Result.Error?.Contains("Not found font file", StringComparison.OrdinalIgnoreCase) == true &&
            !File.Exists(missingFont.ExpectedOutput),
            "缺字体告警保持致命且不发布成品", missingFont.Result.Error);

        var missingGlyph = await RunCase(new Scenario("missing-glyph", OmitGlyphA: true,
            Diagnostics: _ => [new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning)]));
        var glyphError = missingGlyph.Result.Error ?? "";
        Check(missingGlyph.Result.Status == MuxStatus.Failed &&
            glyphError.Contains("缺字形", StringComparison.Ordinal) && glyphError.Contains("U+0041", StringComparison.Ordinal) &&
            !File.Exists(missingGlyph.ExpectedOutput),
            "允许颜色提示不绕过子集字形检查", glyphError);

        var incomplete = await RunCase(new Scenario("incomplete-afs-output", OmitCorrectedAss: true,
            Diagnostics: _ => [new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning)]));
        Check(incomplete.Result.Status == MuxStatus.Failed &&
            incomplete.Result.Error?.Contains("修正 ASS", StringComparison.Ordinal) == true &&
            !File.Exists(incomplete.ExpectedOutput),
            "允许颜色提示不绕过 AFS 产物完整性检查", incomplete.Result.Error);

        var mismatch = await RunCase(new Scenario("qc-mismatch", MismatchTrackName: true,
            Diagnostics: _ => [new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning)]));
        Check(mismatch.Result.Status == MuxStatus.Failed &&
            mismatch.Result.Error?.Contains("新增字幕轨", StringComparison.Ordinal) == true &&
            !File.Exists(mismatch.ExpectedOutput),
            "允许颜色提示不绕过成品轨道 QC", mismatch.Result.Error);

        var nonzero = await RunCase(new Scenario("nonzero-exit", AfsExitCode: 7,
            Diagnostics: _ => [new ProcessLine(true, Timestamp + "|INF|subsetting started")]));
        Check(nonzero.Result.Status == MuxStatus.Failed &&
            nonzero.Result.Error?.Contains("退出码 7", StringComparison.Ordinal) == true &&
            !File.Exists(nonzero.ExpectedOutput),
            "INF/DBG 不掩盖非零工具退出码", nonzero.Result.Error);

        var report = new
        {
            RunDirectory = runRoot,
            Passed = checks.Count(x => x.Passed),
            Failed = checks.Count(x => !x.Passed),
            Checks = checks
        };
        await File.WriteAllTextAsync(Path.Combine(runRoot, "report.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        Console.WriteLine($"Warning policy: {report.Passed}/{checks.Count} PASS; artifacts: {runRoot}");
        return report.Failed == 0 ? 0 : 1;
    }

    public static async Task<int> RunRealAsync(string? requestLogPath = null, bool expectSyntheticBold = false)
    {
        var workspace = Workspace.Discover();
        var runRoot = Path.Combine(workspace.Root, "desktop", "artifacts", "warning-policy",
            "real-" + Guid.NewGuid().ToString("N"));
        var inputRoot = Path.Combine(runRoot, "input");
        var outputDirectory = Path.Combine(runRoot, "out");
        Directory.CreateDirectory(inputRoot);
        Directory.CreateDirectory(outputDirectory);
        var checks = new List<CheckRecord>();
        var protectedHashesBefore = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var protectedHashesAfter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var originalAssCopiesMatch = new List<bool>();
        string? sourceVideo = null;
        MuxResult? muxResult = null;
        string? copiedTaskLog = null;
        void Check(bool passed, string name, string? detail = null)
        {
            checks.Add(new CheckRecord(name, passed, detail));
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}{(detail is null ? "" : " — " + detail)}");
        }

        try
        {
            var tools = new ToolLocator(workspace).Inspect();
            string? Tool(string name) => tools.FirstOrDefault(tool => tool.Name == name)?.Path;
            var missing = new[] { "ffmpeg", "mkvmerge", "mkvextract", "AssFontSubset" }.Where(name => Tool(name) is null).ToArray();
            if (missing.Length > 0)
            {
                Check(false, "真实 warning fixture 的外部工具可用", "缺少：" + string.Join(", ", missing));
                return await SaveRealReport(2);
            }

            string videoPath;
            string fontsDirectory;
            List<SubtitleInput> subtitles;
            if (requestLogPath is not null)
            {
                var snapshot = ReadRequestSnapshot(requestLogPath);
                videoPath = snapshot.VideoPath;
                fontsDirectory = snapshot.FontsDirectory;
                subtitles = [];
                for (var i = 0; i < snapshot.Subtitles.Count; i++)
                {
                    var original = snapshot.Subtitles[i];
                    protectedHashesBefore[original.Path] = Hash(original.Path);
                    var copy = Path.Combine(inputRoot, $"subtitle-{i + 1}.ass");
                    File.Copy(original.Path, copy, overwrite: false);
                    protectedHashesBefore[copy] = Hash(copy);
                    originalAssCopiesMatch.Add(protectedHashesBefore[copy] == protectedHashesBefore[original.Path]);
                    subtitles.Add(original with { Path = copy });
                }
            }
            else
            {
                videoPath = Path.Combine(inputRoot, "warning-source.mkv");
                fontsDirectory = Path.Combine(runRoot, "fonts-arial");
                Directory.CreateDirectory(fontsDirectory);
                var systemFont = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "arial.ttf");
                if (!File.Exists(systemFont)) throw new FileNotFoundException("缺少用于英文真实 fixture 的 Arial 字体", systemFont);
                File.Copy(systemFont, Path.Combine(fontsDirectory, "arial.ttf"));
                subtitles = [new SubtitleInput(Path.Combine(inputRoot, "warning.ass"), "Warning policy", "en", true, false)];
                await File.WriteAllTextAsync(subtitles[0].Path,
                    AddColorOverride(MakeAss("Arial", bold: expectSyntheticBold)), new UTF8Encoding(false));

                var ffmpeg = Tool("ffmpeg")!;
                var fixtureResult = await new ProcessRunner().RunAsync(new ProcessRequest(ffmpeg,
                    ["-hide_banner", "-y", "-f", "lavfi", "-i", "color=c=0x1c2b3d:s=320x240:r=24:d=3",
                     "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=3", "-c:v", "libx264",
                     "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", videoPath],
                    runRoot, Encoding.UTF8));
                await File.WriteAllTextAsync(Path.Combine(runRoot, "fixture-ffmpeg.log"),
                    fixtureResult.StandardOutput + Environment.NewLine + fixtureResult.StandardError, new UTF8Encoding(false));
                if (fixtureResult.ExitCode != 0) throw new InvalidOperationException("ffmpeg fixture 生成失败，exit=" + fixtureResult.ExitCode);
            }

            sourceVideo = videoPath;
            foreach (var subtitle in subtitles) protectedHashesBefore[subtitle.Path] = Hash(subtitle.Path);
            foreach (var font in Directory.EnumerateFiles(fontsDirectory, "*", SearchOption.AllDirectories))
                protectedHashesBefore[font] = Hash(font);

            Check(File.Exists(videoPath) && (subtitles.Count is 1 or 2) &&
                subtitles.All(item => File.Exists(item.Path)) && Directory.Exists(fontsDirectory),
                "真实 fixture 输入可读且字体目录可用", requestLogPath ?? videoPath);
            if (!File.Exists(videoPath) || (subtitles.Count is < 1 or > 2) || subtitles.Any(item => !File.Exists(item.Path)) ||
                !Directory.Exists(fontsDirectory)) return await SaveRealReport(1);
            if (requestLogPath is not null)
                Check(originalAssCopiesMatch.Count == subtitles.Count && originalAssCopiesMatch.All(match => match),
                    "requestLog 重放的字幕副本与原始 ASS 逐字节一致", string.Join(" | ", originalAssCopiesMatch));
            else if (expectSyntheticBold)
            {
                var fixtureFonts = Directory.EnumerateFiles(fontsDirectory, "*", SearchOption.AllDirectories).ToArray();
                var boldRequest = File.ReadAllText(subtitles[0].Path).Contains(
                    "Style: Default,Arial,32,&H00FFFFFF,&H000000FF,&H00000000,&H64000000,-1,0,1,",
                    StringComparison.Ordinal);
                Check(boldRequest && fixtureFonts.Length == 1 &&
                    Path.GetFileName(fixtureFonts[0]).Equals("arial.ttf", StringComparison.OrdinalIgnoreCase),
                    "合成粗体真实 fixture 明确请求 Bold 且只提供 Arial Regular", subtitles[0].Path);
            }

            var sourceLength = new FileInfo(videoPath).Length;
            var sourceWrite = File.GetLastWriteTimeUtc(videoPath);
            var sourceSubtitleHashes = subtitles.ToDictionary(item => item.Path, item => Hash(item.Path), StringComparer.OrdinalIgnoreCase);
            var outputName = Path.GetFileNameWithoutExtension(videoPath) + ".warning-policy.muxed.mkv";
            muxResult = await new SingleMuxService(workspace).RunAsync(
                new MuxRequest(videoPath, subtitles, fontsDirectory, outputDirectory, outputName));
            var outputExists = muxResult.OutputPath is { } finalPath && File.Exists(finalPath);
            if (muxResult.LogPath is { } taskLog && File.Exists(taskLog))
            {
                copiedTaskLog = Path.Combine(runRoot, "task.log");
                File.Copy(taskLog, copiedTaskLog, overwrite: true);
            }
            foreach (var path in protectedHashesBefore.Keys)
                if (File.Exists(path)) protectedHashesAfter[path] = Hash(path);
            var warningCodes = muxResult.Warnings.Select(warning => warning.Code).Distinct(StringComparer.Ordinal).ToArray();
            var requestReplay = requestLogPath is not null;
            var allowedWarningCodes = new[] { ColorWarningCode }
                .Concat(requestReplay || expectSyntheticBold ? new[] { SyntheticBoldWarningCode } : Array.Empty<string>())
                .Concat(requestReplay ? new[] { IntrinsicWeightWarningCode } : Array.Empty<string>())
                .ToArray();
            var requiredWarningCodes = (requestReplay ? Array.Empty<string>() : new[] { ColorWarningCode })
                .Concat(expectSyntheticBold ? new[] { SyntheticBoldWarningCode } : Array.Empty<string>())
                .ToArray();
            Check(muxResult.Status == MuxStatus.Succeeded && outputExists,
                "真实工具与 Core 提示任务完成并提交成品", muxResult.Error);
            Check(warningCodes.All(code => allowedWarningCodes.Contains(code, StringComparer.Ordinal)) &&
                requiredWarningCodes.All(requiredCode => warningCodes.Contains(requiredCode, StringComparer.Ordinal)) &&
                (requestReplay || muxResult.Warnings.Sum(warning => warning.Occurrences) > 0),
                "真实结果仅包含本用例预期的结构化提示且记录出现次数", JsonSerializer.Serialize(muxResult.Warnings));
            if (expectSyntheticBold)
                Check(muxResult.Warnings.Any(warning => warning.Code == SyntheticBoldWarningCode && warning.Occurrences > 0),
                    "真实 Regular-only 字体 fixture 验证合成粗体提示", JsonSerializer.Serialize(muxResult.Warnings));
            if (requestReplay)
            {
                var subtitleChecks = muxResult.Checks.Count(item => item.StartsWith("字幕 ", StringComparison.Ordinal));
                Check(subtitleChecks == subtitles.Count, "requestLog 中每条字幕分别完成 AFS、字形与嵌入 QC",
                    $"expected={subtitles.Count}; actual={subtitleChecks}");
            }
            Check(muxResult.Checks.Any(item => item.Contains("字体", StringComparison.Ordinal) || item.Contains("font", StringComparison.OrdinalIgnoreCase)) &&
                muxResult.Checks.Any(item => item.Contains("ASS", StringComparison.Ordinal)),
                "真实任务仍完成字体/ASS 及成品 QC", string.Join(" | ", muxResult.Checks));
            Check(new FileInfo(videoPath).Length == sourceLength && File.GetLastWriteTimeUtc(videoPath) == sourceWrite &&
                protectedHashesBefore.All(pair => protectedHashesAfter.TryGetValue(pair.Key, out var afterHash) && pair.Value == afterHash),
                "真实 source video 元数据及原始/副本 ASS、字体哈希保持不变", videoPath);
            Check(copiedTaskLog is { Length: > 0 } && File.ReadAllText(copiedTaskLog).Contains("ASS color tag", StringComparison.Ordinal),
                "保留包含 AFS 原始颜色告警的 Core 任务日志", copiedTaskLog ?? muxResult.LogPath);
            Console.WriteLine("真实 warning artifacts: " + runRoot);
            return await SaveRealReport(checks.Any(check => !check.Passed) ? 1 : 0);
        }
        catch (Exception ex)
        {
            Check(false, "真实 warning fixture 执行", ex.ToString());
            Console.WriteLine("真实 warning artifacts: " + runRoot);
            return await SaveRealReport(1);
        }

        async Task<int> SaveRealReport(int exitCode)
        {
            var report = new
            {
                RunDirectory = runRoot,
                RequestLog = requestLogPath,
                SyntheticBoldExpected = expectSyntheticBold,
                IntrinsicWeightAllowed = requestLogPath is not null,
                SourceVideo = sourceVideo,
                OutputPath = muxResult?.OutputPath,
                Status = muxResult?.Status.ToString(),
                Error = muxResult?.Error,
                Warnings = muxResult?.Warnings,
                CoreTaskLog = muxResult?.LogPath,
                TaskLogCopy = copiedTaskLog,
                ProtectedHashesBefore = protectedHashesBefore,
                ProtectedHashesAfter = protectedHashesAfter,
                Passed = checks.Count(check => check.Passed),
                Failed = checks.Count(check => !check.Passed),
                Checks = checks
            };
            await File.WriteAllTextAsync(Path.Combine(runRoot, "report.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            return exitCode;
        }
    }

    public static void VerifyUiWarningPresentation(string artifactRoot, Action<bool, string> check, Action<SingleMuxViewModel>? verifyView = null)
    {
        var fixture = Path.Combine(artifactRoot, "warning-ui-fixture");
        var workspaceRoot = Path.Combine(fixture, "workspace");
        var input = Path.Combine(fixture, "input");
        var fonts = Path.Combine(fixture, "fonts");
        var output = Path.Combine(fixture, "output");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(fonts);
        Directory.CreateDirectory(output);
        PrepareFakeTools(workspaceRoot);
        var video = Path.Combine(input, "warning-ui.mkv");
        var subtitle = Path.Combine(input, "warning-ui.ass");
        var secondarySubtitle = Path.Combine(input, "warning-ui-tc.ass");
        const string secondaryFontName = "Mux Synthetic Two";
        File.WriteAllText(video, "warning ui source", new UTF8Encoding(false));
        File.WriteAllText(subtitle, MakeAss(bold: true), new UTF8Encoding(false));
        File.WriteAllText(secondarySubtitle, MakeAss(secondaryFontName, bold: true), new UTF8Encoding(false));
        var subtitles = new[]
        {
            new SubtitleInput(subtitle, "SC", "zh-Hans", true, false),
            new SubtitleInput(secondarySubtitle, "TC", "zh-Hant", false, true)
        };
        var scenario = new Scenario("warning-ui", SubtitleCount: 2,
            Diagnostics: index => index == 0 ? [new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning)] : [],
            BoldRequest: true, SecondFontName: secondaryFontName);
        var runner = new WarningPolicyRunner(video, subtitles, scenario, fonts);
        var model = new SingleMuxViewModel(new SingleMuxService(new Workspace(workspaceRoot), runner))
        {
            FontsDirectory = fonts,
            OutputDirectory = output,
            ScSubtitle = subtitle,
            TcSubtitle = secondarySubtitle,
            VideoPath = video
        };
        model.TcTrack.IsForced = true;

        Pump(model.ProbeVideoAsync(), TimeSpan.FromSeconds(10));
        Pump(model.StartAsync(), TimeSpan.FromSeconds(20));
        var result = model.Result;
        var resultChecks = result?.Checks ?? Array.Empty<string>();
        var warnings = result?.Warnings ?? Array.Empty<MuxWarning>();
        var warningHeading = model.ResultSummary.IndexOf("提示（2类，累计3次）", StringComparison.Ordinal);
        var checksHeading = model.ResultSummary.IndexOf("校验结果：", StringComparison.Ordinal);
        check(result is { Status: MuxStatus.Succeeded } && model.StatusText == "成功（2类提示/累计3次）" &&
            warnings.Count == 3 && warnings.Select(warning => warning.Code).Distinct(StringComparer.Ordinal).Count() == 2 &&
            warnings.Sum(warning => (long)warning.Occurrences) == 3 &&
            warnings.Count(warning => warning.Code == SyntheticBoldWarningCode) == 2 &&
            warnings.Select(warning => warning.Message).Distinct(StringComparer.Ordinal).Count() == 3,
            "相同 Core warning code 的不同字体消息仍计为同一类别并各自保留次数");
        check(warningHeading >= 0 && checksHeading > warningHeading && resultChecks.Count > 0 &&
            !resultChecks.Any(item => item.Contains("AFS 颜色标签", StringComparison.Ordinal)) &&
            warnings.All(warning => model.ResultSummary.Contains(warning.Message, StringComparison.Ordinal)),
            "UI 保留三条提示 message，并将 AFS/Core warning 与成品 QC Checks 分区展示");
        verifyView?.Invoke(model);
    }

    private static IReadOnlyList<ProcessLine> SplitColorLine()
    {
        var header = Timestamp + "|WRN|";
        var remaining = 8192 - header.Length;
        var threeCharEscapes = 0;
        while (threeCharEscapes <= 3 && (remaining - 3 * threeCharEscapes) % 4 != 0) threeCharEscapes++;
        if (threeCharEscapes > 3) throw new InvalidOperationException("无法将 ANSI 填充对齐到 8 KiB 边界。");
        var fourCharEscapes = (remaining - 3 * threeCharEscapes) / 4;
        var padding = string.Concat(Enumerable.Repeat("\u001b[m", threeCharEscapes)) +
            string.Concat(Enumerable.Repeat("\u001b[0m", fourCharEscapes));
        var line = header + padding + ColorWarning;
        if (line.Length <= 8192) throw new InvalidOperationException("分块样例没有越过 8 KiB 边界。");
        return [new ProcessLine(true, line[..8192], IsPartial: true), new ProcessLine(true, line[8192..])];
    }

    private static void Pump(Task task, TimeSpan timeout)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted && timer.Elapsed < timeout)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        if (!task.IsCompleted) throw new TimeoutException("warning policy UI fixture 未在时限内完成。");
        task.GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
    }

    private static void PrepareFakeTools(string workspaceRoot)
    {
        var bin = Path.Combine(workspaceRoot, "bin");
        foreach (var relative in new[]
        {
            "mkvtoolnix/mkvmerge.exe", "mkvtoolnix/mkvextract.exe", "mkvtoolnix/mkvpropedit.exe",
            "ffmpeg/bin/ffmpeg.exe", "assfontsubset/AssFontSubset.Console.exe", "assfonts/assfonts.exe",
        }) WritePlaceholder(Path.Combine(bin, relative));

        var extension = OperatingSystem.IsWindows() ? ".exe" : "";
        WritePlaceholder(Path.Combine(bin, "python", "Scripts", "pyftsubset" + extension));
        WritePlaceholder(Path.Combine(bin, "python", "Scripts", "ttx" + extension));
    }

    private static void WritePlaceholder(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "intercepted by warning-policy fake runner");
    }

    private static string AddSecondDialogue(string ass) => ass +
        "\nDialogue: 0,0:00:04.00,0:00:05.00,Default,,0,0,0,,A";

    private static string AddHeaderMapping(string ass, string mapping)
    {
        var firstLineEnd = ass.IndexOf('\n');
        return firstLineEnd < 0
            ? ass + "\n" + mapping
            : ass.Insert(firstLineEnd + 1, mapping + "\n");
    }

    private static string AddCommentsBetweenDialogueRecords(string ass)
    {
        var lines = ass.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var dialogueLines = lines.Select((line, index) => (line, index))
            .Where(pair => pair.line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.index).ToArray();
        var comments = new List<string>
        {
            "Comment: 0,0:00:00.50,0:00:00.75,Default,,0,0,0,,meta-1",
            "Comment: 0,0:00:00.75,0:00:01.00,Default,,0,0,0,,meta-2",
        };
        if (dialogueLines.Length >= 2) lines.InsertRange(dialogueLines[1], comments);
        else if (dialogueLines.Length == 1) lines.AddRange(comments);
        return string.Join("\n", lines);
    }

    private static string RelocateComments(string ass)
    {
        var lines = ass.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var eventsIndex = lines.FindIndex(line => line.Equals("[Events]", StringComparison.OrdinalIgnoreCase));
        if (eventsIndex < 0) return string.Join("\n", lines);
        var formatIndex = lines.FindIndex(eventsIndex + 1, line => line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase));
        if (formatIndex < 0) return string.Join("\n", lines);
        var commentLines = new List<string>();
        for (var index = lines.Count - 1; index > formatIndex; index--)
        {
            if (!lines[index].StartsWith("Comment:", StringComparison.OrdinalIgnoreCase)) continue;
            commentLines.Insert(0, lines[index]);
            lines.RemoveAt(index);
        }
        lines.InsertRange(formatIndex + 1, commentLines);
        return string.Join("\n", lines);
    }

    private static string MutateExtractedAss(string ass, AssMutation mutation)
    {
        var lines = ass.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var firstDialogue = lines.FindIndex(line => line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase));
        switch (mutation)
        {
            case AssMutation.DialogueText:
                if (firstDialogue >= 0) lines[firstDialogue] = ReplaceDialogueText(lines[firstDialogue], "B");
                break;
            case AssMutation.DialogueStartTime:
                if (firstDialogue >= 0) lines[firstDialogue] = lines[firstDialogue]
                    .Replace("0:00:01.00", "0:00:01.10", StringComparison.Ordinal);
                break;
            case AssMutation.StyleName:
                var styleLine = lines.FindIndex(line => line.StartsWith("Style: Default,", StringComparison.Ordinal));
                if (styleLine >= 0) lines[styleLine] = lines[styleLine]
                    .Replace("Style: Default,", "Style: Alternate,", StringComparison.Ordinal);
                break;
            case AssMutation.FontName:
                var fontLine = lines.FindIndex(line => line.StartsWith("Style: Default,Mux Synthetic,", StringComparison.Ordinal));
                if (fontLine >= 0) lines[fontLine] = lines[fontLine]
                    .Replace("Style: Default,Mux Synthetic,", "Style: Default,Alternate Font,", StringComparison.Ordinal);
                break;
            case AssMutation.DialogueOrder:
                var dialogueLines = lines.Select((line, index) => (line, index))
                    .Where(pair => pair.line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
                    .Select(pair => pair.index).ToArray();
                if (dialogueLines.Length >= 2)
                    (lines[dialogueLines[0]], lines[dialogueLines[1]]) = (lines[dialogueLines[1]], lines[dialogueLines[0]]);
                break;
            case AssMutation.DialogueTextSpaceAdded:
                if (firstDialogue >= 0 && lines[firstDialogue].EndsWith(",,A", StringComparison.Ordinal))
                    lines[firstDialogue] += " ";
                break;
            case AssMutation.DialogueTextSpaceRemoved:
                if (firstDialogue >= 0 && lines[firstDialogue].EndsWith(",,A ", StringComparison.Ordinal))
                    lines[firstDialogue] = lines[firstDialogue][..^1];
                break;
        }
        var mutated = string.Join("\n", lines);
        if (mutated.Equals(ass, StringComparison.Ordinal))
            throw new InvalidOperationException($"ASS 破坏 fixture {mutation} 没有改变任何字节。");
        return mutated;
    }

    private static string ReplaceDialogueText(string line, string replacement)
    {
        var textField = line.LastIndexOf(',');
        return textField < 0 ? line : line[..(textField + 1)] + replacement;
    }

    private static string SetFirstDialogueText(string ass, string replacement)
    {
        var lines = ass.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var index = lines.FindIndex(line => line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new InvalidDataException("ASS space fixture 缺少 Dialogue 行。");
        lines[index] = ReplaceDialogueText(lines[index], replacement);
        return string.Join("\n", lines);
    }

    private static string MakeAss(string fontName = "Mux Synthetic", bool bold = false) => $"""
        [Script Info]
        ScriptType: v4.00+
        WrapStyle: 2

        [V4+ Styles]
        Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
        Style: Default,{fontName},32,&H00FFFFFF,&H000000FF,&H00000000,&H64000000,{(bold ? -1 : 0)},0,1,2,0,2,20,20,20,1

        [Events]
        Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
        Dialogue: 0,0:00:01.00,0:00:03.00,Default,,0,0,0,,A
        """;

    private static string AddColorOverride(string ass)
    {
        var lines = ass.Replace("\r", "").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase)) continue;
            var separator = lines[i].IndexOf(':') + 1;
            for (var comma = 0; comma < 9; comma++)
            {
                separator = lines[i].IndexOf(',', separator);
                if (separator < 0) throw new InvalidDataException("ASS Dialogue 行少于 10 个字段。");
                separator++;
            }
            lines[i] = lines[i][..separator] + "{\\3c&H00FFFFFF&}" + lines[i][separator..];
            return string.Join("\n", lines);
        }
        throw new InvalidDataException("ASS 中没有可注入颜色提示的 Dialogue 行。");
    }

    private static RequestSnapshot ReadRequestSnapshot(string requestLogPath)
    {
        var log = Path.GetFullPath(requestLogPath);
        if (!File.Exists(log)) throw new FileNotFoundException("Core task.log 不存在", log);
        var requestLine = File.ReadLines(log).FirstOrDefault(line => line.Contains("请求 ", StringComparison.Ordinal))
            ?? throw new InvalidDataException("task.log 未包含请求快照行。");
        var marker = requestLine.IndexOf("请求 ", StringComparison.Ordinal) + "请求 ".Length;
        using var document = JsonDocument.Parse(requestLine[marker..]);
        var root = document.RootElement;
        var videoPath = Path.GetFullPath(root.GetProperty("VideoPath").GetString() ?? "");
        var fontsDirectory = Path.GetFullPath(root.GetProperty("FontsDirectory").GetString() ?? "");
        var subtitles = root.GetProperty("Subtitles").EnumerateArray().Select(item => new SubtitleInput(
            Path.GetFullPath(item.GetProperty("Path").GetString() ?? ""),
            item.GetProperty("Name").GetString() ?? "",
            item.GetProperty("Language").GetString() ?? "",
            item.GetProperty("IsDefault").GetBoolean(),
            item.GetProperty("IsForced").GetBoolean())).ToArray();
        if (!File.Exists(videoPath) || !Directory.Exists(fontsDirectory) || subtitles.Any(item => !File.Exists(item.Path)))
            throw new FileNotFoundException("请求快照中的视频、字幕或字体目录不再可用。");
        return new RequestSnapshot(videoPath, fontsDirectory, subtitles);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed record Scenario(string Name, int SubtitleCount = 1,
        Func<int, IReadOnlyList<ProcessLine>>? Diagnostics = null, int AfsExitCode = 0,
        bool OmitCorrectedAss = false, bool OmitFont = false, bool OmitGlyphA = false,
        bool MismatchTrackName = false, string SourceFileName = "source.mkv",
        string AfsStandardOutput = "", string AfsStandardError = "", bool BoldRequest = false,
        bool DuplicateRegularCandidate = false, int RegularWeight = 400, bool ItalicRegularCandidate = false,
        bool BoldCandidate = false, string? SecondFontName = null, bool CancelAfterPartialWarning = false,
        bool IntrinsicFace = false, bool IntrinsicGenericName = false,
        bool DuplicateIntrinsicSource = false, bool MismatchIntrinsicMapping = false,
        bool IntrinsicOmitNameId16 = false, bool RelocateCommentsDuringTransport = false,
        AssMutation Mutation = AssMutation.None, bool OmitReferenceMks = false);

    private enum AssMutation
    {
        None,
        DialogueText,
        DialogueStartTime,
        StyleName,
        FontName,
        DialogueOrder,
        DialogueTextSpaceAdded,
        DialogueTextSpaceRemoved
    }

    private sealed record CheckRecord(string Name, bool Passed, string? Detail);
    private sealed record RequestSnapshot(string VideoPath, string FontsDirectory, IReadOnlyList<SubtitleInput> Subtitles);
    private sealed record Observation(MuxResult Result, WarningPolicyRunner Runner, string ExpectedOutput,
        bool SourceUnchanged);

    private sealed class WarningPolicyRunner(string sourcePath, IReadOnlyList<SubtitleInput> subtitles,
        Scenario scenario, string fontsDirectory, CancellationTokenSource? cancellationSource = null) : IMuxToolRunner
    {
        private readonly List<string> correctedAssPaths = [];
        private readonly List<(string Name, string Path)> attachmentInputs = [];
        private readonly Dictionary<string, string> referenceInputs = new(StringComparer.OrdinalIgnoreCase);
        private string? outputPath;

        public int AfsCalls { get; private set; }
        public int TtxCalls { get; private set; }
        public int MuxCalls { get; private set; }
        public int ReferenceMuxCalls { get; private set; }
        public int ReferenceExtractCalls { get; private set; }
        public int ExtractedTrackCount { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessLine>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var args = request.Arguments;
            if (args.Count == 1 && args[0] == "--help" || args.Count == 1 && args[0] == "--version")
                return Task.FromResult(new ProcessResult(0, "", ""));
            if (args.Count > 0 && args[0] == "-J")
            {
                var path = Path.GetFullPath(args[1]);
                var json = path.Equals(Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase)
                    ? SourceProbeJson()
                    : OutputProbeJson(path);
                return Task.FromResult(new ProcessResult(0, json, ""));
            }
            if (args.Count > 0 && args[0] == "-q")
            {
                TtxCalls++;
                var output = args[Array.IndexOf(args.ToArray(), "-o") + 1];
                var fontInput = args[^1];
                if (scenario.IntrinsicFace && IsWithin(fontsDirectory, fontInput))
                {
                    File.WriteAllText(output, IntrinsicSourceFontXml(scenario.IntrinsicOmitNameId16), new UTF8Encoding(false));
                    return Task.FromResult(new ProcessResult(0, "", ""));
                }
                if (scenario.IntrinsicFace && Path.GetFileName(fontInput).StartsWith("mux-intrinsic.", StringComparison.OrdinalIgnoreCase))
                {
                    File.WriteAllText(output, IntrinsicSubsetFontXml(scenario.OmitGlyphA), new UTF8Encoding(false));
                    return Task.FromResult(new ProcessResult(0, "", ""));
                }
                var familyName = Path.GetFileName(fontInput).Equals("secondary-synthetic.ttf", StringComparison.OrdinalIgnoreCase)
                    ? scenario.SecondFontName ?? "Mux Synthetic" : "Mux Synthetic";
                File.WriteAllText(output, FontXml(scenario.OmitGlyphA, scenario.RegularWeight,
                    scenario.ItalicRegularCandidate, scenario.BoldCandidate, familyName), new UTF8Encoding(false));
                return Task.FromResult(new ProcessResult(0, "", ""));
            }
            if (args.Contains("--output", StringComparer.Ordinal))
                return RunAfs(args, progress, cancellationToken);
            if (args.Count > 0 && args[0] == "-o")
            {
                if (Path.GetFileName(args[1]).StartsWith("ass-reference-", StringComparison.Ordinal))
                {
                    ReferenceMuxCalls++;
                    referenceInputs[Path.GetFullPath(args[1])] = Path.GetFullPath(args[^1]);
                    if (!scenario.OmitReferenceMks) File.WriteAllBytes(args[1], [0x4D, 0x4B, 0x53]);
                    return Task.FromResult(new ProcessResult(0, "", ""));
                }
                MuxCalls++;
                outputPath = args[1];
                File.WriteAllBytes(outputPath, [0x4D, 0x4B, 0x56]);
                attachmentInputs.Clear();
                string? nextName = null;
                for (var i = 0; i < args.Count; i++)
                {
                    if (args[i] == "--attachment-name" && i + 1 < args.Count) nextName = args[++i];
                    else if (args[i] == "--attach-file" && i + 1 < args.Count)
                    {
                        var path = args[++i];
                        attachmentInputs.Add((nextName ?? Path.GetFileName(path), path));
                        nextName = null;
                    }
                }
                return Task.FromResult(new ProcessResult(0, "", ""));
            }
            if (args.Count > 1 && args[1] == "tracks")
            {
                var isReference = Path.GetFileName(args[0]).StartsWith("ass-reference-", StringComparison.Ordinal);
                if (isReference) ReferenceExtractCalls++;
                var subtitleIndex = 0;
                foreach (var item in args.Skip(2))
                {
                    var path = ExtractPath(item);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var source = isReference ? referenceInputs[Path.GetFullPath(args[0])] : correctedAssPaths[subtitleIndex];
                    var text = File.ReadAllText(source, Encoding.UTF8);
                    if (scenario.RelocateCommentsDuringTransport) text = RelocateComments(text);
                    if (!isReference && scenario.Mutation != AssMutation.None)
                        text = MutateExtractedAss(text, scenario.Mutation);
                    File.WriteAllText(path, text, new UTF8Encoding(false));
                    subtitleIndex++;
                }
                if (!isReference) ExtractedTrackCount = subtitleIndex;
                return Task.FromResult(new ProcessResult(0, "", ""));
            }
            if (args.Count > 1 && args[1] == "attachments")
            {
                var attachmentIndex = 0;
                foreach (var item in args.Skip(2))
                {
                    var path = ExtractPath(item);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.Copy(attachmentInputs[attachmentIndex++].Path, path, overwrite: true);
                }
                return Task.FromResult(new ProcessResult(0, "", ""));
            }
            return Task.FromResult(new ProcessResult(0, "", ""));
        }

        private Task<ProcessResult> RunAfs(IReadOnlyList<string> args, IProgress<ProcessLine>? progress,
            CancellationToken cancellationToken)
        {
            var index = AfsCalls++;
            var input = args[0];
            var output = args[Array.IndexOf(args.ToArray(), "--output") + 1];
            if (scenario.CancelAfterPartialWarning)
            {
                progress?.Report(new ProcessLine(true, Timestamp + "|WRN|" + ColorWarning[..^1], IsPartial: true));
                cancellationSource?.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException("取消 fixture 未取消请求 token。");
            }
            Directory.CreateDirectory(output);
            var corrected = Path.Combine(output, Path.GetFileName(input));
            if (!scenario.OmitCorrectedAss)
            {
                var originalText = File.ReadAllText(input, Encoding.UTF8);
                if (scenario.Mutation == AssMutation.DialogueOrder ||
                    (scenario.RelocateCommentsDuringTransport && scenario.Mutation is not
                        (AssMutation.DialogueTextSpaceAdded or AssMutation.DialogueTextSpaceRemoved)))
                    originalText = AddSecondDialogue(originalText);
                originalText = originalText.TrimEnd('\r', '\n');
                if (scenario.Mutation == AssMutation.DialogueTextSpaceRemoved)
                    originalText = SetFirstDialogueText(originalText, "A ");
                string text;
                if (scenario.IntrinsicFace)
                {
                    var sourceName = scenario.IntrinsicGenericName ? "Mux Intrinsic" : "Mux Intrinsic Heavy";
                    text = originalText.Replace(sourceName, "FACE0001", StringComparison.Ordinal);
                    var mappedName = scenario.MismatchIntrinsicMapping ? "Unrelated Face Name" : sourceName;
                    text = AddHeaderMapping(text, $"; Font Subset: FACE0001 - {mappedName}");
                }
                else
                    text = AddHeaderMapping(originalText, "; Font Subset: Mux Synthetic");
                if (scenario.RelocateCommentsDuringTransport)
                    text = AddCommentsBetweenDialogueRecords(text);
                File.WriteAllText(corrected, text, new UTF8Encoding(false));
                correctedAssPaths.Add(corrected);
            }
            if (!scenario.OmitFont)
            {
                var useSecondaryFont = index == 1 && scenario.SecondFontName is not null;
                var font = Path.Combine(output, scenario.IntrinsicFace
                    ? "mux-intrinsic.0.FACE0001.ttf"
                    : useSecondaryFont ? "secondary-synthetic.ttf" : "mux-synthetic.ttf");
                File.WriteAllBytes(font, [0, 1, 2, 3, 4, 5, 6, 7]);
                if (scenario.DuplicateRegularCandidate)
                    File.WriteAllBytes(Path.Combine(output, "mux-synthetic-second.ttf"), [7, 6, 5, 4, 3, 2, 1, 0]);
            }
            foreach (var line in scenario.Diagnostics?.Invoke(index) ?? []) progress?.Report(line);
            return Task.FromResult(new ProcessResult(scenario.AfsExitCode,
                scenario.AfsStandardOutput, scenario.AfsStandardError));
        }

        private string OutputProbeJson(string path)
        {
            if (outputPath is null || !path.Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected output probe target: " + path);
            var tracks = BaseTracks();
            for (var i = 0; i < subtitles.Count; i++)
            {
                var subtitle = subtitles[i];
                tracks.Add(new
                {
                    id = i + 2,
                    type = "subtitles",
                    codec = "SubStationAlpha",
                    properties = new
                    {
                        codec_id = "S_TEXT/ASS",
                        language = "zho",
                        language_ietf = subtitle.Language,
                        track_name = scenario.MismatchTrackName ? subtitle.Name + " mismatch" : subtitle.Name,
                        default_track = subtitle.IsDefault,
                        forced_track = subtitle.IsForced
                    }
                });
            }
            var attachments = attachmentInputs.Select((item, index) => new
            {
                id = index + 10,
                file_name = item.Name,
                content_type = "font/ttf",
                size = new FileInfo(item.Path).Length
            }).ToArray();
            return JsonSerializer.Serialize(new { tracks, attachments });
        }

        private static string SourceProbeJson()
        {
            var tracks = BaseTracks();
            tracks.Add(new
            {
                id = 2,
                type = "subtitles",
                codec = "SubRip/SRT",
                properties = new
                {
                    codec_id = "S_TEXT/UTF8",
                    language = "eng",
                    language_ietf = "en-US",
                    track_name = "Legacy subtitle",
                    default_track = false,
                    forced_track = false
                }
            });
            var attachments = new[]
            {
                new { id = 9, file_name = "LegacyFont.ttf", content_type = "application/x-truetype-font", size = 37L }
            };
            return JsonSerializer.Serialize(new { tracks, attachments });
        }

        private static List<object> BaseTracks() =>
        [
            new
            {
                id = 0,
                type = "video",
                codec = "AVC/H.264/MPEG-4p10",
                properties = new
                {
                    codec_id = "V_MPEG4/ISO/AVC",
                    language = "und",
                    language_ietf = "und",
                    track_name = "",
                    default_track = true,
                    forced_track = false,
                    pixel_dimensions = "1280x720"
                }
            },
            new
            {
                id = 1,
                type = "audio",
                codec = "AAC",
                properties = new
                {
                    codec_id = "A_AAC-2",
                    language = "eng",
                    language_ietf = "en-US",
                    track_name = "",
                    default_track = true,
                    forced_track = false,
                    audio_channels = 2,
                    audio_sampling_frequency = 48000
                }
            }
        ];

        private static string FontXml(bool omitGlyph, int weight, bool italic, bool bold, string familyName)
        {
            var postscriptName = familyName.Replace(" ", "", StringComparison.Ordinal);
            return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <ttFont>
                  <cmap>
                    <cmap_format_4 platformID="3" platEncID="1">
                      <map code="0x0020" name="space"/>
                      {(!omitGlyph ? "<map code=\"0x0041\" name=\"A\"/>" : "")}
                    </cmap_format_4>
                  </cmap>
                  <name>
                    <namerecord nameID="1">{familyName}</namerecord>
                    <namerecord nameID="4">{familyName}</namerecord>
                    <namerecord nameID="6">{postscriptName}</namerecord>
                  </name>
                  <OS_2><fsSelection value="{Convert.ToString((italic ? 1 : 0) | (bold ? 32 : 0), 2).PadLeft(10, '0')}"/><usWeightClass value="{weight}"/></OS_2>
                </ttFont>
                """;
        }

        private static string IntrinsicSourceFontXml(bool omitNameId16) => $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <ttFont>
              <cmap><cmap_format_4 platformID="3" platEncID="1"><map code="0x0041" name="A"/></cmap_format_4></cmap>
              <name>
                <namerecord nameID="1">Mux Intrinsic</namerecord>
                <namerecord nameID="4">Mux Intrinsic Heavy</namerecord>
                <namerecord nameID="6">MuxIntrinsic-Heavy</namerecord>
                {(omitNameId16 ? "" : "<namerecord nameID=\"16\">Mux Intrinsic</namerecord>")}
              </name>
              <OS_2><fsSelection value="0000100000"/><usWeightClass value="800"/></OS_2>
            </ttFont>
            """;

        private static string IntrinsicSubsetFontXml(bool omitGlyph) => $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <ttFont>
              <cmap><cmap_format_4 platformID="3" platEncID="1">{(!omitGlyph ? "<map code=\"0x0041\" name=\"A\"/>" : "")}</cmap_format_4></cmap>
              <name>
                <namerecord nameID="1">FACE0001</namerecord>
                <namerecord nameID="4">FACE0001</namerecord>
                <namerecord nameID="6">FACE0001</namerecord>
                <namerecord nameID="16">Mux Intrinsic</namerecord>
              </name>
              <OS_2><fsSelection value="0000100000"/><usWeightClass value="800"/></OS_2>
            </ttFont>
            """;

        private static bool IsWithin(string directory, string path)
        {
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(path);
            return resolved.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        private static string ExtractPath(string idAndPath)
        {
            var separator = idAndPath.IndexOf(':');
            return separator < 0 ? idAndPath : idAndPath[(separator + 1)..];
        }
    }
}
