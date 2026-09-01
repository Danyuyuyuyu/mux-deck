# -*- coding: utf-8 -*-
# 轨道与字幕：GET /api/probe（mkvmerge -J）、GET /api/match_subs（按集数匹配简繁字幕）、POST /api/prep_subs（编码预处理）
import json, os, re, uuid
from app import core

def probe(path):
    if not path or not os.path.exists(path):
        return {"error": "file not found", "tracks": [], "attachments": 0}
    out = os.path.join(core.JOBS_DIR, "probe_" + uuid.uuid4().hex[:8] + ".json")
    try:
        try:
            rc = core.run_to_file([core.MKVMERGE, "-J", path], out, timeout=120)
        except Exception as ex:
            return {"error": "mkvmerge error: " + str(ex), "tracks": [], "attachments": 0}
        if rc != 0:
            return {"error": "mkvmerge failed (exit %d)" % rc, "tracks": [], "attachments": 0}
        try:
            with open(out, "r", encoding="utf-8", errors="replace") as f:
                data = json.load(f)
        except Exception as ex:
            return {"error": "parse failed: " + str(ex), "tracks": [], "attachments": 0}
        tracks = []
        vh = 0
        for t in data.get("tracks", []):
            pr = t.get("properties", {})
            tracks.append({
                "id": t.get("id"), "type": t.get("type"), "codec": t.get("codec"),
                "lang": pr.get("language_ietf") or pr.get("language") or "-",
                "name": pr.get("track_name") or "", "default": bool(pr.get("default_track")),
            })
            if t.get("type") == "video" and not vh:
                # 视频分辨率高度（与 mux_cli 同口径：pixel_height 优先，回退 display_dimensions），供输出名 {res} 预览
                vh = int(pr.get("pixel_height") or pr.get("height") or 0)
                if not vh:
                    dd = str(pr.get("display_dimensions") or "")
                    if "x" in dd:
                        try:
                            vh = int(dd.split("x")[1])
                        except ValueError:
                            vh = 0
        return {"tracks": tracks, "attachments": len(data.get("attachments", [])), "video_height": vh}
    finally:
        try:
            os.remove(out)
        except OSError:
            pass


def handle(q):
    return probe((q.get("path") or [""])[0])


# ---------------- 集数匹配字幕 ----------------
EP_RE = [re.compile(r'S(\d{1,2})E(\d{1,3})', re.I),
         re.compile(r'\[(\d{1,3})\]'),
         re.compile(r'[-_]\s*(\d{1,3})(?![0-9])'),
         re.compile(r'EP(\d{1,3})', re.I),
         re.compile(r'(?<![0-9A-Za-z])E(\d{1,3})(?![0-9A-Za-z])', re.I),
         re.compile(r'(\d{1,3})(?![0-9])$')]

def ep_of(name):
    base = os.path.splitext(os.path.basename(name))[0]
    for pat in EP_RE:
        m = pat.search(base)
        if m:
            g = m.groups()
            return int(g[-1])
    return -1

# 字幕槽位分类别名表：token（小写，分隔符 [._\- ] 切分）→ 槽位 'sc'/'tc'。
# 相邻两 token 以 '-' 连接的组合也参与查表（如 'zh'+'hans' → 'zh-hans'）。
# 项目仅简繁两类字幕：简体槽位固定语言 zh-Hans、繁体槽位固定 zh-Hant（见 mux_cli 默认），不再做语言自动配对。
SC_TC_ALIAS = {
    'zh-hans': 'sc', 'zh-cn': 'sc', 'zh-chs': 'sc', 'chs': 'sc', 'sc': 'sc',
    'jpsc': 'sc', '简体': 'sc', '简': 'sc', 'gb': 'sc',
    'zh-hant': 'tc', 'zh-tw': 'tc', 'zh-hk': 'tc', 'zh-mo': 'tc', 'cht': 'tc',
    'tc': 'tc', 'jptc': 'tc', '繁体': 'tc', '繁': 'tc', 'big5': 'tc',
}

def _sub_tokens(name):
    base = os.path.basename(name or "")
    return [t for t in re.split(r"[._\- ]+", base.lower()) if t]

def sub_kind(name):
    """文件名 → 字幕槽位 'sc' | 'tc' | ''。仅路由到简/繁槽位，不产出语言值（语言由槽位决定）。
    简繁标签优先（先扫 SC/TC 别名含相邻组合，再扫单 token），认不出不猜。"""
    toks = _sub_tokens(name)
    for i, t in enumerate(toks):
        if i + 1 < len(toks):
            p = SC_TC_ALIAS.get(t + "-" + toks[i + 1])
            if p:
                return p
        s = SC_TC_ALIAS.get(t)
        if s:
            return s
    return ""

