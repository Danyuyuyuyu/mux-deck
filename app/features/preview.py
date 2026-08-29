# -*- coding: utf-8 -*-
# 预览帧渲染：POST /api/preview（ffmpeg+libass 烧录一帧；支持内封字幕轨与 SRT 转 ASS）
# 单帧同步返回；连拍（grid）走 job 模式——逐帧回报进度，可中途停止
import json, os, shutil, threading, time, uuid
from app import core

def _extract_embedded_fonts(video, pid):
    """把视频自带的字体附件抽到 PREVIEW_DIR/<pid>_fonts（内封轨道预览用）；无或失败返回空串。"""
    tmp_json = os.path.join(core.JOBS_DIR, "probe_%s.json" % pid)
    plog = os.path.join(core.LOG_DIR, "preview_%s_probe.log" % pid)
    atts = []
    if core.run_to_file([core.MKVMERGE, "-J", video], tmp_json, timeout=120) == 0:
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
    fdir = os.path.join(core.PREVIEW_DIR, pid + "_fonts")
    os.makedirs(fdir, exist_ok=True)
    cmd = [core.MKVEXTRACT, "attachments", video]
    for aid, fn in atts:
        cmd.append("%d:%s" % (aid, os.path.join(fdir, fn)))
    core.run_to_file(cmd, plog, timeout=120)
    return fdir if os.listdir(fdir) else ""

def _ass_times(ass_path):
    """读 ASS Dialogue 的 Start 时间（秒），升序去重。"""
    ts = []
    try:
        with open(ass_path, encoding="utf-8-sig", errors="replace") as f:
            for line in f:
                if line.startswith("Dialogue:"):
                    parts = line.split(",", 10)
                    if len(parts) >= 3:
                        try:
                            h, m, s = parts[1].split(":")
                            ts.append(int(h) * 3600 + int(m) * 60 + float(s))
                        except (ValueError, IndexError):
                            pass
    except OSError:
        pass
    return sorted(set(ts))

def _grid_points(times, n=8, pad=0.05):
    """从字幕时间点均匀抽 n 个（首尾必含），各加 pad 秒保证字幕已上屏。"""
    if not times:
        return [0.0] * n
    if len(times) <= n:
        pts = list(times)
    else:
        step = (len(times) - 1) / float(n - 1)
        pts = [times[int(round(i * step))] for i in range(n)]
        pts = sorted(set(pts))
    return [min(p + pad, max(times)) for p in pts]

