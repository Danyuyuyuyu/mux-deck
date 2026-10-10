using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace MuxDeck.Core.Muxing;

internal static partial class AssGlyphValidator
{
    internal sealed record FontFace(string Name, bool Bold, bool Italic);
    internal sealed record IntrinsicFaceRequest(string Token, string SourceFileName, int FaceIndex, string OriginalName);
    internal sealed record IntrinsicFaceProof(string OriginalName, string SourceFontPath, string SourceXmlPath);
    private sealed record FontCandidate(HashSet<string> Names, bool Bold, bool Italic,
        int? Weight, string SourceName, int? FaceIndex, HashSet<int> Glyphs);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string ReadAss(string path)
    {
        var text = StrictUtf8.GetString(File.ReadAllBytes(path)).TrimStart('\uFEFF');
        if (!text.Contains("[Script Info]", StringComparison.OrdinalIgnoreCase) ||
            !text.Contains("[V4+ Styles]", StringComparison.OrdinalIgnoreCase) ||
            !text.Contains("[Events]", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"ASS 缺少必要节：{path}");
        return text;
    }

    public static Dictionary<FontFace, HashSet<int>> ReadUsage(string text)
    {
        var styles = new Dictionary<string, FontFace>(StringComparer.OrdinalIgnoreCase);
        var usage = new Dictionary<FontFace, HashSet<int>>();
        var section = "";
        string[] format = [];
        var dialogueCount = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { section = line; format = []; continue; }
            if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
                format = line[7..].Split(',').Select(x => x.Trim()).ToArray();
            else if (section.Equals("[V4+ Styles]", StringComparison.OrdinalIgnoreCase) &&
                     line.StartsWith("Style:", StringComparison.OrdinalIgnoreCase))
            {
                var values = line[6..].Split(',');
                styles[Field("Name", format, values)] = new FontFace(
                    Field("Fontname", format, values).TrimStart('@').ToUpperInvariant(),
                    Field("Bold", format, values) != "0", Field("Italic", format, values) != "0");
            }
            else if (section.Equals("[Events]", StringComparison.OrdinalIgnoreCase) &&
                     line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
            {
                dialogueCount++;
                var values = line[9..].TrimStart().Split(',', format.Length);
                var style = Field("Style", format, values);
                if (!styles.TryGetValue(style, out var baseFont))
                    throw new InvalidDataException($"字幕使用了未定义样式：{style}");
                var font = baseFont;
                var drawing = false;
                foreach (Match part in Parts().Matches(Field("Text", format, values)))
                {
                    var value = part.Value;
                    if (value.StartsWith('{'))
                    {
                        // 首阶段不猜测动画中字体切换的时间范围，避免漏掉初态字形。
                        if (Regex.IsMatch(value, @"\\t\([^)]*\\(?:fn|r|b-?\d*(?=\\|\))|i-?\d*(?=\\|\)))"))
                            throw new InvalidDataException("首阶段尚不支持 \\t 动画中的字体/粗斜体切换。");
                        foreach (Match tag in Tags().Matches(value[1..^1]))
                        {
                            if (tag.Groups[1].Success)
                            {
                                var name = tag.Groups[1].Value.Trim().TrimStart('@');
                                font = font with { Name = name.Length == 0 ? baseFont.Name : name.ToUpperInvariant() };
                            }
                            else if (tag.Groups[2].Success)
                            {
                                var reset = tag.Groups[2].Value.Trim();
                                font = reset.Length == 0 ? baseFont : styles.GetValueOrDefault(reset)
                                    ?? throw new InvalidDataException($"字幕覆盖样式不存在：{reset}");
                                drawing = false;
                            }
                            else if (tag.Groups[3].Success) drawing = tag.Groups[3].Value != "0";
                            else if (tag.Groups[4].Success)
                            {
                                var weight = tag.Groups[4].Value;
                                if (weight is not ("" or "0" or "1" or "-1" or "400" or "700"))
                                    throw new InvalidDataException("首阶段尚不支持非标准 ASS 字重：" + weight);
                                font = font with { Bold = weight.Length == 0 ? baseFont.Bold : weight is "1" or "-1" or "700" };
                            }
                            else if (tag.Groups[5].Success)
                                font = font with { Italic = tag.Groups[5].Value.Length == 0 ? baseFont.Italic : tag.Groups[5].Value != "0" };
                        }
                    }
                    else if (!drawing)
                    {
                        if (!usage.TryGetValue(font, out var glyphs)) usage[font] = glyphs = [];
                        value = value.Replace("\\N", "").Replace("\\n", "").Replace("\\h", "\u00A0");
                        foreach (var rune in value.EnumerateRunes())
                            if (!Rune.IsControl(rune)) glyphs.Add(rune.Value);
                    }
                }
            }
        }
        if (dialogueCount == 0 || usage.Count == 0)
            throw new InvalidDataException("ASS 未包含可子集化的文本对白。");
        return usage;
    }

    public static void Validate(Dictionary<FontFace, HashSet<int>> usage, IReadOnlyList<string> xmlFiles,
        List<MuxWarning>? warnings = null, IReadOnlyDictionary<string, IntrinsicFaceProof>? intrinsicProofs = null)
    {
        var fonts = new Dictionary<FontFace, HashSet<int>>();
        var candidates = ReadCandidates(xmlFiles);
        foreach (var candidate in candidates)
        {
            foreach (var name in candidate.Names)
            {
                var face = new FontFace(name, candidate.Bold, candidate.Italic);
                // 同名同样式重复字体采用交集，不能通过另一份字体掩盖缺字。
                if (!fonts.TryGetValue(face, out var glyphs)) fonts[face] = new HashSet<int>(candidate.Glyphs);
                else glyphs.IntersectWith(candidate.Glyphs);
            }
        }
        foreach (var (name, glyphs) in usage)
        {
            FontCandidate? synthetic = null;
            IntrinsicFaceProof? intrinsic = null;
            if (!fonts.TryGetValue(name, out var available))
            {
                var sameFamily = candidates.Where(candidate => candidate.Names.Contains(name.Name)).ToArray();
                if (sameFamily.Length > 1)
                    throw new InvalidDataException($"字体 {name.Name} 的样式来源存在多个同 family 候选，不能确认实际字体来源。");
                if (!name.Bold && sameFamily.Length == 1 && sameFamily[0].Bold && sameFamily[0].Italic == name.Italic &&
                    intrinsicProofs is not null && intrinsicProofs.TryGetValue(name.Name, out var proof))
                {
                    intrinsic = proof;
                    ValidateIntrinsicProof(sameFamily[0], proof);
                    available = sameFamily[0].Glyphs;
                }
                else
                {
                    if (!name.Bold || sameFamily.Length != 1 || sameFamily[0].Bold || sameFamily[0].Italic != name.Italic)
                        throw new InvalidDataException($"修正 ASS 引用的字体 face 未产出：{name.Name} / bold={name.Bold} / italic={name.Italic}");
                    var candidate = sameFamily[0];
                    if (candidate.Weight is not int knownWeight || knownWeight is < 1 or > 1000 ||
                        700 <= EffectiveWeight(knownWeight) + 150)
                        throw new InvalidDataException($"字体 {candidate.SourceName} 的字重信息缺失或不符合已验证的合成粗体条件。");
                    // libass 会在 700 > 原字重+150 且原 face 非粗体时合成加粗；仅允许唯一同 family、同斜体候选。
                    // 合成样式不会补字，仍必须在这个实际字体自己的 cmap 内找到全部所用码点。
                    synthetic = candidate;
                    available = candidate.Glyphs;
                }
            }
            var missing = glyphs.Where(x => !available.Contains(x)).Take(12).ToArray();
            if (missing.Length > 0)
                throw new InvalidDataException($"子集字体 {name.Name}（bold={name.Bold}, italic={name.Italic}）缺字形：{string.Join("、", missing.Select(x => $"U+{x:X4}"))}");
            if (synthetic is not null && warnings is not null)
            {
                var message = $"字体“{synthetic.SourceName}”需要播放器合成粗体；其所用字形已核验。";
                AddWarning(warnings, "FONT_SYNTHETIC_BOLD", message);
            }
            if (intrinsic is not null && warnings is not null)
                AddWarning(warnings, "FONT_INTRINSIC_WEIGHT",
                    $"字体“{Path.GetFileName(intrinsic.SourceFontPath)}”已明确指定固有粗体字重；保留其原有字形，未额外合成粗体。");
        }
    }

    public static IReadOnlyList<IntrinsicFaceRequest> GetIntrinsicFaceRequests(
        Dictionary<FontFace, HashSet<int>> usage, IReadOnlyList<string> xmlFiles, string correctedAss,
        Dictionary<FontFace, HashSet<int>> originalUsage)
    {
        var candidates = ReadCandidates(xmlFiles);
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match mapping in SubsetMapping().Matches(correctedAss))
        {
            var token = mapping.Groups[1].Value.ToUpperInvariant();
            var original = mapping.Groups[2].Value.Trim().TrimStart('@').ToUpperInvariant();
            if (mappings.TryGetValue(token, out var existing) && existing != original)
                throw new InvalidDataException("AFS 字体 mapping 存在歧义：" + token);
            mappings[token] = original;
        }
        var requests = new List<IntrinsicFaceRequest>();
        foreach (var (face, glyphs) in usage)
        {
            if (face.Bold) continue;
            var family = candidates.Where(candidate => candidate.Names.Contains(face.Name)).ToArray();
            if (family.Any(candidate => !candidate.Bold && candidate.Italic == face.Italic)) continue;
            if (family.Length != 1 || !family[0].Bold || family[0].Italic != face.Italic) continue;
            var candidate = family[0];
            if (!mappings.TryGetValue(face.Name, out var original) ||
                !originalUsage.TryGetValue(face with { Name = original }, out var originalGlyphs) ||
                !originalGlyphs.SetEquals(glyphs))
                throw new InvalidDataException("固有字重的 AFS mapping 与原 ASS 字体使用不一致，不能确认显式 face 身份。");
            if (candidate.FaceIndex is not int index)
                throw new InvalidDataException("无法确认固有字重字体的源 faceIndex。");
            requests.Add(new IntrinsicFaceRequest(face.Name, candidate.SourceName, index, original));
        }
        return requests;
    }

