# -*- coding: utf-8 -*-
# 共享底座：路径/目录/配置/工具定位/进程管理/日志解码/拖放索引。
# 所有 feature 模块只依赖本文件；新增功能不改这里。
import json, os, re, shutil, subprocess, sys, threading, time, uuid

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))  # 项目根（本文件位于 app/）
APP_DIR = os.path.join(BASE, "app")
TOOLS_DIR = os.path.join(APP_DIR, "tools")  # 可独立执行的脚本/工具（mux_cli/bootstrap/冒烟/自检…）
DATA_DIR = os.path.join(BASE, "data")
# 新写入路径按功能分目录；LEGACY_* 为旧位置，只读兼容（历史文件原地不动）
JOBS_DIR = os.path.join(DATA_DIR, "mux")
LEGACY_JOBS_DIR = os.path.join(DATA_DIR, "jobs")
TMP_DIR = os.path.join(DATA_DIR, "tmp")
PREVIEW_DIR = os.path.join(DATA_DIR, "preview")
LEGACY_PREVIEW_DIR = os.path.join(DATA_DIR, "previews")
LOG_DIR = os.path.join(DATA_DIR, "log")
CONFIG_PATH = os.path.join(APP_DIR, "config.json")

# ---------- 工具定位（自带 bin 优先 -> PATH -> 默认安装路径） ----------
def _first_existing(*paths):
    for p in paths:
        if p and os.path.isfile(p):
            return p
    return None

BIN_DIR = os.path.join(BASE, "bin")

def _locate_tools():
    # AFS 默认后端 PyFontTools 需要 pyftsubset/ttx（fonttools），用当前解释器的 Scripts 目录
    py_scripts = os.path.join(os.path.dirname(sys.executable), "Scripts")
    return {
        "MKVMERGE": _first_existing(os.path.join(BIN_DIR, "mkvtoolnix", "mkvmerge.exe")) or shutil.which("mkvmerge") or "C:/Program Files/MKVToolNix/mkvmerge.exe",
        "MKVEXTRACT": _first_existing(os.path.join(BIN_DIR, "mkvtoolnix", "mkvextract.exe")) or shutil.which("mkvextract") or "C:/Program Files/MKVToolNix/mkvextract.exe",
        "FFMPEG": _first_existing(os.path.join(BIN_DIR, "ffmpeg", "bin", "ffmpeg.exe")) or shutil.which("ffmpeg") or "ffmpeg",
        "ASSFONTS": _first_existing(os.path.join(BIN_DIR, "assfonts", "assfonts.exe")) or shutil.which("assfonts") or os.path.join(BIN_DIR, "assfonts", "assfonts.exe"),
        "AFS": _first_existing(os.path.join(BIN_DIR, "assfontsubset", "AssFontSubset.Console.exe")) or shutil.which("AssFontSubset.Console") or "",
        "PY_SCRIPTS": py_scripts,
        "PYFTSUBSET": _first_existing(os.path.join(py_scripts, "pyftsubset.exe")) or shutil.which("pyftsubset") or "",
    }

# 工具定位在模块加载时执行一次；环境（bin/ 或 PATH）变更后调 refresh_tools() 重算，免重启生效
_tools = _locate_tools()
MKVMERGE, MKVEXTRACT, FFMPEG = _tools["MKVMERGE"], _tools["MKVEXTRACT"], _tools["FFMPEG"]
ASSFONTS, AFS, PY_SCRIPTS, PYFTSUBSET = _tools["ASSFONTS"], _tools["AFS"], _tools["PY_SCRIPTS"], _tools["PYFTSUBSET"]

def refresh_tools():
    """环境安装/变更后重算工具路径（网页内安装完成后调用，免重启生效）。返回新的定位结果。"""
    _t = _locate_tools()
    globals().update(_t)
    return _t

for d in (JOBS_DIR, LEGACY_JOBS_DIR, TMP_DIR, PREVIEW_DIR, LEGACY_PREVIEW_DIR, LOG_DIR):
    os.makedirs(d, exist_ok=True)

# ---------- 配置（工作目录/扫描根 + 子集工具双轨） ----------
DEFAULT_SCAN_ROOT = "D:" + chr(92) + "Video"
CONFIG = {"scan_root": DEFAULT_SCAN_ROOT, "subset_tool": "afs"}

def load_config():
    try:
        with open(CONFIG_PATH, encoding="utf-8") as f:
            c = json.load(f)
        if isinstance(c.get("scan_root"), str) and os.path.isdir(c["scan_root"]):
            CONFIG["scan_root"] = c["scan_root"]
        if c.get("subset_tool") in ("afs", "assfonts"):
            CONFIG["subset_tool"] = c["subset_tool"]
    except Exception:
        pass

def save_config(scan_root=None, subset_tool=None):
    if scan_root:
        CONFIG["scan_root"] = scan_root
    if subset_tool in ("afs", "assfonts"):
        CONFIG["subset_tool"] = subset_tool
    with open(CONFIG_PATH, "w", encoding="utf-8") as f:
        json.dump({"scan_root": CONFIG["scan_root"], "subset_tool": CONFIG["subset_tool"]},
                  f, ensure_ascii=False, indent=2)
    with INDEX_LOCK:
        INDEX["t"] = 0.0  # 扫描根变更后索引失效，下次访问重建

load_config()

# ---------- 任务状态注册表（mux/extract 共享，stop/status 跨功能可见） ----------
JOBS = {}
JOBS_LOCK = threading.Lock()

