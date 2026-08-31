# -*- coding: utf-8 -*-
# 封装编排：POST /api/mux（单任务）、POST /api/batch（批量）、GET /api/job、POST /api/stop、POST /api/rerun、GET /api/history
# 单任务实际执行由 app/tools/mux_cli.py 完成（Python 编排，PowerShell 已退役）
import json, os, re, sys, threading, time, uuid
from app import core
from app.features import tracks as tracks_mod
from app.tools import mux_cli

MUX_CLI = os.path.join(core.TOOLS_DIR, "mux_cli.py")

COMMON_KEYS = ("fonts_dir", "audio", "audio_mode", "keep_src_audio", "audio_lang", "audio_name",
               "out_dir", "force", "sc_name", "tc_name", "sc_lang", "tc_lang", "no_backup", "audio_tracks",
               "subtitle_tracks", "keep_attachments", "sc_default", "tc_default",
               "sc_forced", "tc_forced", "chapters", "out_name", "title", "fonts_mode", "skip_existing")

# ---------------- 命令构造 ----------------

def display_cmd(cmd):
    """命令列表 -> 可复制执行的命令行（含空格的参数加引号）。"""
    return " ".join(('"%s"' % c) if (" " in c) else c for c in cmd)

def build_cmd(it, common):
    full = dict(common)
    for k, v in it.items():
        if v:
            full[k] = v
    video = full.get("video", "")
    cmd = [sys.executable, MUX_CLI, "--video", video]
    def add(k, v):
        if v is None:
            v = ""
        elif not isinstance(v, str):
            v = str(v)
        v = v.strip()
        if v:
            cmd.append(k)
            cmd.append(v)
    add("--sc-sub", full.get("sc_sub") or full.get("sc"))
    add("--tc-sub", full.get("tc_sub") or full.get("tc"))
    add("--fonts-dir", full.get("fonts_dir"))
    add("--sc-name", full.get("sc_name"))
    add("--tc-name", full.get("tc_name"))
    add("--sc-lang", full.get("sc_lang"))
    add("--tc-lang", full.get("tc_lang"))
    audio_tracks = full.get("audio_tracks")
    if not audio_tracks and full.get("audio_mode") == "none":
        audio_tracks = "none"
    add("--audio-tracks", audio_tracks)
    add("--audio", full.get("audio"))
    add("--audio-lang", full.get("audio_lang"))
    add("--audio-name", full.get("audio_name"))
    add("--out-dir", full.get("out_dir"))
    if full.get("force"):
        cmd.append("--force")
    if full.get("no_backup"):
        cmd.append("--no-backup")
    add("--subtitle-tracks", full.get("subtitle_tracks"))
    if full.get("keep_attachments"):
        cmd.append("--keep-attachments")
    # 轨道旗标：default 允许显式覆盖（"0"/"1"），空 = CLI 端自动
    add("--sc-default", full.get("sc_default"))
    add("--tc-default", full.get("tc_default"))
    if full.get("sc_forced"):
        cmd.append("--sc-forced")
    if full.get("tc_forced"):
        cmd.append("--tc-forced")
    add("--chapters", full.get("chapters"))
    add("--out-name", full.get("out_name"))
    add("--title", full.get("title"))
    if full.get("fonts_mode") in ("subset", "collect"):
        cmd += ["--fonts-mode", full.get("fonts_mode")]
    return cmd

# ---------------- 任务生命周期 ----------------

def _fail_reason(txt):
    """任务日志里最后一条 FAIL: 行 = mux_cli 写下的最贴近根因的失败原因。"""
    m = re.findall(r"^FAIL: (.+)$", txt or "", re.M)
    return m[-1].strip() if m else ""

def _qc_from_log(txt):
    """从 item 日志解析 mux_cli 的 QC 输出（QC: / QC-WARN: / FAIL: QC 失败：）。"""
    hard = re.findall(r"^FAIL: QC 失败：(.+)$", txt or "", re.M)
    warn = re.findall(r"^QC-WARN: (.+)$", txt or "", re.M)
    ok = re.findall(r"^QC: (.+)$", txt or "", re.M)
    if hard:
        return {"status": "fail", "hard": hard, "warn": warn}
    if warn:
        return {"status": "warn", "warn": warn, "ok": ok}
    if ok:
        return {"status": "ok", "ok": ok}
    return None

