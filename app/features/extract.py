# -*- coding: utf-8 -*-
# 字幕提取：POST /api/extract（mkvextract 抽轨，文件名带语言后缀）
import json, os, re, threading, time, uuid
from app import core

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
    with core.JOBS_LOCK:
        if any(js.get("status") == "running" for js in core.JOBS.values()):
            return {"error": "另一个任务正在运行，请等待完成"}
    jid = uuid.uuid4().hex[:12]
    jdir = os.path.join(core.JOBS_DIR, jid)
    os.makedirs(jdir)
    base = os.path.splitext(os.path.basename(video))[0]
    with open(os.path.join(jdir, "params.json"), "w", encoding="utf-8") as f:
        json.dump({"video": video, "tracks": tracks, "out_dir": out_dir}, f, ensure_ascii=False, indent=2)
    cmd = [core.MKVEXTRACT, "tracks", video]
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
             "failed": 0, "results": [], "result": "、".join(os.path.basename(o) for o in outs),
             "stop_event": threading.Event()}

    def worker():
        try:
            rc = core.run_to_file(cmd, log, jid=jid, stop_flag=state["stop_event"])
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
            with open(os.path.join(core.LOG_DIR, "job_%s.log" % jid), "w", encoding="utf-8", errors="replace") as f:
                f.write(core.read_tail(log, 100))
        except Exception:
            pass

    with core.JOBS_LOCK:
        if any(js.get("status") == "running" for js in core.JOBS.values()):
            return {"error": "另一个任务正在运行，请等待完成"}
        core.JOBS[jid] = state
        threading.Thread(target=worker, daemon=True).start()
    return {"job": jid}


def handle_extract(body):
    return extract_subs((body.get("video") or "").strip(),
                        body.get("tracks") or [],
                        (body.get("out_dir") or "").strip())


handlers = {"POST": {"/api/extract": handle_extract}}