    private static void ValidateIntrinsicProof(FontCandidate candidate, IntrinsicFaceProof proof)
    {
        var xml = ReadXml(proof.SourceXmlPath);
        var fullNames = ReadNames(xml, "4", "6");
        var genericNames = ReadNames(xml, "16");
        if (genericNames.Count == 0) genericNames = ReadNames(xml, "1");
        if (genericNames.Count == 0)
            throw new InvalidDataException("源字体缺少 generic family 元数据，不能确认独立 face 身份。");
        if (!fullNames.Contains(proof.OriginalName) || genericNames.Contains(proof.OriginalName))
            throw new InvalidDataException("原 ASS 未明确指定源字体的 full/PostScript 名，或使用了 generic family，不能替代为固有粗体。");
        var sourceFlags = ReadStyleFlags(xml);
        var sourceWeight = ReadWeight(xml);
        if ((sourceFlags & 32) == 0 || ((sourceFlags & 1) != 0 || (sourceFlags & 512) != 0) != candidate.Italic ||
            sourceWeight is not int knownSourceWeight || candidate.Weight is not int knownSubsetWeight ||
            knownSourceWeight is < 1 or > 1000 || knownSubsetWeight is < 1 or > 1000 ||
            EffectiveWeight(knownSourceWeight) != EffectiveWeight(knownSubsetWeight))
            throw new InvalidDataException("固有字重字体的源/子集 bold、italic 或 weight 元数据不一致或缺失。");
    }

