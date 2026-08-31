# -*- coding: utf-8 -*-
# 独立子集化：POST /api/subset_run（不封装，把 ASS 字幕做字体子集化输出到用户指定目录）
# GET /api/subset_status?id= 轮询进度/结果；POST /api/subset_stop?id= 终止（kill 进程树）。
# 双轨同 fonts.py 先例：AFS 主用（产物=修正后同名 ASS + 子集字体）；assfonts 回退（仅子集字体）。
# 会话临时目录放在 core.TMP_DIR 下且用 fontcheck_ 前缀（对齐 core._clean_one_dir 清理白名单，
# 随临时区周期回收）；绝不删除/清空用户输出目录。
import os, re, threading, time, uuid
from app import core
from app.features.fonts import _parse_afs_missing
from app.tools.font_sources import build_merged_font_dir


def _assfonts_missing(txt):
    """assfonts 日志缺字解析（与 fonts.check_fonts_assfonts 同口径）。"""
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
            item = name or "未命名字体缺字形"
            if item not in missing:
                missing.append(item)
    return missing


def _same_dir(a, b):
    """目录同判：abspath 归一 + Windows 大小写不敏感（normcase）。"""
    try:
        return os.path.normcase(os.path.abspath(a)) == os.path.normcase(os.path.abspath(b))
    except Exception:
        return False


