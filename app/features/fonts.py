# -*- coding: utf-8 -*-
# 字体域：POST /api/check_fonts（体检）、POST /api/font_supply（缺字体一键从备份目录补给）
# 体检双轨：AFS 主用=跑一遍子集当体检；assfonts 回退=建库+子集检查。
# 补给：AFS 报 missing 的 Fontname → fontTools 在 source 目录做 fullname/族匹配 →
#       校验目标目录无 (family,style) 重复 → 复制 → 自动重跑体检。
import os, re, shutil, time, uuid
from app import core

# ---------------- 体检 ----------------

def _parse_afs_missing(txt):
    """从 AFS 输出解析缺字体报告：'Not found font file: name,0,0,1、name2,...'"""
    missing = []
    for line in txt.splitlines():
        if "Not found font file:" in line:
            rest = line.split("Not found font file:", 1)[1].strip()
            for item in rest.split("、"):
                name = item.split(",", 1)[0].strip().lstrip("@")
                if name and name not in missing:
                    missing.append(name)
    return missing

def _afs_crash_font(txt):
    """AFS 崩溃时找出正在处理的字体文件名：优先从 pyftsubset 命令行取，回退最后一条 'Start subset'。"""
    m = re.search(r"Command execution failed:.*?pyftsubset (\S+)", txt)
    if m:
        return os.path.basename(m.group(1))
    name = ""
    for line in txt.splitlines():
        if "Start subset" in line:
            name = line.split("Start subset", 1)[1].strip()
    return name

def check_fonts_afs(subs, fonts_dir):
    ck = os.path.join(core.TMP_DIR, "fontcheck_" + uuid.uuid4().hex[:8])
    os.makedirs(ck)
    logs = []
    missing = []
    for i, sub in enumerate(subs):
        out_i = os.path.join(ck, "out_%d" % i)
        log_i = os.path.join(ck, "afs_%d.log" % i)
        rc = core.run_to_file([core.AFS, sub, "--fonts", fonts_dir, "--output", out_i,
                               "--bin-path", core.PY_SCRIPTS], log_i, timeout=600)
        txt = core.read_tail(log_i, 5000)
        logs.append(txt)
        for name in _parse_afs_missing(txt):
            if name not in missing:
                missing.append(name)
        if "Duplicate fonts" in txt or "duplicate fonts" in txt:
            return {"ok": False,
                    "error": "字体目录存在重复字体（AFS 要求目录内同族字体只保留一份），请精简字体目录，或在高级选项把子集化工具改为 assfonts",
                    "missing": missing, "log": "\n".join(logs)}
        if rc != 0 and not missing:
            # 不是缺字、是工具崩溃：最常见根因是字体文件本身不规范
            # （如老字体 cmap 子表 length 字段写错，pyftsubset 严格解析即断言退出）。
            crash_font = _afs_crash_font(txt)
            tip = "建议在高级选项把子集化工具切换为 assfonts，或把字体处理改为仅收集（不裁字形，坏字体免疫）后重试"
            if crash_font:
                return {"ok": False,
                        "error": "字体文件 %s 不规范或损坏，AFS 无法解析——%s" % (crash_font, tip),
                        "missing": [], "log": "\n".join(logs)}
            return {"ok": False, "error": "AFS 检查失败——%s" % tip,
                    "missing": [], "log": "\n".join(logs)}
    return {"ok": len(missing) == 0, "missing": missing, "log": "\n".join(logs)}