    private static List<FontCandidate> ReadCandidates(IReadOnlyList<string> xmlFiles)
    {
        var candidates = new List<FontCandidate>();
        foreach (var file in xmlFiles)
        {
            var xml = ReadXml(file);
            var cmap = xml.Descendants("cmap").Elements()
                .Where(x => x.Attribute("platformID")?.Value == "0" ||
                    (x.Attribute("platformID")?.Value == "3" && x.Attribute("platEncID")?.Value is "1" or "10"))
                .Descendants("map")
                .Where(x => x.Attribute("code")?.Value.StartsWith("0x", StringComparison.Ordinal) == true &&
                    x.Attribute("name")?.Value != ".notdef")
                .Select(x => Convert.ToInt32(x.Attribute("code")!.Value[2..], 16)).ToHashSet();
            var flags = ReadStyleFlags(xml);
            var fileName = Path.GetFileNameWithoutExtension(file);
            var suffix = SubsetSuffix().Match(fileName);
            int? faceIndex = suffix.Success && int.TryParse(suffix.Groups[1].Value, out var index) ? index : null;
            candidates.Add(new FontCandidate(ReadNames(xml, "1", "4", "6", "16"), (flags & 32) != 0,
                (flags & 1) != 0 || (flags & 512) != 0, ReadWeight(xml), SubsetSuffix().Replace(fileName, ""), faceIndex, cmap));
        }
        return candidates;
    }