def match_subs(video):
    if not video or not os.path.exists(video):
        return {"sc": "", "tc": ""}
    d = os.path.dirname(video)
    vep = ep_of(video)
    subs = []
    try:
        for fn in os.listdir(d):
            if fn.lower().endswith((".ass", ".ssa", ".srt")):
                subs.append(os.path.join(d, fn))
    except OSError:
        pass
    sc = tc = ""
    for sp in subs:
        sep = ep_of(sp)
        if sep >= 0 and sep == vep:
            k = sub_kind(sp)
            if k == "sc" and not sc:
                sc = sp
            elif k == "tc" and not tc:
                tc = sp
    return {"sc": sc, "tc": tc}


def handle_match(q):
    return match_subs((q.get("path") or [""])[0])


def detect_fonts_dir(video):
    """识别视频旁的字体目录（Fonts 优先，其次 Font）；服务端 isdir 直判，供前端统一识别入口。"""
    if not video:
        return {"fonts_dir": ""}
    d = os.path.dirname(video)
    if not d:
        return {"fonts_dir": ""}
    for cand in ("Fonts", "Font"):
        p = os.path.join(d, cand)
        if os.path.isdir(p):
            return {"fonts_dir": p}
    return {"fonts_dir": ""}


def handle_fonts_dir(q):
    return detect_fonts_dir((q.get("path") or [""])[0])


def detect_chapters(video):
    """识别视频旁同名章节文件（stem.txt / stem.xml / stem.chapters.txt）；服务端 exists 直判。"""
    if not video or not os.path.isfile(video):
        return {"chapters": ""}
    d = os.path.dirname(video)
    stem = os.path.splitext(os.path.basename(video))[0]
    for cand in (stem + ".chapters.txt", stem + ".txt", stem + ".xml", stem + ".chapters.xml"):
        p = os.path.join(d, cand)
        if os.path.isfile(p):
            return {"chapters": p}
    return {"chapters": ""}


def handle_detect_chapters(q):
    return detect_chapters((q.get("path") or [""])[0])


# ---------------- 字幕编码预处理 ----------------
BOM_UTF8 = bytes([239, 187, 191])
BOM_UTF16 = (bytes([255, 254]), bytes([254, 255]))

def detect_encoding(data):
    if data[:3] == BOM_UTF8:
        return "utf-8-sig"
    if data[:2] in BOM_UTF16:
        return "utf-16"
    try:
        data.decode("utf-8")
        return "utf-8"
    except UnicodeDecodeError:
        pass
    ok = []
    for enc in ("gbk", "big5"):
        try:
            data.decode(enc)
            ok.append(enc)
        except UnicodeDecodeError:
            pass
    if len(ok) == 1:
        return ok[0]
    if len(ok) == 2:
        gt = data.decode("gbk")
        bt = data.decode("big5")
        if gt == bt:
            return "gbk"
        trad = "們這個來時說對會沒還著麼說與為過讓"
        gc = sum(1 for ch in gt if ch in trad)
        bc = sum(1 for ch in bt if ch in trad)
        if bc >= 3 and bc >= gc * 2:
            return "big5"
        return "ambiguous"
    return "gbk"

def prep_subs(sc, tc):
    out = {"sc": None, "tc": None}
    ambiguous = False
    for key, path in (("sc", sc), ("tc", tc)):
        if not path or not os.path.exists(path):
            out[key] = {"path": path or "", "encoding": "-", "converted": False}
            continue
        with open(path, "rb") as f:
            data = f.read()
        enc = detect_encoding(data)
        if enc in ("utf-8",):
            out[key] = {"path": path, "encoding": "utf-8", "converted": False}
            continue
        if enc == "ambiguous":
            enc = "big5" if key == "tc" else "gbk"
            ambiguous = True
        try:
            if enc == "utf-8-sig":
                text = data.decode("utf-8-sig")
            elif enc == "utf-16":
                text = data.decode("utf-16")
            else:
                text = data.decode(enc)
        except Exception as ex:
            out[key] = {"path": path, "encoding": enc, "converted": False, "error": str(ex)}
            continue
        dest = os.path.join(core.TMP_DIR, "%s_%s.ass" % (key, uuid.uuid4().hex[:8]))
        with open(dest, "w", encoding="utf-8", newline="") as f:
            f.write(text)
        out[key] = {"path": dest, "encoding": enc, "converted": True}
    if ambiguous:
        out["ambiguous"] = True
    return out


def handle_prep(body):
    return prep_subs((body.get("sc") or "").strip(), (body.get("tc") or "").strip())


handlers = {
    "GET": {"/api/probe": handle, "/api/match_subs": handle_match, "/api/detect_fonts_dir": handle_fonts_dir,
            "/api/detect_chapters": handle_detect_chapters},
    "POST": {"/api/prep_subs": handle_prep},
}