def check_fonts_assfonts(subs, fonts_dir):
    if not fonts_dir or not os.path.isdir(fonts_dir):
        return {"ok": False, "error": "字体目录不存在"}
    if not subs:
        return {"ok": False, "error": "请先提供字幕文件"}
    ck = os.path.join(core.TMP_DIR, "fontcheck_" + uuid.uuid4().hex[:8])
    os.makedirs(ck)
    dbdir = os.path.join(ck, "db")
    os.makedirs(dbdir)
    outdir = os.path.join(ck, "out")
    os.makedirs(outdir)
    build_log = os.path.join(ck, "build.log")
    rc = core.run_to_file([core.ASSFONTS, "-f", fonts_dir, "-b", "-d", dbdir], build_log, timeout=300)
    if rc != 0:
        return {"ok": False, "error": "assfonts 数据库构建失败", "log": core.read_tail(build_log, 80)}
    db = os.path.join(dbdir, "fonts.json")
    if not os.path.exists(db):
        return {"ok": False, "error": "assfonts 数据库未生成"}
    sub_log = os.path.join(ck, "sub.log")
    args = [core.ASSFONTS, "-f", fonts_dir, "-s", "-c",
            "-d", dbdir, "-o", outdir] + list(subs)
    rc = core.run_to_file(args, sub_log, timeout=600)
    txt = core.read_tail(sub_log, 5000)
    if rc != 0:
        return {"ok": False, "error": "assfonts 检查失败", "missing": [], "log": txt}
    missing = []
    for line in txt.splitlines():
        if "Missing codepoints for" in line:
            cm = re.search(r'Missing codepoints for "([^"]+)"', line)
            name = cm.group(1) if cm else ""
            rest = line[cm.end():] if cm else line
            cps = re.findall(r'(?:0x[0-9a-fA-F]+|\d+)', rest)
            item = "%s 缺 %d 个码点" % (name, len(set(cps)))
            if item not in missing:
                missing.append(item)
        elif "Missing the font" in line:
            m = re.search(r'"([^"]*)"', line)
            name = m.group(1).strip() if m else ""
            if not name:
                item = "未命名字体缺字形"
                if item not in missing:
                    missing.append(item)
            elif name not in missing:
                missing.append(name)
    return {"ok": len(missing) == 0 and rc == 0, "missing": missing, "log": txt}


def check_fonts(subs, fonts_dir):
    if not fonts_dir or not os.path.isdir(fonts_dir):
        return {"ok": False, "error": "字体目录不存在"}
    if not subs:
        return {"ok": False, "error": "请先提供字幕文件"}
    if core.CONFIG.get("subset_tool") == "afs" and core.AFS:
        return check_fonts_afs(subs, fonts_dir)
    return check_fonts_assfonts(subs, fonts_dir)


def handle_check_fonts(body):
    return check_fonts([s for s in (body.get("subs") or []) if s], (body.get("fonts_dir") or "").strip())


# ---------------- 字体补给 ----------------
# 备份目录中检索 missing 字体，复制进目标字体目录；口径与 font_dup_scan 一致：
# 重复判定 = (family, style) 样式键（nameID 16|1 + 17|2，英文名优先），AFS 同口径。

try:
    from fontTools.ttLib import TTFont, TTCollection
    _HAS_FT = True
except Exception:
    _HAS_FT = False

FONT_EXTS = (".ttf", ".otf", ".ttc", ".otc")

def _norm(s):
    return re.sub(r"\s+", "", s or "").lower()

def _font_records(path):
    """一个字体文件 -> [(family, style, fullname)]，TTC 展开每个子字体；出错返回 []。"""
    out = []
    try:
        if path.lower().endswith((".ttc", ".otc")):
            fonts = TTCollection(path, lazy=True).fonts
        else:
            fonts = [TTFont(path, lazy=True, fontNumber=0)]
    except Exception:
        return out
    for f in fonts:
        fam = sty = full = ""
        try:
            for rec in f["name"].names:
                if rec.nameID in (1, 16):
                    s = rec.toUnicode().strip()
                    if s and (not fam or rec.langID in (0x0409,)):
                        fam = s  # 英文名优先
                elif rec.nameID in (2, 17):
                    s = rec.toUnicode().strip()
                    if s and (not sty or rec.langID in (0x0409,)):
                        sty = s
                elif rec.nameID == 4:
                    s = rec.toUnicode().strip()
                    if s and (not full or rec.langID in (0x0409,)):
                        full = s
        except Exception:
            pass
        finally:
            try:
                f.close()
            except Exception:
                pass
        if fam:
            out.append((fam, sty or "Regular", full))
    return out