def _qc_summary(state):
    qc_list = state.get("qc_list") or []
    if not qc_list:
        return None
    s = {"total": len(qc_list), "ok": 0, "warn": 0, "fail": 0}
    for e in qc_list:
        st = (e.get("qc") or {}).get("status")
        s[st if st in ("ok", "warn", "fail") else "warn"] += 1
    return s

def _finalize_job(state):
    jid, jdir = state["id"], state["dir"]
    if state.get("stopped"):
        state["status"] = "killed"
        state["exit"] = -1
    else:
        state["status"] = "done" if state["failed"] == 0 else "error"
        state["exit"] = 0 if state["failed"] == 0 else 1
    try:   # QC 报告落盘（逐集 QC 结论，发布前汇总用）
        if state.get("qc_list"):
            with open(os.path.join(jdir, "qc_report.json"), "w", encoding="utf-8") as f:
                json.dump({"items": state["qc_list"], "summary": _qc_summary(state)}, f, ensure_ascii=False, indent=2)
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
        parts = []
        for i in range(1, state.get("total", 1) + 1):
            log = os.path.join(jdir, "item_%02d.log" % i)
            tl = core.read_tail(log, 60)
            if tl:
                parts.append("---- item %d ----\n%s" % (i, tl))
        with open(os.path.join(core.LOG_DIR, "job_%s.log" % jid), "w", encoding="utf-8", errors="replace") as f:
            f.write("\n".join(parts))
    except Exception:
        pass

def _another_running():
    with core.JOBS_LOCK:
        return any(s.get("status") == "running" for s in core.JOBS.values())

def start_batch(body):
    items = body.get("items") or []
    if not items:
        return {"error": "没有任务项"}
    for it in items:
        if not it.get("video") or not os.path.exists(it.get("video")):
            return {"error": "存在无效的视频路径: %s" % it.get("video")}
    if _another_running():
        return {"error": "另一个任务正在运行，请等待完成"}
    common = {k: body.get(k) for k in COMMON_KEYS}
    if (common.get("out_dir") or "").strip():
        base_names = [os.path.basename(it.get("video", "")) for it in items]
        lower_names = [n.lower() for n in base_names]
        dup = sorted({n for n in base_names if lower_names.count(n.lower()) > 1})
        if dup:
            return {"error": "输出同名冲突，已拒绝提交: %s" % "、".join(dup)}
    jid = uuid.uuid4().hex[:12]
    jdir = os.path.join(core.JOBS_DIR, jid)
    os.makedirs(jdir)
    with open(os.path.join(jdir, "params.json"), "w", encoding="utf-8") as f:
        json.dump({"common": common, "items": items}, f, ensure_ascii=False, indent=2)
    first = items[0]
    fv = first.get("video", "")
    out_dir = (common.get("out_dir") or "").strip() or os.path.dirname(fv)
    result = os.path.join(out_dir, os.path.splitext(os.path.basename(fv))[0] + os.path.splitext(fv)[1]) if fv else ""
    state = {"id": jid, "dir": jdir, "status": "running", "exit": None,
             "started": time.time(), "current": 0, "total": len(items),
             "current_video": "", "item_status": "", "failed": 0, "results": [], "result": result,
             "stop_event": threading.Event()}

    def worker():
        for i, it in enumerate(items):
            if state.get("stopped"):
                break
            state["current"] = i + 1
            state["item_status"] = "running"
            state["current_video"] = it.get("video", "")
            log = os.path.join(jdir, "item_%02d.log" % (i + 1))
            try:
                cmd = build_cmd(it, common)
                state["last_cmd"] = display_cmd(cmd)   # 实际执行的封装命令（可复现/进流水线）
                od = (common.get("out_dir") or "").strip()
                out_path = os.path.join(od, os.path.basename(it.get("video", ""))) if od else it.get("video", "")
                # 跳过已存在输出（仅输出目录模式；替换模式的目标即源文件，恒存在无意义）
                if od and common.get("skip_existing") and os.path.isfile(out_path):
                    state["results"].append({"video": it.get("video", ""), "output": out_path,
                                             "ok": True, "exit": 0, "skipped": True,
                                             "reason": "输出已存在，跳过", "cmd": ""})
                    continue
                rc = core.run_to_file(cmd, log, jid=jid, stop_flag=state["stop_event"])
                reason = "" if (rc == 0 or state.get("stopped")) else _fail_reason(core.read_tail(log, 200))
                qc = _qc_from_log(core.read_tail(log, 200))
                if qc:
                    state.setdefault("qc_list", []).append({"video": it.get("video", ""), "qc": qc})
                state["results"].append({"video": it.get("video", ""), "output": out_path,
                                         "ok": rc == 0 and not state.get("stopped"), "exit": rc,
                                         "reason": reason, "cmd": state["last_cmd"], "qc": qc})
                if rc != 0 and not state.get("stopped"):
                    state["failed"] += 1
            except Exception as ex:
                state["results"].append({"video": it.get("video", ""), "ok": False, "exit": -1,
                                         "reason": "服务端编排异常: %s" % ex})
                state["failed"] += 1
                try:
                    with open(log, "a", encoding="utf-8", errors="replace") as f:
                        f.write("\nSERVER ERROR: %s\n" % ex)
                except Exception:
                    pass
            state["item_status"] = "done"
        _finalize_job(state)

    with core.JOBS_LOCK:
        if any(s.get("status") == "running" for s in core.JOBS.values()):
            return {"error": "另一个任务正在运行，请等待完成"}
        core.JOBS[jid] = state
        threading.Thread(target=worker, daemon=True).start()
    return {"job": jid}

