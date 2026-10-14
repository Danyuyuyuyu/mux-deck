"""仅用于本地验收：还原 AFS 随机字体名，逐表严格比较字体证据。"""
import argparse
import copy
import hashlib
import json
import re
import struct
from pathlib import Path
from fontTools.ttLib import TTFont

MAP_LINE = re.compile(rb"^([ \t]*;[ \t]*Font Subset:[ \t]+)([A-Z0-9]{8})([ \t]+-[ \t]+)(.+)$")
FONT_SUFFIX = re.compile(r"^(.*\.\d+)\.([A-Z0-9]{8})(\.[^.]+)$")
FONT_TAG = re.compile(rb"(\\fn)(@?)([A-Z0-9]{8})(?=[ \t]*(?:\\|[})]))")

def sha(data):
    return hashlib.sha256(data).hexdigest().upper()

def encoded(value):
    return json.dumps(value, ensure_ascii=False, separators=(",", ":")).encode("utf8")

def line_parts(line):
    for ending in (b"\r\n", b"\n", b"\r"):
        if line.endswith(ending):
            return line[:-len(ending)], ending
    return line, b""

def font_map(data):
    """映射只来自该文件的 AFS 注释，重复行和行顺序在全文比较中保留。"""
    result = {}
    for line in data.splitlines(keepends=True):
        body, _ = line_parts(line)
        match = MAP_LINE.fullmatch(body)
        if match:
            token, original = match.group(2), match.group(4)
            original.decode("utf8", errors="strict")
            if token in result and result[token] != original:
                raise ValueError("同一随机字体名对应不同原名")
            result[token] = original
        elif re.match(rb"^[ \t]*;[ \t]*Font Subset:", body):
            raise ValueError("AFS 映射格式无效")
    return result

def restore_ass(data, mapping):
    """只处理映射名、Style.Fontname 与覆盖标签；普通 Dialogue 文本保持原字节。"""
    result, section, font_column = [], None, None
    for line in data.splitlines(keepends=True):
        body, ending = line_parts(line)
        header = body.lstrip(b"\xef\xbb\xbf").strip()
        if header.startswith(b"[") and header.endswith(b"]"):
            section, font_column = header.lower(), None
        match = MAP_LINE.fullmatch(body)
        if match:
            body = match.group(1) + mapping[match.group(2)] + match.group(3) + match.group(4)
        if section in (b"[v4+ styles]", b"[v4 styles]"):
            if body.lstrip().lower().startswith(b"format:"):
                columns = body.split(b":", 1)[1].split(b",")
                indices = [i for i, c in enumerate(columns) if c.strip().lower() == b"fontname"]
                if len(indices) != 1:
                    raise ValueError("Style Format 缺少唯一 Fontname")
                font_column = indices[0]
            elif body.lstrip().lower().startswith(b"style:"):
                if font_column is None:
                    raise ValueError("Style 缺少 Format")
                prefix, raw = body.split(b":", 1)
                fields = raw.split(b",")
                field = fields[font_column]
                left, right = len(field) - len(field.lstrip(b" \t")), len(field.rstrip(b" \t"))
                name = field[left:right]
                vertical = b"@" if name.startswith(b"@") else b""
                token = name[len(vertical):]
                if token in mapping:
                    fields[font_column] = field[:left] + vertical + mapping[token] + field[right:]
                body = prefix + b":" + b",".join(fields)
        if section == b"[events]" and re.match(rb"^[ \t]*(?:Dialogue|Comment):", body, re.I):
            def block_replace(block):
                def tag_replace(tag):
                    token = tag.group(3)
                    return tag.group(1) + tag.group(2) + mapping[token] if token in mapping else tag.group(0)
                return FONT_TAG.sub(tag_replace, block.group(0))
            body = re.sub(rb"\{[^}]*\}", block_replace, body)
        result.append(body + ending)
    return b"".join(result)

def name_table(font, mapping):
    """记录和存储元数据仍严格保留；不删除任何 name ID，也不忽略无关记录。"""
    raw = font.reader["name"]
    fmt, count, start = struct.unpack(">HHH", raw[:6])
    if fmt != 0:
        raise ValueError("未验证的 name 表格式：" + str(fmt))
    names = font["name"].names
    if count != len(names):
        raise ValueError("name 记录数量不符")
    records, used = [], set()
    for i, record in enumerate(names):
        fields = struct.unpack(">HHHHHH", raw[6 + i * 12:18 + i * 12])
        platform, encoding, language, name_id, length, offset = fields
        if (platform, encoding, language, name_id) != (record.platformID, record.platEncID, record.langID, record.nameID):
            raise ValueError("name 记录顺序不可验证")
        text = record.toUnicode()
        for token, original in mapping.items():
            text = text.replace(token.decode("ascii"), original.decode("utf8"))
        records.append({"Platform": platform, "Encoding": encoding, "Language": language,
            "NameId": name_id, "Length": length, "Offset": offset, "Value": text})
        used.update(range(start + offset, start + offset + length))
    unused = [[i, value] for i, value in enumerate(raw) if i >= 6 + count * 12 and i not in used]
    return {"Format": fmt, "Count": count, "StorageOffset": start, "Records": records, "Unused": unused}