def _index_dir(d):
    """目录 -> {file: [(fam,sty,full),...]} 与查找表。"""
    by_file = {}
    for fn in os.listdir(d):
        fp = os.path.join(d, fn)
        if os.path.isfile(fp) and fn.lower().endswith(FONT_EXTS):
            recs = _font_records(fp)
            if recs:
                by_file[fp] = recs
    return by_file

def _style_keys(by_file):
    """{file: set((family,style))} —— AFS 重复口径。"""
    return {fp: set((f, s) for f, s, _ in recs) for fp, recs in by_file.items()}

def _match_in_source(name, src_by_file):
    """missing 名 -> [(file, family, style)]，优先级：fullname > family+style > family。"""
    n = _norm(name)
    hits_exact, hits_famsty, hits_fam = [], [], []
    for fp, recs in src_by_file.items():
        for fam, sty, full in recs:
            if full and _norm(full) == n:
                hits_exact.append((fp, fam, sty))
            if (_norm(fam) + _norm(sty)) == n or (_norm(fam + " " + sty)) == n:
                hits_famsty.append((fp, fam, sty))
            if _norm(fam) == n:
                hits_fam.append((fp, fam, sty))
    return hits_exact or hits_famsty or hits_fam

def font_supply(subs, fonts_dir, source_dir):
    if not _HAS_FT:
        return {"error": "服务端缺 fonttools（服务所用 Python 未安装），无法补给；请在服务端 Python 上 pip install fonttools"}
    if not os.path.isdir(source_dir):
        return {"error": "备份字体目录不存在: %s" % source_dir}
    first = check_fonts(subs, fonts_dir)
    if "error" in first and "missing" not in first:
        return first  # 参数/目录级错误直接透传
    missing = first.get("missing") or []
    if not missing:
        return {"ok": True, "supplied": [], "skipped_dup": [], "not_found": [],
                "recheck": first, "note": "体检未发现缺字体，无需补给"}

    src_by_file = _index_dir(source_dir)
    dst_by_file = _index_dir(fonts_dir)
    dst_keys = {}  # (fam,sty) -> 已存在文件
    for fp, keys in _style_keys(dst_by_file).items():
        for k in keys:
            dst_keys.setdefault(k, fp)

    supplied, skipped, not_found = [], [], []
    for name in missing:
        hits = _match_in_source(name, src_by_file)
        if not hits:
            not_found.append(name)
            continue
        done = False
        for fp, fam, sty in hits:
            key = (fam, sty)
            if key in dst_keys:
                skipped.append({"missing": name, "existing": os.path.basename(dst_keys[key]),
                                "key": "%s / %s" % (fam, sty)})
                done = True  # 目标已有同族样式，视作已满足（名字写法差异）
                break
            shutil.copy2(fp, os.path.join(fonts_dir, os.path.basename(fp)))
            supplied.append({"missing": name, "file": os.path.basename(fp),
                             "key": "%s / %s" % (fam, sty)})
            dst_keys[key] = fp
            done = True
            break
        if not done:
            not_found.append(name)

    recheck = check_fonts(subs, fonts_dir) if supplied else first
    return {"ok": bool(recheck.get("ok")), "supplied": supplied, "skipped_dup": skipped,
            "not_found": not_found, "recheck": recheck}

def handle_font_supply(body):
    subs = [s for s in (body.get("subs") or []) if s]
    fonts_dir = (body.get("fonts_dir") or "").strip()
    source_dir = (body.get("source_dir") or "").strip()
    if not fonts_dir or not os.path.isdir(fonts_dir):
        return {"error": "字体目录不存在"}
    if not subs:
        return {"error": "请先提供字幕文件"}
    return font_supply(subs, fonts_dir, source_dir)


handlers = {
    "POST": {"/api/check_fonts": handle_check_fonts, "/api/font_supply": handle_font_supply},
}