    private static XDocument ReadXml(string file)
    {
        using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        return XDocument.Load(reader);
    }
    private static HashSet<string> ReadNames(XDocument xml, params string[] ids) => xml.Descendants("namerecord")
        .Where(record => record.Attribute("nameID") is { } id && ids.Contains(id.Value))
        .Select(record => record.Value.Trim().TrimStart('@').ToUpperInvariant()).Where(name => name.Length > 0).ToHashSet();
    private static int ReadStyleFlags(XDocument xml) => Convert.ToInt32((xml.Descendants("fsSelection")
        .SingleOrDefault()?.Attribute("value")?.Value ?? throw new InvalidDataException("字体缺少 OS/2 样式标识。"))
        .Replace(" ", ""), 2);
    private static int? ReadWeight(XDocument xml) => int.TryParse(xml.Descendants("usWeightClass")
        .SingleOrDefault()?.Attribute("value")?.Value, out var weight) ? weight : null;
    private static void AddWarning(List<MuxWarning> warnings, string code, string message)
    {
        var index = warnings.FindIndex(warning => warning.Code == code && warning.Message == message);
        if (index < 0) warnings.Add(new MuxWarning(code, message, 1));
        else warnings[index] = warnings[index] with { Occurrences = checked(warnings[index].Occurrences + 1) };
    }

    // libass 兼容旧字体的 1–9 字重编码；其余使用 OpenType 数值。
    private static int EffectiveWeight(int weight) => weight switch
    {
        1 => 100, 2 => 200, 3 => 300, 4 => 350, 5 => 400, 6 => 600, 7 => 700, 8 => 800, 9 => 900, _ => weight
    };

    private static string Field(string name, string[] format, string[] values)
    {
        var index = Array.FindIndex(format, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index < values.Length ? values[index].Trim()
            : throw new InvalidDataException($"ASS 格式字段缺失：{name}");
    }

    [GeneratedRegex(@"\{[^}]*\}|[^{}]+")]
    private static partial Regex Parts();
    [GeneratedRegex(@"\\fn([^\\}]*)|\\r([^\\}]*)|\\p(\d+)(?=\\|$)|\\b(-?\d*)(?=\\|$)|\\i(-?\d*)(?=\\|$)")]
    private static partial Regex Tags();
    [GeneratedRegex(@"\.(\d+)\.[A-Za-z0-9]{8}(?=\.[^.]+$)")]
    private static partial Regex SubsetSuffix();
    [GeneratedRegex(@"^; Font Subset: ([^ ]+) - ([^\r\n]+)", RegexOptions.Multiline)]
    private static partial Regex SubsetMapping();
}
