# -*- coding: utf-8 -*-
# 环境域：网页内环境检测与安装。
#   GET  /api/env_check           检测 mkvmerge/mkvextract/ffmpeg/assfonts/AFS/fonttools
#   POST /api/env_install         触发安装缺失组件（后台线程，立即返回任务 id）
#   GET  /api/env_install?id=xxx  查询安装进度与日志
# 安装逻辑复用 app/tools/bootstrap.py 的 run_bootstrap()（与 CLI 同一份实现）。
import os, shutil, threading, uuid
from app import core

# ---------------- 检测 ----------------

def _find(rel_paths, which_name, default_paths=()):
    for rel in rel_paths:
        p = os.path.join(core.BIN_DIR, rel)
        if os.path.isfile(p):
            return p
    w = shutil.which(which_name)
    if w:
        return w
    for d in default_paths:
        if os.path.isfile(d):
            return d
    return None

def check_env():
    mkvmerge   = _find(["mkvtoolnix/mkvmerge.exe"], "mkvmerge", [r"C:\Program Files\MKVToolNix\mkvmerge.exe"])
    mkvextract = _find(["mkvtoolnix/mkvextract.exe"], "mkvextract", [r"C:\Program Files\MKVToolNix\mkvextract.exe"])
    ffmpeg     = _find(["ffmpeg/bin/ffmpeg.exe"], "ffmpeg")
    assfonts   = _find(["assfonts/assfonts.exe"], "assfonts")
    afs        = _find(["assfontsubset/AssFontSubset.Console.exe"], "AssFontSubset.Console")
    try:
        import fontTools
        ft = getattr(fontTools, "version", "") or "已安装"
    except ImportError:
        ft = None

    items = [
        {"key": "mkvtoolnix", "name": "MKVToolNix (mkvmerge)", "status": "ok" if mkvmerge else "missing",
         "path": mkvmerge or "", "essential": True, "install": "mkvtoolnix", "hint": "封装核心工具"},
        {"key": "mkvextract", "name": "MKVToolNix (mkvextract)", "status": "ok" if mkvextract else "missing",
         "path": mkvextract or "", "essential": True, "install": "mkvtoolnix", "hint": "字幕提取"},
        {"key": "ffmpeg", "name": "ffmpeg (含 libass)", "status": "ok" if ffmpeg else "missing",
         "path": ffmpeg or "", "essential": True, "install": "ffmpeg", "hint": "预览帧 / 连拍必需"},
        {"key": "afs", "name": "AssFontSubset（主用子集工具）", "status": "ok" if afs else "missing",
         "path": afs or "", "essential": False, "install": "assfontsubset", "hint": "自动修正字幕字体名"},
        {"key": "assfonts", "name": "assfonts（回退子集工具）", "status": "ok" if assfonts else "missing",
         "path": assfonts or "", "essential": False, "install": "assfonts", "hint": "AFS 不可用时的后备"},
        {"key": "fonttools", "name": "fonttools (AFS 后端)", "status": "ok" if ft else "missing",
         "path": ("v" + ft) if ft else "", "essential": False, "install": "fonttools", "hint": "AFS 默认后端依赖"},
    ]
    critical = [i for i in items if i["essential"] and i["status"] == "missing"]
    subset_ok = any(i["status"] == "ok" for i in items if i["key"] in ("afs", "assfonts"))
    if critical or not subset_ok:
        overall = "broken"    # 关键工具缺失或子集工具全缺 → 无法正常工作
    elif any(i["status"] == "missing" for i in items):
        overall = "partial"   # 非关键项缺失（如只缺 assfonts 但有 AFS）
    else:
        overall = "ready"
    missing = sorted({i["install"] for i in items if i["status"] == "missing"})
    return {"ok": True, "overall": overall, "items": items, "missing": missing}

def handle_env_check(q):
    return check_env()

# ---------------- 安装（后台线程 + 日志累积） ----------------

INSTALL = {}
INSTALL_LOCK = threading.Lock()
_INSTALLING = {"v": False}

def _worker(jid, proxy, keys):
    st = INSTALL[jid]
    log = st["log"]
    try:
        from app.tools import bootstrap
        res = bootstrap.run_bootstrap(proxy=proxy, items=keys, log=lambda s: log.append(s))
        st["ok"] = not res["fail"]
        st["fail"] = res["fail"]
    except Exception as ex:
        log.append("FAIL: %s" % ex)
        st["ok"] = False
    finally:
        st["done"] = True
        _INSTALLING["v"] = False
        try:
            core.refresh_tools()   # 安装完成重算工具路径，免重启生效
        except Exception:
            pass

def handle_install_post(body):
    if _INSTALLING["v"]:
        return {"error": "已有安装任务在进行中，请等待完成"}
    proxy = (body.get("proxy") or "").strip()
    want = body.get("items") or None
    env = check_env()
    keys = env["missing"] if not want else [k for k in want if k in env["missing"]]
    if not keys:
        return {"ok": True, "id": "", "items": [], "note": "没有需要安装的组件"}
    jid = uuid.uuid4().hex[:12]
    _INSTALLING["v"] = True
    with INSTALL_LOCK:
        INSTALL[jid] = {"log": [], "done": False, "ok": None, "fail": [], "items": keys}
    threading.Thread(target=_worker, args=(jid, proxy, keys), daemon=True).start()
    return {"ok": True, "id": jid, "items": keys}

def handle_install_get(q):
    jid = (q.get("id") or [""])[0]
    st = INSTALL.get(jid)
    if not st:
        return {"error": "未找到安装任务"}
    return {"ok": True, "id": jid, "done": st["done"], "ok": st["ok"],
            "fail": st["fail"], "items": st["items"], "log": "\n".join(st["log"])}

handlers = {
    "GET": {"/api/env_check": handle_env_check, "/api/env_install": handle_install_get},
    "POST": {"/api/env_install": handle_install_post},
}
