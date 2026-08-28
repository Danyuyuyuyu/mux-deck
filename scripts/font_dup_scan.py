# -*- coding: utf-8 -*-
# font_dup_scan.py — 只读扫描字体目录，按 AFS 规则（同族字体）列出重复组，生成报告，不做任何修改。
import os, sys
from fontTools.ttLib import TTFont, TTCollection

def style_key_of(path):
    """读出字体的 (family, subfamily) 样式键（nameID 16+17 优先，回落 1+2；英文名优先）。
    与 AFS 重复判定口径一致：同族同样式 = 重复。TTC 每个子字体各算一个键。"""
    keys = set()
    try:
        if path.lower().endswith((".ttc", ".otc")):
            fonts = TTCollection(path, lazy=True).fonts
        else:
            fonts = [TTFont(path, lazy=True, fontNumber=0)]
        for f in fonts:
            try:
                names = {}
                for rec in f["name"].names:
                    if rec.nameID not in (1, 2, 16, 17):
                        continue
                    try:
                        s = rec.toUnicode().strip()
                    except Exception:
                        continue
                    if not s:
                        continue
                    en = rec.langID in (0x409, 0x0)
                    cur = names.get(rec.nameID)
                    if cur is None or (en and not cur[1]):
                        names[rec.nameID] = (s, en)
                def val(nid, fallback):
                    if nid in names:
                        return names[nid][0]
                    return names.get(fallback, ("", False))[0]
                fam = val(16, 1)
                sub = val(17, 2)
                if fam:
                    keys.add((fam, sub))
            except Exception:
                pass
            try:
                f.close()
            except Exception:
                pass
    except Exception as ex:
        print("  [跳过] %s (%s)" % (os.path.basename(path), ex), flush=True)
    return keys

def main(root, report_path):
    exts = (".ttf", ".otf", ".ttc", ".otc")
    files = []
    for dp, _, fns in os.walk(root):
        for fn in fns:
            if fn.lower().endswith(exts):
                files.append(os.path.join(dp, fn))
    files.sort(key=str.lower)
    print("扫描 %d 个字体文件: %s" % (len(files), root), flush=True)

    fam_map = {}  # (family, subfamily) -> [file paths]
    for i, fp in enumerate(files):
        if (i + 1) % 50 == 0:
            print("  进度 %d/%d" % (i + 1, len(files)), flush=True)
        for key in style_key_of(fp):
            fam_map.setdefault(key, []).append(fp)

    dups = {k: ps for k, ps in fam_map.items() if len(ps) > 1}
    lines = []
    lines.append("# 字体目录重复扫描报告（AFS 规则：同族同样式只许一份）")
    lines.append("")
    lines.append("- 目录: %s" % root)
    lines.append("- 字体文件: %d 个；样式键（族+样式）: %d 个；**重复样式: %d 个**" % (len(files), len(fam_map), len(dups)))
    lines.append("- 本报告为只读扫描，未修改任何文件。每个重复组保留一个文件、移走其余即可让 AFS 通过。")
    lines.append("- 注意：同名不同样式（如 Arial 与 Arial Narrow、Regular 与 Bold）不算重复，均可保留。")
    lines.append("")
    for key in sorted(dups, key=lambda k: (k[0].lower(), k[1].lower())):
        ps = sorted(set(dups[key]), key=str.lower)
        lines.append("## %s / %s（%d 份）" % (key[0], key[1] or "(Regular)", len(ps)))
        for j, p in enumerate(ps):
            tag = "保留建议" if j == 0 else "移走候选"
            lines.append("- [%s] %s" % (tag, os.path.relpath(p, root)))
        lines.append("")
    with open(report_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
    print("重复族: %d 个；报告: %s" % (len(dups), report_path), flush=True)

if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else r"D:\Video\Font",
         sys.argv[2] if len(sys.argv) > 2 else r"E:\Video\mux-deck\docs\字体重复扫描报告.md")