def job_status(jid):
    s = core.JOBS.get(jid)
    if not s:
        return {"error": "unknown job"}
    # merge logs: current item full + previous items tails
    parts = []
    total_items = s.get("total", 1)
    cur = s.get("current", 0)
    for i in range(1, min(cur + 1, total_items + 1)):
        log = os.path.join(s["dir"], "item_%02d.log" % i)
        if i == cur and s.get("item_status") == "running":
            parts.append(core.read_tail(log, 300))
        else:
            tl = core.read_tail(log, 25)
            if tl:
                parts.append("---- item %d ----\n%s" % (i, tl))
    merged = "\n".join(parts)
    progress = s.get("progress")   # 显式进度优先（预览连拍直报）；缺省回落到日志解析
    if progress is None:
        m = re.findall(r'(?:进度|Progress)[:：]\s*(\d+)%', merged)
        if m:
            progress = int(m[-1])
    return {"id": s["id"], "status": s["status"], "exit": s["exit"],
            "current": cur, "total": total_items, "failed": s.get("failed", 0),
            "current_video": s.get("current_video", ""), "progress": progress,
            "results": s.get("results", []), "result": s.get("result", ""), "log": merged,
            "reason": _fail_reason(merged), "cmd": s.get("last_cmd", ""), "qc": s.get("results", [{}])[-1].get("qc") if s.get("results") else None,
            "qc_summary": _qc_summary(s)}

def stop_job(jid):
    st = core.JOBS.get(jid)
    if not st:
        return {"error": "任务不存在"}
    if st.get("status") != "running":
        return {"ok": True, "status": st.get("status")}
    st["stopped"] = True
    ev = st.get("stop_event")
    if ev is not None:
        ev.set()
    proc = core.get_proc(jid)
    if proc and proc.poll() is None:
        core._kill_tree(proc)
    return {"ok": True}

# ---------------- 历史（新 data/mux + 旧 data/jobs 合并读取） ----------------

def _job_dirs():
    out = []
    for base in (core.JOBS_DIR, core.LEGACY_JOBS_DIR):
        try:
            for d in os.listdir(base):
                jd = os.path.join(base, d)
                if os.path.isdir(jd):
                    out.append(jd)
        except OSError:
            pass
    out.sort(key=lambda x: os.path.getmtime(x), reverse=True)
    return out

def _find_job_dir(jid):
    if not re.fullmatch(r"[0-9a-f]{12}", jid):
        return ""
    for base in (core.JOBS_DIR, core.LEGACY_JOBS_DIR):
        jd = os.path.join(base, jid)
        if os.path.isdir(jd):
            return jd
    return ""

def history_list():
    items = []
    try:
        for jd in _job_dirs()[:60]:
            d = os.path.basename(jd)
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
                        txt = core.read_tail(os.path.join(jd, fn), 8000)
                        if "FAIL:" in txt or "SERVER ERROR" in txt:
                            status = "error"
                            break
            items.append({"id": d, "type": typ, "video": video, "status": status,
                          "time": int(os.path.getmtime(jd) * 1000)})
    except Exception:
        pass
    return {"items": items}

def history_log(jid):
    jd = _find_job_dir(jid)
    if not jd:
        return {"error": "任务不存在"}
    parts = []
    for fn in sorted(os.listdir(jd)):
        if fn.startswith("item_") and fn.endswith(".log"):
            parts.append("---- " + fn + " ----\n" + core.read_tail(os.path.join(jd, fn), 8000))
    return {"log": "\n".join(parts)}

