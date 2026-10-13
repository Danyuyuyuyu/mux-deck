using System.Xml.Linq;
using MuxDeck.Application.Ports;
using MuxDeck.Domain;

namespace MuxDeck.Infrastructure.Muxing;

public sealed class TtxFontInspector(IMuxToolRunner? runner = null, IToolCatalog? catalog = null) : IFontInspector
{
    public Task<FontInspectionResult> InspectAsync(FontInspectionRequest request, ToolCallContext context,
        CancellationToken cancellationToken = default) => PortBoundary.Guard(async () =>
    {
        var xml = Path.Combine(context.Workspace.WorkDirectory, "font-" + Guid.NewGuid().ToString("N") + ".xml");
        var executable = await ToolCatalog.Require(catalog ?? new ToolCatalog(), ToolCapability.InspectFonts, context.Tools, cancellationToken).ConfigureAwait(false);
        var collection = Path.GetExtension(request.Path).ToLowerInvariant() is ".ttc" or ".otc";
        if (request.FaceIndex < 0 || (!collection && request.FaceIndex != 0))
            throw new InvalidDataException("独立源字体的 faceIndex 必须为 0。");
        await ExportAsync(executable, request.Path, xml, context, cancellationToken, true, collection ? request.FaceIndex : null).ConfigureAwait(false);
        return ReadFacts(request, xml);
    });

    internal Task ExportAsync(string executable, string font, string xml, ToolCallContext context,
        CancellationToken token, bool includeCmap, int? collectionFace = null) => PortBoundary.Guard(async () =>
    {
        var args = new List<string> { "-q", "-t", "name" };
        if (includeCmap) args.AddRange(["-t", "cmap"]);
        args.AddRange(["-t", "OS/2"]);
        if (collectionFace is { } face) args.AddRange(["-y", face.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        args.AddRange(["-o", xml, font]);
        await new ToolExecutor(runner).RunAsync(executable, args, context, token, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        if (!File.Exists(xml)) throw new InvalidDataException(includeCmap ? "无法读取子集字体的 name/cmap 表。" : "未产出源字体 name/OS2 元数据。");
        return true;
    });

    private static FontInspectionResult ReadFacts(FontInspectionRequest request, string file)
    {
        var xml = AssGlyphValidator.ReadXml(file);
        var names = xml.Descendants("namerecord").Where(record => record.Attribute("nameID")?.Value is "1" or "4" or "6" or "16")
            .Select(record => record.Value.Trim().TrimStart('@').ToUpperInvariant()).Where(name => name.Length > 0).Distinct().Order().ToEquatableArray();
        var glyphs = xml.Descendants("cmap").Elements().Where(table => table.Attribute("platformID")?.Value == "0" ||
                (table.Attribute("platformID")?.Value == "3" && table.Attribute("platEncID")?.Value is "1" or "10"))
            .Descendants("map").Where(map => map.Attribute("code")?.Value.StartsWith("0x", StringComparison.Ordinal) == true && map.Attribute("name")?.Value != ".notdef")
            .Select(map => Convert.ToInt32(map.Attribute("code")!.Value[2..], 16)).Distinct().Order().ToEquatableArray();
        var flags = Convert.ToInt32((xml.Descendants("fsSelection").SingleOrDefault()?.Attribute("value")?.Value
            ?? throw new InvalidDataException("字体缺少 OS/2 样式标识。")).Replace(" ", ""), 2);
        int? weight = int.TryParse(xml.Descendants("usWeightClass").SingleOrDefault()?.Attribute("value")?.Value, out var value) ? value : null;
        return new(request.Path, request.FaceIndex, names, glyphs, (flags & 32) != 0, (flags & 1) != 0 || (flags & 512) != 0, weight);
    }
}