def make_preview(video, sub, fonts_dir, t, mode="frame", job=None):
    """job 不为 None 时（grid 连拍）：逐步更新 job 状态（current/total/progress/current_video）、
    按 step 写 item_NN.log 供 /api/job 合并日志、响应 stop_event；单帧/黑底模式忽略 job。"""
    ff = core.FFMPEG
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
        track_sub = os.path.join(core.PREVIEW_DIR, pid + "_track." + text_ext)
        xlog = os.path.join(core.LOG_DIR, "preview_%s_extract.log" % pid)
        rc = core.run_to_file([core.MKVEXTRACT, "tracks", video, "%d:%s" % (tid, track_sub)], xlog, timeout=120)
        if rc != 0 or not os.path.exists(track_sub):
            return {"error": "内封字幕轨提取失败", "log": core.read_tail(xlog, 30)}
        sub = track_sub
        fonts_dir = _extract_embedded_fonts(video, pid) or fonts_dir
    if not sub or not os.path.exists(sub):
        return {"error": "字幕文件不存在"}
    if mode != "subtitle" and (not video or not os.path.exists(video)):
        return {"error": "视频文件不存在"}
    if not embedded and (not fonts_dir or not os.path.isdir(fonts_dir)):
        return {"error": "字体目录不存在"}
    out_png = os.path.join(core.PREVIEW_DIR, pid + ".png")
    preview_ass = os.path.join(core.PREVIEW_DIR, pid + ".ass")
    if sub.lower().endswith(".srt"):
        conv_log = os.path.join(core.LOG_DIR, "preview_conv_%s.log" % pid)
        crc = core.run_to_file([ff, "-y", "-i", sub, preview_ass], conv_log, timeout=300)
        if crc != 0 or not os.path.exists(preview_ass):
            return {"error": "SRT 转 ASS 失败", "log": core.read_tail(conv_log, 60)}
    else:
        try:
            shutil.copy(sub, preview_ass)
        except Exception as ex:
            return {"error": "复制字幕失败: %s" % ex}
    if fonts_dir and os.path.isdir(fonts_dir):
        try:
            rel_fonts = os.path.relpath(fonts_dir, core.PREVIEW_DIR)
        except ValueError as ex:
            return {"error": "字体目录与预览目录不在同一驱动器: %s" % ex}
        vf = "ass=%s:fontsdir=%s,scale=1280:-2" % (os.path.basename(preview_ass), rel_fonts.replace("\\", "/"))
    else:
        vf = "ass=%s,scale=1280:-2" % os.path.basename(preview_ass)  # 无字体目录（内封轨且视频无字体附件）时用系统字体
    # ---- 连拍模式：按字幕时间点抽 8 帧拼 4x2 网格（帧宽 640，输入 seek 保速度） ----
    if mode == "grid":
        pts = _grid_points(_ass_times(preview_ass), 8)
        vf_g = vf.replace("scale=1280:-2", "scale=640:-2")
        steps = len(pts) + 1   # 8 帧 + 拼图
        log = os.path.join(core.LOG_DIR, "preview_%s.log" % pid)
        if job is not None:
            job["total"] = steps
        frames = []
        for i, tt in enumerate(pts):
            if job is not None:
                if job["stop_event"].is_set():
                    return {"error": "已停止"}
                job["current"] = i + 1
                job["current_video"] = "渲染第 %d/%d 帧" % (i + 1, len(pts))
                job["progress"] = int(round(i / float(steps) * 100))
                ilog = os.path.join(job["dir"], "item_%02d.log" % (i + 1))
            else:
                ilog = log
            fp = os.path.join(core.PREVIEW_DIR, "%s_f%d.png" % (pid, i))
            c = [ff, "-y", "-ss", str(tt), "-i", video, "-vf", vf_g, "-frames:v", "1", fp]
            rc = core.run_to_file(c, ilog, timeout=120, cwd=core.PREVIEW_DIR,
                                  jid=(job["id"] if job else None),
                                  stop_flag=(job["stop_event"] if job else None))
            if rc == 0 and os.path.exists(fp):
                frames.append(fp)
            if job is not None:
                job["progress"] = int(round((i + 1) / float(steps) * 100))
        if not frames:
            return {"error": "连拍渲染失败（一帧都没成功）", "log": core.read_tail(log, 60)}
        # 部分帧失败会导致 %d 模式断号 -> 重排成连续 seq
        seqs = []
        for i, fp in enumerate(frames):
            sp = os.path.join(core.PREVIEW_DIR, "%s_s%d.png" % (pid, i))
            try:
                os.replace(fp, sp)
                seqs.append(sp)
            except OSError:
                pass
        if job is not None:
            if job["stop_event"].is_set():
                return {"error": "已停止"}
            job["current"] = steps
            job["current_video"] = "拼接 4x2 网格"
            ilog = os.path.join(job["dir"], "item_%02d.log" % steps)
        else:
            ilog = log
        rc = core.run_to_file([ff, "-y", "-start_number", "0", "-i", "%s_s%%d.png" % os.path.join(core.PREVIEW_DIR, pid),
                               "-vf", "tile=4x2", "-frames:v", "1", out_png], ilog, timeout=120, cwd=core.PREVIEW_DIR,
                              jid=(job["id"] if job else None),
                              stop_flag=(job["stop_event"] if job else None))
        if job is not None:
            job["progress"] = 100
        for sp in seqs:
            try:
                os.remove(sp)
            except OSError:
                pass
        if rc != 0 or not os.path.exists(out_png):
            return {"error": "连拍拼图失败", "log": core.read_tail(log, 60)}
        return {"ok": True, "url": "/api/file?path=" + pid + ".png", "pid": pid,
                "grid": True, "points": pts}
    log = os.path.join(core.LOG_DIR, "preview_%s.log" % pid)
    if mode == "subtitle":
        cmd = [ff, "-y", "-f", "lavfi", "-i", "color=c=black:s=1920x1080:r=25:d=36000",
               "-ss", str(t), "-vf", vf, "-frames:v", "1", out_png]
    else:
        cmd = [ff, "-y", "-i", video, "-ss", str(t), "-vf", vf, "-frames:v", "1", out_png]
    rc = core.run_to_file(cmd, log, timeout=600, cwd=core.PREVIEW_DIR)
    if rc != 0 or not os.path.exists(out_png):
        return {"error": "渲染失败", "log": core.read_tail(log, 60)}
    return {"ok": True, "url": "/api/file?path=" + pid + ".png", "pid": pid}


