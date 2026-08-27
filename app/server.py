# -*- coding: utf-8 -*-
# Mux UI backend: single/batch mux, sub encoding convert, font check, preview, drag-drop
import json, os, re, shutil, subprocess, threading, time, uuid, urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))  # 项目根（本文件位于 app/）
JOBS_DIR = os.path.join(BASE, "data", "jobs")
TMP_DIR = os.path.join(BASE, "data", "tmp")
PREVIEW_DIR = os.path.join(BASE, "data", "previews")
LOG_DIR = os.path.join(BASE, "data", "log")
PS1 = os.path.join(BASE, "scripts", "ass_mux_manual.ps1")
POWERSHELL = r"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"
def _first_existing(*paths):
    for p in paths:
        if p and os.path.isfile(p):
            return p
    return None

MKVMERGE = _first_existing(os.path.join(BASE, "bin", "mkvtoolnix", "mkvmerge.exe")) or shutil.which("mkvmerge") or r"C:\Program Files\MKVToolNix\mkvmerge.exe"
MKVEXTRACT = _first_existing(os.path.join(BASE, "bin", "mkvtoolnix", "mkvextract.exe")) or shutil.which("mkvextract") or r"C:\Program Files\MKVToolNix\mkvextract.exe"
ASSFONTS = _first_existing(os.path.join(BASE, "bin", "assfonts", "assfonts.exe")) or shutil.which("assfonts") or os.path.join(BASE, "bin", "assfonts", "assfonts.exe")
FFMPEG = _first_existing(os.path.join(BASE, "bin", "ffmpeg", "bin", "ffmpeg.exe")) or shutil.which("ffmpeg")
HOST = "127.0.0.1"
SCAN_ROOT = "D:\\Video"
CONFIG_PATH = os.path.join(BASE, "app", "config.json")
CONFIG = {"scan_root": SCAN_ROOT}
try:
    with open(CONFIG_PATH, encoding="utf-8") as _f:
        _c = json.load(_f)
    if isinstance(_c.get("scan_root"), str) and os.path.isdir(_c["scan_root"]):
        CONFIG["scan_root"] = _c["scan_root"]
except Exception:
    pass

for d in (JOBS_DIR, TMP_DIR, PREVIEW_DIR, LOG_DIR):
    os.makedirs(d, exist_ok=True)

JOBS = {}
JOBS_LOCK = threading.Lock()
INDEX = {"t": 0.0, "map": {}}
INDEX_LOCK = threading.Lock()

VIDEO_EXT = {'.mkv','.mp4','.m2ts','.ts','.avi','.mov','.webm','.flv','.wmv','.m4v'}
SUB_EXT   = {'.ass','.ssa','.srt'}
FONT_EXT  = {'.ttf','.otf','.ttc','.otc','.woff','.woff2'}
AUDIO_EXT = {'.mka','.flac','.aac','.m4a','.mp3','.opus','.ogg','.wav','.ac3','.dts','.eac3'}

# ---------------- helpers ----------------

def list_dir(path):
    if not path:
        drives = []
        for d in "ABCDEFGHIJKLMNOPQRSTUVWXYZ":
            if os.path.exists(d + ":\\"):
                drives.append(d + ":\\")
        return {"path": "", "drives": drives, "dirs": [], "files": []}
    path = os.path.normpath(path)
    if not os.path.isdir(path):
        return {"path": path, "dirs": [], "files": [], "error": "not a directory"}
    dirs, files = [], []
    try:
        with os.scandir(path) as it:
            for e in it:
                try:
                    if e.is_dir():
                        dirs.append(e.name)
                    else:
                        try: sz = e.stat().st_size
                        except OSError: sz = 0
                        files.append((e.name, sz))
                except OSError:
                    pass
    except OSError as ex:
        return {"path": path, "dirs": [], "files": [], "error": str(ex)}
    dirs.sort(key=str.lower)
    files.sort(key=lambda x: str.lower(x[0]))
    return {"path": path, "dirs": dirs, "files": files}