def start_subset(subs, fonts_dir, out_dir, use_sys_fonts):
    subs = [s.strip() for s in (subs or []) if s and s.strip()]
    fonts_dir = (fonts_dir or "").strip()
    out_dir = (out_dir or "").strip()
    if not subs:
        return {"error": "请先提供字幕文件（每行一个）"}
    for s in subs:
        if not os.path.isfile(s):
            return {"error": "字幕文件不存在: %s" % s}
    if not fonts_dir or not os.path.isdir(fonts_dir):
        return {"error": "字体目录不存在"}
    if not out_dir:
        return {"error": "请填写输出目录"}
    if _same_dir(out_dir, fonts_dir):
        return {"error": "输出目录不能与字体目录相同（避免污染字体库）"}
    with core.JOBS_LOCK:
        if any(js.get("status") == "running" for js in core.JOBS.values()):
            return {"error": "另一个任务正在运行，请等待完成"}
    # 会话目录前缀对齐 core._clean_one_dir 白名单（fontcheck_），随临时区周期清理
    jid = uuid.uuid4().hex[:12]
    jdir = os.path.join(core.TMP_DIR, "fontcheck_subset_" + jid)
    os.makedirs(jdir)
    tool = "afs" if (core.CONFIG.get("subset_tool") == "afs" and core.AFS) else "assfonts"
    state = {"id": jid, "dir": jdir, "status": "running", "exit": None, "started": time.time(),
             "current": 0, "total": len(subs), "current_video": "准备中", "progress": 0,
             "failed": 0, "results": [], "result": "", "tool": tool, "log": "",
             "stop_event": threading.Event()}

    def _item_log(i, tag):
        return os.path.join(jdir, "%s_%02d.log" % (tag, i))

    def _append_log(name, path):
        state["log"] = (state["log"] + ("\n" if state["log"] else "") +
                        "---- " + name + " ----\n" + core.read_tail(path, 300))

    def worker():
        try:
            fdir = fonts_dir
            if use_sys_fonts:
                state["current_video"] = "合并系统字体目录…"
                fdir = build_merged_font_dir(fonts_dir, True, jdir)
            dbdir = ""
            if tool == "assfonts":
                dbdir = os.path.join(jdir, "db")
                os.makedirs(dbdir)
                blog = _item_log(0, "build")
                rc = core.run_to_file([core.ASSFONTS, "-f", fdir, "-b", "-d", dbdir], blog,
                                      timeout=300, jid=jid, stop_flag=state["stop_event"])
                _append_log("assfonts 建库", blog)
                if rc != 0 or not os.path.exists(os.path.join(dbdir, "fonts.json")):
                    for s in subs:
                        state["results"].append({"sub": s, "ok": False, "missing": [],
                                                 "out_dir": "", "error": "assfonts 数据库构建失败"})
                    state["failed"] = len(subs)
                    state["exit"] = -1
                    state["status"] = "error"
                    state["current_video"] = ""
                    return
            for i, s in enumerate(subs):
                if state["stop_event"].is_set():
                    break
                stem = os.path.splitext(os.path.basename(s))[0]
                out_i = os.path.join(out_dir, stem)
                state["current"] = i
                state["current_video"] = os.path.basename(s)
                state["progress"] = int(round(i / float(len(subs)) * 100))
                if tool == "afs":
                    log_i = _item_log(i + 1, "afs")
                    rc = core.run_to_file([core.AFS, s, "--fonts", fdir, "--output", out_i,
                                           "--bin-path", core.PY_SCRIPTS], log_i, timeout=600,
                                          jid=jid, stop_flag=state["stop_event"])
                    txt = core.read_tail(log_i, 5000)
                    _append_log(os.path.basename(s), log_i)
                    ok = rc == 0 and not state["stop_event"].is_set()
                    state["results"].append({"sub": s, "ok": ok,
                                             "missing": _parse_afs_missing(txt), "out_dir": out_i})
                else:
                    log_i = _item_log(i + 1, "sub")
                    rc = core.run_to_file([core.ASSFONTS, "-f", fdir, "-s", "-c", "-d", dbdir,
                                           "-o", out_i, s], log_i, timeout=600,
                                          jid=jid, stop_flag=state["stop_event"])
                    txt = core.read_tail(log_i, 5000)
                    _append_log(os.path.basename(s), log_i)
                    ok = rc == 0 and not state["stop_event"].is_set()
                    state["results"].append({"sub": s, "ok": ok,
                                             "missing": _assfonts_missing(txt), "out_dir": out_i})
                if not ok:
                    state["failed"] += 1  # 单字幕失败不中断整体
                state["current"] = i + 1
                state["progress"] = int(round((i + 1) / float(len(subs)) * 100))
            if state["stop_event"].is_set():
                state["status"] = "killed"
            elif state["failed"]:
                state["status"] = "error"
                state["exit"] = 1
            else:
                state["status"] = "done"
                state["exit"] = 0
                state["progress"] = 100
                state["result"] = out_dir
            state["current_video"] = ""
        except Exception as ex:
            state["exit"] = -1
            state["status"] = "error"
            state["current_video"] = ""
            try:
                state["log"] += "\nSERVER ERROR: %s\n" % ex
            except Exception:
                pass

    with core.JOBS_LOCK:
        if any(js.get("status") == "running" for js in core.JOBS.values()):
            return {"error": "另一个任务正在运行，请等待完成"}
        core.JOBS[jid] = state
        threading.Thread(target=worker, daemon=True).start()
    return {"job": jid, "tool": tool}


def handle_run(body):
    return start_subset(body.get("subs") or [],
                        body.get("fonts_dir") or "",
                        body.get("out_dir") or "",
                        bool(body.get("use_sys_fonts")))


def _is_subset_job(st):
    return isinstance(st, dict) and "tool" in st


def handle_status(q):
    rid = q.get("id") if isinstance(q, dict) else ""
    if isinstance(rid, list):
        rid = rid[0] if rid else ""
    st = core.JOBS.get((rid or "").strip())
    if not _is_subset_job(st):
        return {"error": "任务不存在"}
    return {"id": st["id"], "status": st.get("status"), "done": st.get("current", 0),
            "total": st.get("total", 0), "failed": st.get("failed", 0),
            "current_video": st.get("current_video", ""), "progress": st.get("progress", 0),
            "tool": st.get("tool", ""), "results": st.get("results", []),
            "log": st.get("log", "")}


def handle_stop(body):
    jid = (body.get("id") or "").strip()
    st = core.JOBS.get(jid)
    if not _is_subset_job(st):
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


handlers = {"GET": {"/api/subset_status": handle_status},
            "POST": {"/api/subset_run": handle_run, "/api/subset_stop": handle_stop}}