def start_grid_job(video, sub, fonts_dir):
    """连拍 job 化：轻校验同步返回 job id，重活（抽轨/字体/逐帧）在后台线程，进度经 /api/job 轮询。"""
    if not core.FFMPEG:
        return {"error": "未找到 ffmpeg（请安装并加入 PATH）"}
    embedded = isinstance(sub, str) and sub.startswith("track:")
    if not sub:
        return {"error": "请选择字幕文件"}
    if not video or not os.path.exists(video):
        return {"error": "视频文件不存在"}
    if not embedded and (not fonts_dir or not os.path.isdir(fonts_dir)):
        return {"error": "字体目录不存在"}
    with core.JOBS_LOCK:
        if any(js.get("status") == "running" for js in core.JOBS.values()):
            return {"error": "另一个任务正在运行，请等待完成"}
    jid = uuid.uuid4().hex[:12]
    jdir = os.path.join(core.PREVIEW_DIR, "job_" + jid)   # 放 PREVIEW_DIR：不进任务历史（预览无需重跑）
    os.makedirs(jdir)
    state = {"id": jid, "dir": jdir, "status": "running", "exit": None, "started": time.time(),
             "current": 0, "total": 9, "current_video": "准备字幕与字体…", "progress": 0,
             "failed": 0, "results": [], "result": "", "stop_event": threading.Event()}

    def worker():
        try:
            r = make_preview(video, sub, fonts_dir, 0, mode="grid", job=state)
            if state.get("stopped"):
                state["status"] = "killed"
            elif r.get("error"):
                state["status"] = "error"
                state["exit"] = -1
                state["failed"] = 1
                try:   # FAIL: 行 = job_status reason 的协议来源
                    with open(os.path.join(jdir, "item_%02d.log" % max(state["current"], 1)), "a",
                              encoding="utf-8", errors="replace") as f:
                        f.write("\nFAIL: %s\n" % r.get("error"))
                except OSError:
                    pass
            else:
                state["status"] = "done"
                state["exit"] = 0
                state["progress"] = 100
                state["result"] = r.get("url", "")
        except Exception:
            state["status"] = "error"
            state["exit"] = -1
            state["failed"] = 1
        # 预览任务不落盘 state.json（无历史/重跑需求），JOBS 内存态够轮询用

    with core.JOBS_LOCK:
        if any(js.get("status") == "running" for js in core.JOBS.values()):
            return {"error": "另一个任务正在运行，请等待完成"}
        core.JOBS[jid] = state
        threading.Thread(target=worker, daemon=True).start()
    return {"job": jid}


def handle_preview(body):
    try:
        tm = float(body.get("time") or 0)
    except (TypeError, ValueError):
        return {"error": "无效的时间参数"}
    video = (body.get("video") or "").strip()
    sub = (body.get("sub") or "").strip()
    fonts_dir = (body.get("fonts_dir") or "").strip()
    mode = (body.get("mode") or "frame")
    if mode == "grid":
        return start_grid_job(video, sub, fonts_dir)
    return make_preview(video, sub, fonts_dir, tm, mode)


handlers = {"POST": {"/api/preview": handle_preview}}