# ---------- 进程注册表（stop/超时管理） ----------
PROCS = {}
PROCS_LOCK = threading.Lock()

def register_proc(jid, proc):
    with PROCS_LOCK:
        PROCS[jid] = proc

def unregister_proc(jid):
    with PROCS_LOCK:
        PROCS.pop(jid, None)

def get_proc(jid):
    with PROCS_LOCK:
        return PROCS.get(jid)

def _kill_tree(proc):
    try:
        subprocess.Popen(["taskkill", "/T", "/F", "/PID", str(proc.pid)],
                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    except Exception:
        try:
            proc.kill()
        except Exception:
            pass

def run_to_file(cmd, out_path, timeout=1800, cwd=None, jid=None, stop_flag=None):
    parent = os.path.dirname(out_path)
    if parent:
        try:
            os.makedirs(parent, exist_ok=True)
        except OSError:
            pass
    with open(out_path, "wb") as f:
        proc = subprocess.Popen(cmd, stdout=f, stderr=subprocess.STDOUT, cwd=cwd)
    if jid:
        register_proc(jid, proc)
    try:
        if stop_flag is not None and stop_flag.is_set():
            _kill_tree(proc)
            proc.wait()
            return 1
        proc.wait(timeout=timeout)
        return proc.returncode
    except subprocess.TimeoutExpired:
        _kill_tree(proc)
        proc.wait()
        try:
            with open(out_path, "a", encoding="utf-8", errors="replace") as f:
                f.write(chr(10) + "TIMEOUT: 任务超时已终止" + chr(10))
        except Exception:
            pass
        return 1
    finally:
        if jid:
            unregister_proc(jid)

def decode_log(data):
    # UTF-16 BOM 检测用字节序列（避免源码里的转义字节字面量）
    if data[:2] in (bytes([255, 254]), bytes([254, 255])):
        return data.decode("utf-16", errors="replace")
    try:
        return data.decode("utf-8")
    except UnicodeDecodeError:
        return data.decode("gbk", errors="replace")

def read_tail(path, n=300):
    try:
        with open(path, "rb") as f:
            data = f.read()
        return chr(10).join(decode_log(data).splitlines()[-n:])
    except Exception:
        return ""

def ext_of(p):
    return os.path.splitext(p)[1].lower()

# ---------- 拖放索引 ----------
INDEX = {"t": 0.0, "map": {}}
INDEX_LOCK = threading.Lock()

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
                    dns[:] = [d for d in dns if d not in ("jobs", "tmp", "previews", "bin", "data", "docs", "scripts", "app") and not d.startswith("__")]
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

# ---------- 分类扩展名 ----------
VIDEO_EXT = {'.mkv', '.mp4', '.m2ts', '.ts', '.avi', '.mov', '.webm', '.flv', '.wmv', '.m4v'}
SUB_EXT = {'.ass', '.ssa', '.srt'}
FONT_EXT = {'.ttf', '.otf', '.ttc', '.otc', '.woff', '.woff2'}
AUDIO_EXT = {'.mka', '.flac', '.aac', '.m4a', '.mp3', '.opus', '.ogg', '.wav', '.ac3', '.dts', '.eac3'}

# ---------- 后台维护线程（索引定期刷新 + 过期文件清理，server 启动时调用一次） ----------
def _index_refresher():
    while True:
        try:
            build_index()
        except Exception:
            pass
        time.sleep(300)

def _clean_one_dir(d, cutoff):
    try:
        names = os.listdir(d)
    except OSError:
        return
    for fn in names:
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
            elif d in (PREVIEW_DIR, LEGACY_PREVIEW_DIR) and os.path.isdir(fp) and os.path.getmtime(fp) < cutoff:
                shutil.rmtree(fp, ignore_errors=True)  # 内封轨道预览抽出的 *_fonts 目录
        except OSError:
            pass

# ---------- 历史任务目录保留策略 ----------
# 任务目录（data/mux + 旧 data/jobs）含 params.json/state.json/item_*.log，
# /api/history 只展示最近 60 条，但目录会无限累积 —— 这里按 mtime 清理：
#   超过 retention_days 天的整目录删除；无论多旧，最近 keep_min 条始终保留。
def _clean_job_dirs(retention_days=30, keep_min=100):
    cutoff = time.time() - retention_days * 86400
    for base in (JOBS_DIR, LEGACY_JOBS_DIR):
        try:
            dirs = [os.path.join(base, d) for d in os.listdir(base)
                    if os.path.isdir(os.path.join(base, d))]
        except OSError:
            continue
        dirs.sort(key=lambda x: os.path.getmtime(x), reverse=True)
        for jd in dirs[keep_min:]:  # 最新 keep_min 条不动
            try:
                if os.path.getmtime(jd) < cutoff:
                    shutil.rmtree(jd, ignore_errors=True)
            except OSError:
                pass

def _cleaner():
    while True:
        try:
            cutoff = time.time() - 7 * 86400
            for d in (LOG_DIR, PREVIEW_DIR, LEGACY_PREVIEW_DIR, TMP_DIR):
                _clean_one_dir(d, cutoff)
            _clean_job_dirs()  # 历史任务：30 天 + 至少留 100 条
        except Exception:
            pass
        time.sleep(6 * 3600)

def start_background():
    threading.Thread(target=_index_refresher, daemon=True).start()
    threading.Thread(target=_cleaner, daemon=True).start()