def font_tables(path, mapping):
    """name 还原后比较；head 只清零两处授权字段，其他表使用原始字节哈希。"""
    with TTFont(path, lazy=True, recalcTimestamp=False) as font:
        hashes, audit = {}, {}
        for tag in sorted(font.reader.keys()):
            raw = font.reader[tag]
            audit[tag] = {"RawSha256": sha(raw)}
            if tag == "name":
                normalized = name_table(font, mapping)
                data = encoded(normalized)
                audit[tag]["Restored"] = normalized
            elif tag == "head":
                if len(raw) != 54:
                    raise ValueError("head 表长度不符")
                audit[tag]["checkSumAdjustment"] = raw[8:12].hex()
                audit[tag]["modified"] = raw[28:36].hex()
                data = raw[:8] + bytes(4) + raw[12:28] + bytes(8) + raw[36:]
            else:
                data = raw
            hashes[tag] = sha(data)
        return {"SfntVersion": font.sfntVersion, "Tables": hashes}, audit

def normalize_side(report_path, side, values, output):
    source = report_path.parent / side
    destination = output / side
    destination.mkdir()
    combined, ass, fonts, identities, audit = {}, [], [], [], {"AssMappings": [], "Fonts": []}
    for track in values["Ass"]:
        path = source / f"track-{track['Id']}.ass"
        raw = path.read_bytes()
        if raw.decode("utf-8-sig") != track["Text"]:
            raise ValueError("ASS 原文与原始报告不符")
        mapping = font_map(raw)
        for token, original in mapping.items():
            if token in combined and combined[token] != original:
                raise ValueError("跨字幕的随机名来源冲突")
            combined[token] = original
        restored = restore_ass(raw, mapping)
        (destination / path.name).write_bytes(restored)
        ass.append({"Id": track["Id"], "Sha256": sha(restored)})
        audit["AssMappings"].append({"Id": track["Id"],
            "Mapping": {k.decode(): v.decode("utf8") for k, v in mapping.items()}})
    for item in values["Attachments"]:
        identity = copy.deepcopy(item)
        path = source / f"attachment-{item['Id']}.bin"
        if sha(path.read_bytes()) != item["Sha256"]:
            raise ValueError("附件哈希与原始证据不符")
        if item["ContentType"] in ("font/ttf", "font/otf"):
            match = FONT_SUFFIX.fullmatch(item["Name"])
            if match:
                token = match.group(2).encode()
                if token not in combined:
                    raise ValueError("附件随机名没有 ASS 来源映射")
                identity["Name"] = match.group(1) + match.group(3)
                identity["OriginalFont"] = combined[token].decode("utf8")
            # 数量、顺序、ID、MIME、大小及文件名保留，只有字体原始整文件哈希由逐表检查替代。
            del identity["Sha256"]
            normalized, details = font_tables(path, combined)
            fonts.append({"Id": item["Id"], "Identity": identity, **normalized})
            audit["Fonts"].append({"Id": item["Id"], "Identity": identity, "Tables": details})
        identities.append(identity)
    return {"Attachments": identities, "Ass": ass, "Fonts": fonts}, audit

def compare_report(report_path, output):
    report_path, output = Path(report_path).resolve(), Path(output).resolve()
    output.mkdir()
    raw = json.loads(report_path.read_text(encoding="utf-8-sig"))
    values = {side: {e["Name"]: e[side.title()] for e in raw["Comparisons"]} for side in ("baseline", "current")}
    old, old_audit = normalize_side(report_path, "baseline", values["baseline"], output)
    new, new_audit = normalize_side(report_path, "current", values["current"], output)
    comparisons = []
    for entry in raw["Comparisons"]:
        name = entry["Name"]
        if name not in ("Attachments", "Ass"):
            # 其余分类完全沿用同一 PowerShell 脚本的严格结果，不做转换或排序。
            comparisons.append(copy.deepcopy(entry))
        else:
            equal = encoded(old[name]) == encoded(new[name])
            if name == "Ass" and equal:
                equal = all((output / "baseline" / f"track-{e['Id']}.ass").read_bytes() ==
                    (output / "current" / f"track-{e['Id']}.ass").read_bytes() for e in old[name])
            comparisons.append({"Name": name, "Equal": equal, "Baseline": old[name], "Current": new[name]})
    comparisons.append({"Name": "Fonts", "Equal": encoded(old["Fonts"]) == encoded(new["Fonts"]),
        "Baseline": old["Fonts"], "Current": new["Fonts"]})
    result = {"Status": "一致" if all(e["Equal"] for e in comparisons) else "差异",
        "RawReport": str(report_path), "Rule": "用户批准：AFS 映射还原；字体逐表，仅排除 head.modified/checkSumAdjustment",
        "Comparisons": comparisons, "BaselineAudit": old_audit, "CurrentAudit": new_audit}
    (output / "comparison.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf8")
    return result

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", required=True)
    parser.add_argument("--output-directory", required=True)
    args = parser.parse_args()
    result = compare_report(args.report, args.output_directory)
    print(result["Status"] + ": " + str(Path(args.output_directory) / "comparison.json"))
    return 0 if result["Status"] == "一致" else 1

if __name__ == "__main__":
    raise SystemExit(main())