def _kill_tree(proc):
    try:
        subprocess.Popen(["taskkill", "/T", "/F", "/PID", str(proc.pid)],
                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    except Exception:
        try:
            proc.kill()
        except Exception:
            pass

def run_to_file(cmd, out_path, timeout=1800, cwd=None, state=None):
    parent = os.path.dirname(out_path)
    if parent:
        try:
            os.makedirs(parent, exist_ok=True)
        except OSError:
            pass
    with open(out_path, "wb") as f:
        proc = subprocess.Popen(cmd, stdout=f, stderr=subprocess.STDOUT, cwd=cwd)
    if state is not None:
        state["proc"] = proc
        if state.get("stop_requested"):
            _kill_tree(proc)
            proc.wait()
            return 1
    try:
        proc.wait(timeout=timeout)
        return proc.returncode
    except subprocess.TimeoutExpired:
        _kill_tree(proc)
        proc.wait()
        try:
            with open(out_path, "a", encoding="utf-8", errors="replace") as f:
                f.write("\nTIMEOUT: 任务超时已终止\n")
        except Exception:
            pass
        return 1

def decode_log(data):
    if data[:2] in (b"\xff\xfe", b"\xfe\xff"):
        return data.decode("utf-16", errors="replace")
    try:
        return data.decode("utf-8")
    except UnicodeDecodeError:
        return data.decode("gbk", errors="replace")

def read_tail(path, n=300):
    try:
        with open(path, "rb") as f:
            data = f.read()
        return "\n".join(decode_log(data).splitlines()[-n:])
    except Exception:
        return ""

def ext_of(p):
    return os.path.splitext(p)[1].lower()

# ---------------- probe ----------------

def probe(path):
    if not path or not os.path.exists(path):
        return {"error": "file not found", "tracks": [], "attachments": 0}
    out = os.path.join(JOBS_DIR, "probe_%s.json" % uuid.uuid4().hex[:8])
    try:
        try:
            rc = run_to_file([MKVMERGE, "-J", path], out, timeout=120)
        except Exception as ex:
            return {"error": "mkvmerge error: %s" % ex, "tracks": [], "attachments": 0}
        if rc != 0:
            return {"error": "mkvmerge failed (exit %d)" % rc, "tracks": [], "attachments": 0}
        try:
            with open(out, "r", encoding="utf-8", errors="replace") as f:
                data = json.load(f)
        except Exception as ex:
            return {"error": "parse failed: %s" % ex, "tracks": [], "attachments": 0}
        tracks = []
        for t in data.get("tracks", []):
            pr = t.get("properties", {})
            tracks.append({
                "id": t.get("id"),
                "type": t.get("type"),
                "codec": t.get("codec"),
                "lang": pr.get("language_ietf") or pr.get("language") or "-",
                "name": pr.get("track_name") or "",
                "default": bool(pr.get("default_track")),
            })
        return {"tracks": tracks, "attachments": len(data.get("attachments", []))}
    finally:
        try:
            os.remove(out)
        except OSError:
            pass

# ---------------- sub encoding ----------------

def detect_encoding(data):
    if data[:3] == b"\xef\xbb\xbf":
        return "utf-8-sig"
    if data[:2] in (b"\xff\xfe", b"\xfe\xff"):
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
        dest = os.path.join(TMP_DIR, "%s_%s.ass" % (key, uuid.uuid4().hex[:8]))
        with open(dest, "w", encoding="utf-8", newline="") as f:
            f.write(text)
        out[key] = {"path": dest, "encoding": enc, "converted": True}
    if ambiguous:
        out["ambiguous"] = True
    return out

# ---------------- font check ----------------

def check_fonts(subs, fonts_dir):
    if not fonts_dir or not os.path.isdir(fonts_dir):
        return {"ok": False, "error": "字体目录不存在"}
    if not subs:
        return {"ok": False, "error": "请先提供字幕文件"}
    ck = os.path.join(TMP_DIR, "fontcheck_" + uuid.uuid4().hex[:8])
    os.makedirs(ck)
    dbdir = os.path.join(ck, "db")
    os.makedirs(dbdir)
    outdir = os.path.join(ck, "out")
    os.makedirs(outdir)
    build_log = os.path.join(ck, "build.log")
    rc = run_to_file([ASSFONTS, "-f", fonts_dir, "-b", "-d", dbdir], build_log, timeout=300)
    if rc != 0:
        return {"ok": False, "error": "assfonts 数据库构建失败", "log": read_tail(build_log, 80)}
    db = os.path.join(dbdir, "fonts.json")
    if not os.path.exists(db):
        return {"ok": False, "error": "字体数据库未生成"}
    sub_log = os.path.join(ck, "sub.log")
    args = [ASSFONTS, "-f", fonts_dir, "-s", "-c",
            "-d", dbdir, "-o", outdir] + list(subs)
    rc = run_to_file(args, sub_log, timeout=600)
    txt = read_tail(sub_log, 5000)
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

# ---------------- preview ----------------

def _extract_embedded_fonts(video, pid):
    """把视频自带的字体附件抽到 PREVIEW_DIR/<pid>_fonts（内封轨道预览用）；无或失败返回空串。"""
    tmp_json = os.path.join(JOBS_DIR, "probe_%s.json" % pid)
    plog = os.path.join(LOG_DIR, "preview_%s_probe.log" % pid)
    atts = []
    if run_to_file([MKVMERGE, "-J", video], tmp_json, timeout=120) == 0:
        try:
            with open(tmp_json, encoding="utf-8", errors="replace") as f:
                j = json.load(f)
            for a in (j.get("attachments") or []):
                fn = str(a.get("file_name") or "")
                ct = str(a.get("content_type") or "")
                if fn.lower().endswith((".ttf", ".otf", ".ttc", ".otc")) or "font" in ct.lower():
                    atts.append((a.get("id"), fn))
        except Exception:
            atts = []
    try:
        os.remove(tmp_json)
    except OSError:
        pass
    if not atts:
        return ""
    fdir = os.path.join(PREVIEW_DIR, pid + "_fonts")
    os.makedirs(fdir, exist_ok=True)
    cmd = [MKVEXTRACT, "attachments", video]
    for aid, fn in atts:
        cmd.append("%d:%s" % (aid, os.path.join(fdir, fn)))
    run_to_file(cmd, plog, timeout=120)
    return fdir if os.listdir(fdir) else ""

def make_preview(video, sub, fonts_dir, t, mode="frame"):
    ff = FFMPEG
    if not ff:
        return {"error": "未找到 ffmpeg（请安装并加入 PATH）"}
    pid = uuid.uuid4().hex[:10]
    # 内封轨道预览：sub 形如 "track:<id>:<ext>"，先抽出该轨与视频自带字体附件
    embedded = isinstance(sub, str) and sub.startswith("track:")
    if embedded:
        if not video or not os.path.exists(video):
            return {"error": "内封轨道预览需要视频文件"}
        parts = sub.split(":")
        try:
            tid = int(parts[1])
        except (IndexError, ValueError):
            return {"error": "无效的内封轨道选择"}
        text_ext = (parts[2] if len(parts) > 2 else "ass").lower()
        if text_ext not in ("ass", "ssa", "srt"):
            return {"error": "PGS 图形字幕无法用 libass 渲染；请先在「字幕提取」导出后用自定义路径预览"}
        track_sub = os.path.join(PREVIEW_DIR, pid + "_track." + text_ext)
        xlog = os.path.join(LOG_DIR, "preview_%s_extract.log" % pid)
        rc = run_to_file([MKVEXTRACT, "tracks", video, "%d:%s" % (tid, track_sub)], xlog, timeout=120)
        if rc != 0 or not os.path.exists(track_sub):
            return {"error": "内封字幕轨提取失败", "log": read_tail(xlog, 30)}
        sub = track_sub
        fonts_dir = _extract_embedded_fonts(video, pid) or fonts_dir
    if not sub or not os.path.exists(sub):
        return {"error": "字幕文件不存在"}
    if mode != "subtitle" and (not video or not os.path.exists(video)):
        return {"error": "视频文件不存在"}
    if not embedded and (not fonts_dir or not os.path.isdir(fonts_dir)):
        return {"error": "字体目录不存在"}
    out_png = os.path.join(PREVIEW_DIR, pid + ".png")
    preview_ass = os.path.join(PREVIEW_DIR, pid + ".ass")
    if sub.lower().endswith(".srt"):
        conv_log = os.path.join(LOG_DIR, "preview_conv_%s.log" % pid)
        crc = run_to_file([ff, "-y", "-i", sub, preview_ass], conv_log, timeout=300)
        if crc != 0 or not os.path.exists(preview_ass):
            return {"error": "SRT 转 ASS 失败", "log": read_tail(conv_log, 60)}
    else:
        try:
            shutil.copy(sub, preview_ass)
        except Exception as ex:
            return {"error": "复制字幕失败: %s" % ex}
    if fonts_dir and os.path.isdir(fonts_dir):
        try:
            rel_fonts = os.path.relpath(fonts_dir, PREVIEW_DIR)
        except ValueError as ex:
            return {"error": "字体目录与预览目录不在同一驱动器: %s" % ex}
        vf = "ass=%s:fontsdir=%s,scale=1280:-2" % (os.path.basename(preview_ass), rel_fonts.replace("\\", "/"))
    else:
        vf = "ass=%s,scale=1280:-2" % os.path.basename(preview_ass)  # 无字体目录（内封轨且视频无字体附件）时用系统字体
    log = os.path.join(LOG_DIR, "preview_%s.log" % pid)
    if mode == "subtitle":
        cmd = [ff, "-y", "-f", "lavfi", "-i", "color=c=black:s=1920x1080:r=25:d=36000",
               "-ss", str(t), "-vf", vf, "-frames:v", "1", out_png]
    else:
        cmd = [ff, "-y", "-i", video, "-ss", str(t), "-vf", vf, "-frames:v", "1", out_png]
    rc = run_to_file(cmd, log, timeout=600, cwd=PREVIEW_DIR)
    if rc != 0 or not os.path.exists(out_png):
        return {"error": "渲染失败", "log": read_tail(log, 60)}
    return {"ok": True, "url": "/api/file?path=" + pid + ".png", "pid": pid}

# ---------------- mux jobs (single = batch of 1) ----------------

def build_cmd(it, common):
    full = dict(common)
    for k, v in it.items():
        if v:
            full[k] = v
    video = full.get("video", "")
    cmd = [POWERSHELL, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", PS1, "-Video", video]
    def add(k, v):
        if v is None:
            v = ""
        elif not isinstance(v, str):
            v = str(v)
        v = v.strip()
        if v:
            cmd.append(k); cmd.append(v)
    add("-ScSub", full.get("sc_sub") or full.get("sc"))
    add("-TcSub", full.get("tc_sub") or full.get("tc"))
    add("-FontsDir", full.get("fonts_dir"))
    add("-ScName", full.get("sc_name"))
    add("-TcName", full.get("tc_name"))
    audio_tracks = full.get("audio_tracks")
    if not audio_tracks and full.get("audio_mode") == "none":
        audio_tracks = "none"
    add("-AudioTracks", audio_tracks)
    add("-Audio", full.get("audio"))
    add("-AudioLang", full.get("audio_lang"))
    add("-AudioName", full.get("audio_name"))
    add("-OutDir", full.get("out_dir"))
    if full.get("force"):
        cmd.append("-Force")
    if full.get("no_backup"):
        cmd.append("-NoBackup")
    add("-SubtitleTracks", full.get("subtitle_tracks"))
    if full.get("keep_attachments"):
        cmd.append("-KeepAttachments")
    return cmd

def start_batch(body):
    items = body.get("items") or []
    if not items:
        return {"error": "没有任务项"}
    for it in items:
        if not it.get("video") or not os.path.exists(it.get("video")):
            return {"error": "存在无效的视频路径: %s" % it.get("video")}
    with JOBS_LOCK:
        if any(s.get("status") == "running" for s in JOBS.values()):
            return {"error": "另一个任务正在运行，请等待完成"}
    common = {k: body.get(k) for k in ("fonts_dir","audio","audio_mode","keep_src_audio","audio_lang","audio_name","out_dir","force","sc_name","tc_name","no_backup","audio_tracks","subtitle_tracks","keep_attachments")}
    if (common.get("out_dir") or "").strip():
        base_names = [os.path.basename(it.get("video", "")) for it in items]
        lower_names = [n.lower() for n in base_names]
        dup = sorted({n for n in base_names if lower_names.count(n.lower()) > 1})
        if dup:
            return {"error": "输出同名冲突，已拒绝提交: %s" % "、".join(dup)}
    jid = uuid.uuid4().hex[:12]
    jdir = os.path.join(JOBS_DIR, jid)
    os.makedirs(jdir)
    with open(os.path.join(jdir, "params.json"), "w", encoding="utf-8") as f:
        json.dump({"common": common, "items": items}, f, ensure_ascii=False, indent=2)
    first = items[0]
    fv = first.get("video", "")
    out_dir = (common.get("out_dir") or "").strip() or os.path.dirname(fv)
    result = os.path.join(out_dir, os.path.splitext(os.path.basename(fv))[0] + os.path.splitext(fv)[1]) if fv else ""
    state = {"id": jid, "dir": jdir, "status": "running", "exit": None,
             "started": time.time(), "current": 0, "total": len(items),
             "current_video": "", "item_status": "", "failed": 0, "results": [], "result": result}
    def worker():
        for i, it in enumerate(items):
            if state.get("stopped"):
                break
            state["current"] = i + 1
            state["item_status"] = "running"
            state["current_video"] = it.get("video", "")
            log = os.path.join(jdir, "item_%02d.log" % (i + 1))
            try:
                rc = run_to_file(build_cmd(it, common), log, state=state)
                od = (common.get("out_dir") or "").strip()
                out_path = os.path.join(od, os.path.basename(it.get("video", ""))) if od else it.get("video", "")
                state["results"].append({"video": it.get("video", ""), "output": out_path,
                                         "ok": rc == 0 and not state.get("stopped"), "exit": rc})
                if rc != 0 and not state.get("stopped"):
                    state["failed"] += 1
            except Exception as ex:
                state["results"].append({"video": it.get("video", ""), "ok": False, "exit": -1})
                state["failed"] += 1
                try:
                    with open(log, "a", encoding="utf-8", errors="replace") as f:
                        f.write("\nSERVER ERROR: %s\n" % ex)
                except Exception:
                    pass
            state["item_status"] = "done"
        if state.get("stopped"):
            state["status"] = "killed"
            state["exit"] = -1
        else:
            state["status"] = "done" if state["failed"] == 0 else "error"
            state["exit"] = 0 if state["failed"] == 0 else 1
        try:
            with open(os.path.join(jdir, "state.json"), "w", encoding="utf-8") as f:
                json.dump({"status": state["status"], "exit": state["exit"], "failed": state.get("failed", 0),
                           "results": state.get("results", []), "current": state.get("current", 0),
                           "total": state.get("total", 0)}, f, ensure_ascii=False, indent=2)
        except Exception:
            pass
        try:
            parts = []
            for i in range(1, state.get("total", 1) + 1):
                log = os.path.join(jdir, "item_%02d.log" % i)
                tl = read_tail(log, 60)
                if tl:
                    parts.append("---- item %d ----\n%s" % (i, tl))
            with open(os.path.join(LOG_DIR, "job_%s.log" % jid), "w", encoding="utf-8", errors="replace") as f:
                f.write("\n".join(parts))
        except Exception:
            pass
    with JOBS_LOCK:
        if any(s.get("status") == "running" for s in JOBS.values()):
            return {"error": "另一个任务正在运行，请等待完成"}
        JOBS[jid] = state
        threading.Thread(target=worker, daemon=True).start()
    return {"job": jid}

def job_status(jid):
    s = JOBS.get(jid)
    if not s:
        return {"error": "unknown job"}
    # merge logs: current item full + previous items tails
    parts = []
    total_items = s.get("total", 1)
    cur = s.get("current", 0)
    for i in range(1, min(cur + 1, total_items + 1)):
        log = os.path.join(s["dir"], "item_%02d.log" % i)
        if i == cur and s.get("item_status") == "running":
            parts.append(read_tail(log, 300))
        else:
            tl = read_tail(log, 25)
            if tl:
                parts.append("---- item %d ----\n%s" % (i, tl))
    merged = "\n".join(parts)
    progress = None
    m = re.findall(r'(?:进度|Progress)[:：]\s*(\d+)%', merged)
    if m:
        progress = int(m[-1])
    return {"id": s["id"], "status": s["status"], "exit": s["exit"],
            "current": cur, "total": total_items, "failed": s.get("failed", 0),
            "current_video": s.get("current_video", ""), "progress": progress,
            "results": s.get("results", []), "result": s.get("result", ""), "log": merged}

# ---------------- drag & drop name resolution ----------------

def build_index():
    now = time.time()
    if now - INDEX["t"] < 300 and INDEX["map"]:
        return INDEX["map"]
    with INDEX_LOCK:
        now = time.time()
        if now - INDEX["t"] < 300 and INDEX["map"]:
            return INDEX["map"]
        m = {}
        skip = ("__pycache__", "[Backup]", ".git", ".dsh_tmp")
        base_l = BASE.lower().rstrip(os.sep) + os.sep
        try:
            for dp, dns, fns in os.walk(CONFIG["scan_root"]):
                if dp.lower().startswith(base_l):
                    dns[:] = [d for d in dns if d not in ("jobs", "tmp", "previews") and not d.startswith("__")]
                else:
                    dns[:] = [d for d in dns if d not in skip and not d.startswith("__")]
                for fn in fns:
                    fp = os.path.join(dp, fn)
                    try:
                        sz = os.path.getsize(fp)
                    except OSError:
                        sz = -1
                    m.setdefault(fn.lower(), []).append({"path": fp, "size": sz})
        except Exception:
            pass
        INDEX["t"] = now
        INDEX["map"] = m
    return m

def resolve_drop(names):
    m = build_index()
    missing = [n for n in names if not m.get(n.lower())]
    if missing:
        INDEX["t"] = 0.0
        m = build_index()
    out = {}
    for n in names:
        out[n] = m.get(n.lower(), [])
    return out

def extract_subs(video, tracks, out_dir):
    if not video or not os.path.exists(video):
        return {"error": "视频文件不存在"}
    if not tracks:
        return {"error": "请选择要提取的字幕轨"}
    out_dir = (out_dir or "").strip() or os.path.dirname(video)
    if not os.path.isdir(out_dir):
        try:
            os.makedirs(out_dir)
        except OSError as ex:
            return {"error": "无法创建输出目录: %s" % ex}
    with JOBS_LOCK:
        if any(js.get("status") == "running" for js in JOBS.values()):
            return {"error": "另一个任务正在运行，请等待完成"}
    jid = uuid.uuid4().hex[:12]
    jdir = os.path.join(JOBS_DIR, jid)
    os.makedirs(jdir)
    base = os.path.splitext(os.path.basename(video))[0]
    with open(os.path.join(jdir, "params.json"), "w", encoding="utf-8") as f:
        json.dump({"video": video, "tracks": tracks, "out_dir": out_dir}, f, ensure_ascii=False, indent=2)
    cmd = [MKVEXTRACT, "tracks", video]
    outs = []
    for t in tracks:
        try:
            tid = int(t.get("id"))
        except (TypeError, ValueError):
            continue
        ext = (t.get("ext") or "ass").lstrip(".")
        lang = re.sub(r"[^0-9A-Za-z\-_]", "", str(t.get("lang") or ""))
        stem = "%s_track%d" % (base, tid)
        if lang:
            stem += "." + lang
        out = os.path.join(out_dir, stem + "." + ext)
        cmd.append("%d:%s" % (tid, out))
        outs.append(out)
    if len(cmd) <= 3:
        return {"error": "无效的轨道选择"}
    log = os.path.join(jdir, "item_01.log")
    state = {"id": jid, "dir": jdir, "status": "running", "exit": None, "started": time.time(),
             "current": 1, "total": 1, "current_video": video, "item_status": "running",
             "failed": 0, "results": [], "result": "、".join(os.path.basename(o) for o in outs)}
    def worker():
        try:
            rc = run_to_file(cmd, log, state=state)
            state["exit"] = rc
            if state.get("stopped"):
                state["status"] = "killed"
            else:
                state["status"] = "done" if rc == 0 else "error"
            state["results"].append({"video": video, "ok": rc == 0 and not state.get("stopped"), "exit": rc})
            if rc != 0 and not state.get("stopped"):
                state["failed"] = 1
        except Exception as ex:
            state["exit"] = -1
            state["status"] = "error"
            state["failed"] = 1
            try:
                with open(log, "a", encoding="utf-8", errors="replace") as f:
                    f.write("\nSERVER ERROR: %s\n" % ex)
            except Exception:
                pass
        try:
            with open(os.path.join(jdir, "state.json"), "w", encoding="utf-8") as f:
                json.dump({"status": state["status"], "exit": state["exit"], "failed": state.get("failed", 0),
                           "results": state.get("results", []), "current": state.get("current", 0),
                           "total": state.get("total", 0)}, f, ensure_ascii=False, indent=2)
        except Exception:
            pass
        try:
            with open(os.path.join(LOG_DIR, "job_%s.log" % jid), "w", encoding="utf-8", errors="replace") as f:
                f.write(read_tail(log, 100))
        except Exception:
            pass
    with JOBS_LOCK:
        if any(js.get("status") == "running" for js in JOBS.values()):
            return {"error": "另一个任务正在运行，请等待完成"}
        JOBS[jid] = state
        threading.Thread(target=worker, daemon=True).start()
    return {"job": jid}

# history / match / open helpers (spliced into server.py)
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

def sub_kind(name):
    base = os.path.basename(name)
    if re.search(r'(?:^|[._\- ])(?:tc|cht|jptc)(?:[._\- ]|$)', base, re.I):
        return "tc"
    if re.search(r'(?:^|[._\- ])(?:sc|chs|jpsc)(?:[._\- ]|$)', base, re.I):
        return "sc"
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

def history_list():
    items = []
    try:
        dirs = [d for d in os.listdir(JOBS_DIR) if os.path.isdir(os.path.join(JOBS_DIR, d))]
        dirs.sort(key=lambda x: os.path.getmtime(os.path.join(JOBS_DIR, x)), reverse=True)
        for d in dirs[:60]:
            jd = os.path.join(JOBS_DIR, d)
            try:
                with open(os.path.join(jd, "params.json"), encoding="utf-8") as f:
                    p = json.load(f)
            except Exception:
                continue
            tracks = p.get("tracks")
            items_data = p.get("items") or []
            if tracks:
                typ = "提取"
                video = p.get("video", "")
            elif len(items_data) > 1:
                typ = "批量"
                video = "、".join(os.path.basename(it.get("video", "")) for it in items_data[:3])
            else:
                typ = "封装"
                it = items_data[0] if items_data else {}
                video = it.get("video", "")
            status = "done"
            try:
                with open(os.path.join(jd, "state.json"), encoding="utf-8") as f:
                    st = json.load(f)
                status = {"done": "done", "error": "error", "killed": "killed"}.get(st.get("status"), "done")
            except Exception:
                for fn in os.listdir(jd):
                    if fn.startswith("item_") and fn.endswith(".log"):
                        txt = read_tail(os.path.join(jd, fn), 8000)
                        if "FAIL:" in txt or "SERVER ERROR" in txt:
                            status = "error"
                            break
            items.append({"id": d, "type": typ, "video": video, "status": status,
                          "time": int(os.path.getmtime(jd) * 1000)})
    except Exception:
        pass
    return {"items": items}

def history_log(jid):
    if not re.fullmatch(r"[0-9a-f]{12}", jid):
        return {"error": "任务不存在"}
    jd = os.path.join(JOBS_DIR, jid)
    if not os.path.isdir(jd):
        return {"error": "任务不存在"}
    parts = []
    for fn in sorted(os.listdir(jd)):
        if fn.startswith("item_") and fn.endswith(".log"):
            parts.append("---- " + fn + " ----\n" + read_tail(os.path.join(jd, fn), 8000))
    return {"log": "\n".join(parts)}

def rerun(jid):
    if not re.fullmatch(r"[0-9a-f]{12}", jid):
        return {"error": "任务不存在"}
    jd = os.path.join(JOBS_DIR, jid)
    if not os.path.isdir(jd):
        return {"error": "任务不存在"}
    try:
        with open(os.path.join(jd, "params.json"), encoding="utf-8") as f:
            p = json.load(f)
    except Exception as ex:
        return {"error": "参数读取失败: %s" % ex}
    if p.get("tracks"):
        return extract_subs(p.get("video", ""), p.get("tracks") or [], p.get("out_dir", ""))
    body = dict(p.get("common") or {})
    body["items"] = p.get("items") or []
    for it in body["items"]:
        if it.get("video") and not (it.get("sc_sub") or it.get("sc") or it.get("tc_sub") or it.get("tc")):
            m = match_subs(it["video"])
            if m.get("sc"):
                it["sc_sub"] = m["sc"]
            if m.get("tc"):
                it["tc_sub"] = m["tc"]
    return start_batch(body)

def stop_job(jid):
    st = JOBS.get(jid)
    if not st:
        return {"error": "任务不存在"}
    if st.get("status") != "running":
        return {"ok": True, "status": st.get("status")}
    st["stopped"] = True
    st["stop_requested"] = True
    proc = st.get("proc")
    if proc and proc.poll() is None:
        _kill_tree(proc)
    return {"ok": True}

def open_path(path):
    if not path or not os.path.exists(path):
        return {"error": "路径不存在"}
    try:
        subprocess.Popen(["explorer", "/select,", path])
        return {"ok": True}
    except Exception as ex:
        return {"error": str(ex)}

# ---------------- http ----------------

class H(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass
    def _send(self, code, body, ctype="application/json; charset=utf-8"):
        data = body if isinstance(body, bytes) else json.dumps(body, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(data)
    def _origin_ok(self):
        origin = self.headers.get("Origin")
        if not origin:
            return True
        try:
            o = urlparse(origin)
            return bool(o.netloc) and o.netloc == self.headers.get("Host", "")
        except Exception:
            return False
    def do_GET(self):
        u = urlparse(self.path)
        if u.path.startswith("/api/") and u.path != "/api/version" and not self._origin_ok():
            self._send(403, {"error": "forbidden"})
            return
        if u.path == "/favicon.ico":
            self.send_response(204)
            self.end_headers()
            return
        if u.path in ("/", "/index.html"):
            try:
                with open(os.path.join(BASE, "app", "index.html"), "rb") as f:
                    self._send(200, f.read(), "text/html; charset=utf-8")
            except Exception as ex:
                self._send(500, {"error": str(ex)})
            return
        if u.path == "/api/list":
            q = parse_qs(u.query)
            self._send(200, list_dir(q.get("path", [""])[0]))
            return
        if u.path == "/api/probe":
            q = parse_qs(u.query)
            self._send(200, probe(q.get("path", [""])[0]))
            return
        if u.path == "/api/job":
            q = parse_qs(u.query)
            self._send(200, job_status(q.get("id", [""])[0]))
            return
        if u.path == "/api/file":
            q = parse_qs(u.query)
            name = q.get("path", [""])[0].split("?")[0]
            fp = os.path.abspath(os.path.join(PREVIEW_DIR, os.path.basename(name)))
            if not fp.startswith(os.path.abspath(PREVIEW_DIR)) or not os.path.exists(fp):
                self._send(404, {"error": "not found"})
                return
            ext = ext_of(fp)
            ctype = "image/png" if ext == ".png" else "image/jpeg" if ext in (".jpg", ".jpeg") else "application/octet-stream"
            with open(fp, "rb") as f:
                self._send(200, f.read(), ctype)
            return
        if u.path == "/api/history":
            q = parse_qs(u.query)
            jid = q.get("id", [""])[0]
            self._send(200, history_log(jid) if jid else history_list())
            return
        if u.path == "/api/match_subs":
            q = parse_qs(u.query)
            self._send(200, match_subs(q.get("path", [""])[0]))
            return
        if u.path == "/api/open":
            q = parse_qs(u.query)
            self._send(200, open_path(q.get("path", [""])[0]))
            return
        if u.path == "/api/version":
            self._send(200, {"ok": True, "time": time.time()})
            return
        if u.path == "/api/config":
            self._send(200, {"scan_root": CONFIG["scan_root"],
                             "valid": os.path.isdir(CONFIG["scan_root"]),
                             "configured": os.path.isfile(CONFIG_PATH)})
            return
        self._send(404, {"error": "not found"})
    def do_POST(self):
        u = urlparse(self.path)
        if not self._origin_ok():
            self._send(403, {"error": "forbidden"})
            return
        n = int(self.headers.get("Content-Length", 0) or 0)
        try:
            body = json.loads(self.rfile.read(n).decode("utf-8")) if n else {}
        except Exception:
            self._send(400, {"error": "bad json"})
            return
        try:
            if u.path == "/api/mux":
                item = dict(body)
                payload = {"items": [item]}
                for k in ("fonts_dir","audio","audio_mode","keep_src_audio","audio_lang","audio_name","out_dir","force","sc_name","tc_name","no_backup","audio_tracks","subtitle_tracks","keep_attachments"):
                    if k in body:
                        payload[k] = body[k]
                self._send(200, start_batch(payload))
                return
            if u.path == "/api/batch":
                self._send(200, start_batch(body))
                return
            if u.path == "/api/prep_subs":
                self._send(200, prep_subs((body.get("sc") or "").strip(), (body.get("tc") or "").strip()))
                return
            if u.path == "/api/check_fonts":
                self._send(200, check_fonts([s for s in (body.get("subs") or []) if s], (body.get("fonts_dir") or "").strip()))
                return
            if u.path == "/api/preview":
                try:
                    tm = float(body.get("time") or 0)
                except (TypeError, ValueError):
                    self._send(400, {"error": "无效的时间参数"})
                    return
                self._send(200, make_preview((body.get("video") or "").strip(), (body.get("sub") or "").strip(),
                                             (body.get("fonts_dir") or "").strip(), tm,
                                             (body.get("mode") or "frame")))
                return
            if u.path == "/api/stop":
                self._send(200, stop_job((body.get("id") or "").strip()))
                return
            if u.path == "/api/rerun":
                self._send(200, rerun((body.get("id") or "").strip()))
                return
            if u.path == "/api/extract":
                self._send(200, extract_subs((body.get("video") or "").strip(),
                                             body.get("tracks") or [],
                                             (body.get("out_dir") or "").strip()))
                return
            if u.path == "/api/drop":
                self._send(200, resolve_drop([s for s in (body.get("names") or []) if s]))
                return
            if u.path == "/api/config":
                p = (body.get("scan_root") or "").strip()
                if not p or not os.path.isdir(p):
                    self._send(200, {"error": "目录不存在: %s" % (p or "(空)")})
                    return
                CONFIG["scan_root"] = p
                try:
                    with open(CONFIG_PATH, "w", encoding="utf-8") as f:
                        json.dump({"scan_root": p}, f, ensure_ascii=False, indent=2)
                except Exception as ex:
                    self._send(200, {"error": "保存失败: %s" % ex})
                    return
                with INDEX_LOCK:
                    INDEX["t"] = 0.0
                self._send(200, {"ok": True, "scan_root": p})
                return
            self._send(404, {"error": "not found"})
        except Exception as ex:
            self._send(500, {"error": str(ex)})

def index_refresher():
    while True:
        try:
            build_index()
        except Exception:
            pass
        time.sleep(300)

def cleaner():
    while True:
        try:
            cutoff = time.time() - 7 * 86400
            for d in (LOG_DIR, PREVIEW_DIR, TMP_DIR):
                for fn in os.listdir(d):
                    fp = os.path.join(d, fn)
                    try:
                        if not os.path.exists(fp):
                            continue
                        if d == TMP_DIR and fn.startswith("fontcheck_"):
                            if os.path.isdir(fp) and os.path.getmtime(fp) < cutoff:
                                shutil.rmtree(fp, ignore_errors=True)
                            continue
                        if d == TMP_DIR and os.path.isfile(fp) and re.match(r'^(?:sc|tc)_.*.ass$', fn, re.I) and os.path.getmtime(fp) < cutoff:
                            os.remove(fp)
                            continue
                        if d != TMP_DIR and os.path.isfile(fp) and os.path.getmtime(fp) < cutoff:
                            os.remove(fp)
                        elif d == PREVIEW_DIR and os.path.isdir(fp) and os.path.getmtime(fp) < cutoff:
                            shutil.rmtree(fp, ignore_errors=True)  # 内封轨道预览抽出的 *_fonts 目录
                    except OSError:
                        pass
        except Exception:
            pass
        time.sleep(6 * 3600)

def main():
    try:
        with urllib.request.urlopen("http://127.0.0.1:8765/api/version", timeout=2) as resp:
            if resp.status == 200:
                try:
                    info = json.loads(resp.read().decode("utf-8") or "{}")
                except Exception:
                    info = {}
                if info.get("ok"):
                    print("Mux UI already running on http://127.0.0.1:8765 (skip start)")
                    return
    except Exception:
        pass
    port = 8765
    srv = None
    for p in range(port, port + 10):
        try:
            srv = ThreadingHTTPServer((HOST, p), H)
            port = p
            break
        except OSError:
            continue
    if not srv:
        print("no free port")
        return
    print("MUX UI running at http://%s:%d" % (HOST, port))
    print("building file index for drag-drop...")
    threading.Thread(target=index_refresher, daemon=True).start()
    threading.Thread(target=cleaner, daemon=True).start()
    srv.serve_forever()

if __name__ == "__main__":
    main()
