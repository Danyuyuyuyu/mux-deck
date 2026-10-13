using MuxDeck.Application.Ports;
using MuxDeck.Contracts;
using MuxDeck.Domain;
using MuxDeck.Infrastructure.Components;

namespace MuxDeck.Infrastructure.Muxing;

public sealed class AfsSubsetter(IMuxToolRunner? runner = null, IToolCatalog? catalog = null,
    int subtitleIndex = 0, List<Diagnostic>? diagnostics = null,
    Dictionary<(string Path, int Index), string>? metadataCache = null) : IFontSubsetter
{
    public Task<FontSubsetResult> SubsetAsync(FontSubsetRequest request, ToolCallContext context,
        IProgress<JobProgress>? progress = null, CancellationToken cancellationToken = default) => PortBoundary.Guard(async () =>
    {
        var work = context.Workspace.WorkDirectory;
        var i = subtitleIndex;
        var warnings = diagnostics ?? [];
        var sourceMetadataCache = metadataCache ?? new();
        var tools = catalog ?? new ToolCatalog();
        var afs = await ToolCatalog.Require(tools, ToolCapability.SubsetFonts, context.Tools, cancellationToken).ConfigureAwait(false);
        var backend = await ToolCatalog.Require(tools, ToolCapability.FontSubsetBackend, context.Tools, cancellationToken).ConfigureAwait(false);
        var fontTools = Path.GetDirectoryName(backend)!;
        var inspector = new TtxFontInspector(runner, tools);
        async Task<ProcessResult> RunToolAsync(string executable, IReadOnlyList<string> args, string work,
            ITaskWorkspace log, CancellationToken token, TimeSpan timeout, bool rejectDiagnostics = false, List<Diagnostic>? warnings = null) =>
            await new ToolExecutor(runner).RunAsync(executable, args, context, token, timeout, rejectDiagnostics: rejectDiagnostics, warnings: warnings).ConfigureAwait(false);
        var log = context.Workspace;
        var input = Path.Combine(work, $"input-{i}", Path.GetFileName(request.Subtitle.Path));
        Directory.CreateDirectory(Path.GetDirectoryName(input)!);
        File.Copy(request.Subtitle.Path, input);
        var originalUsage = AssGlyphValidator.ReadUsage(AssGlyphValidator.ReadAss(input));
        var output = Path.Combine(work, $"afs-{i}");
        await RunToolAsync(afs,
            [input, "--fonts", request.Fonts.Directory, "--output", output,
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
            await inspector.ExportAsync(Path.Combine(fontTools, Executable("ttx")), font, xml, context,
                cancellationToken, includeCmap: true).ConfigureAwait(false);
            xmlFiles.Add(xml);
        }
        var usage = AssGlyphValidator.ReadUsage(text);
        var intrinsicProofs = new Dictionary<string, AssGlyphValidator.IntrinsicFaceProof>();
        foreach (var needed in AssGlyphValidator.GetIntrinsicFaceRequests(usage, xmlFiles, text, originalUsage))
        {
            var sourceFont = LocateSourceFont(request.Fonts.Directory, needed.SourceFileName);
            var collection = Path.GetExtension(sourceFont).ToLowerInvariant() is ".ttc" or ".otc";
            if (!collection && needed.FaceIndex != 0)
                throw new InvalidDataException("独立源字体的 faceIndex 必须为 0。");
            var cacheKey = (sourceFont, needed.FaceIndex);
            if (!sourceMetadataCache.TryGetValue(cacheKey, out var metadataXml))
            {
                metadataXml = Path.Combine(work, $"source-names-{sourceMetadataCache.Count}.xml");
                await inspector.ExportAsync(Path.Combine(fontTools, Executable("ttx")), sourceFont, metadataXml, context,
                    cancellationToken, includeCmap: false, collection ? needed.FaceIndex : null).ConfigureAwait(false);
                sourceMetadataCache.Add(cacheKey, metadataXml);
            }
            intrinsicProofs[needed.Token] = new AssGlyphValidator.IntrinsicFaceProof(
                needed.OriginalName, sourceFont, metadataXml);
        }
        AssGlyphValidator.Validate(usage, xmlFiles, warnings, intrinsicProofs);
        return new FontSubsetResult(new(fixedAss), generated.Select(path => new WorkFile(path)).ToEquatableArray(), new(warnings));
    });

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

    private static bool IsFont(string path) => InputRules.IsFontExtension(Path.GetExtension(path));
    private static string Executable(string name) => ToolPathLookup.Executable(name);
}