def rerun(jid):
    jd = _find_job_dir(jid)
    if not jd:
        return {"error": "任务不存在"}
    try:
        with open(os.path.join(jd, "params.json"), encoding="utf-8") as f:
            p = json.load(f)
    except Exception as ex:
        return {"error": "参数读取失败: %s" % ex}
    if p.get("tracks"):
        from app.features import extract as extract_mod
        return extract_mod.extract_subs(p.get("video", ""), p.get("tracks") or [], p.get("out_dir", ""))
    body = dict(p.get("common") or {})
    body["items"] = p.get("items") or []
    for it in body["items"]:
        if it.get("video") and not (it.get("sc_sub") or it.get("sc") or it.get("tc_sub") or it.get("tc")):
            m = tracks_mod.match_subs(it["video"])
            if m.get("sc"):
                it["sc_sub"] = m["sc"]
            if m.get("tc"):
                it["tc_sub"] = m["tc"]
    return start_batch(body)

# ---------------- 路由 ----------------

def handle_mux(body):
    item = dict(body)
    payload = {"items": [item]}
    for k in COMMON_KEYS:
        if k in body:
            payload[k] = body[k]
    return start_batch(payload)

def handle_batch(body):
    return start_batch(body)

def handle_job(q):
    return job_status((q.get("id") or [""])[0])

def handle_stop(body):
    return stop_job((body.get("id") or "").strip())

def handle_rerun(body):
    return rerun((body.get("id") or "").strip())

def handle_history(q):
    jid = (q.get("id") or [""])[0]
    return history_log(jid) if jid else history_list()

# ---------------- 输出名预览（POST /api/out_preview） ----------------
# 复用 mux_cli.resolve_out_name —— 与实际封装完全同一套模板/集数/非法字符规则，杜绝两套逻辑漂移。
_height_cache = {}   # video -> 分辨率高度（probe 结果缓存，避免预览时反复跑 mkvmerge -J）

def _video_height(video):
    if video in _height_cache:
        return _height_cache[video]
    try:
        d = tracks_mod.probe(video)
        h = int((d or {}).get("video_height") or 0)
    except Exception:
        h = 0
    if h:
        _height_cache[video] = h
    return h

def handle_out_preview(body):
    video = (body.get("video") or "").strip()
    tmpl = (body.get("template") or "").strip()
    title = (body.get("title") or "").strip()
    out_dir = (body.get("out_dir") or "").strip()
    if not video:
        return {"error": "未选择视频"}
    if not os.path.exists(video):
        return {"error": "视频文件不存在"}
    base = os.path.splitext(os.path.basename(video))[0]
    ext = os.path.splitext(video)[1] or ".mkv"
    height = int(body.get("height") or 0) or _video_height(video)
    unresolved_res = bool(tmpl and ("{res}" in tmpl) and not height)
    name = mux_cli.resolve_out_name(tmpl, base, height, title) if tmpl else base
    d = out_dir if out_dir else os.path.dirname(video)
    full = os.path.join(d, name + ext)
    replace = not out_dir   # 输出目录为空 = 替换源视频（现有业务行为）
    exists = (not replace) and os.path.isfile(full)
    return {"name": name + ext, "dir": d, "full": full, "replace": replace,
            "exists": exists, "unresolved_res": unresolved_res}

# ---------------- 封装前文件系统检查（POST /api/preflight_fs） ----------------
# 纯 os.path 检查，无昂贵操作：视频/字幕可访问性、输出目录存在与可写。None = 未提供（跳过）。

def _file_ok(p):
    p = (p or "").strip()
    return os.path.isfile(p) if p else None

def handle_preflight_fs(body):
    out_dir = (body.get("out_dir") or "").strip()
    d = {
        "video_ok": _file_ok(body.get("video")),
        "sc_ok": _file_ok(body.get("sc")),
        "tc_ok": _file_ok(body.get("tc")),
    }
    if out_dir:
        d["out_dir_ok"] = os.path.isdir(out_dir)
        d["out_dir_writable"] = os.access(out_dir, os.W_OK) if d["out_dir_ok"] else False
    return d

handlers = {
    "GET": {"/api/job": handle_job, "/api/history": handle_history},
    "POST": {"/api/mux": handle_mux, "/api/batch": handle_batch,
             "/api/stop": handle_stop, "/api/rerun": handle_rerun,
             "/api/out_preview": handle_out_preview,
             "/api/preflight_fs": handle_preflight_fs},
}
