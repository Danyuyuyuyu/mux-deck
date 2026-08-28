# -*- coding: utf-8 -*-
# 预览帧渲染：POST /api/preview（ffmpeg+libass 烧录一帧；支持内封字幕轨与 SRT 转 ASS）
import json, os, shutil, uuid
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

def make_preview(video, sub, fonts_dir, t, mode="frame"):
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


def handle_preview(body):
    try:
        tm = float(body.get("time") or 0)
    except (TypeError, ValueError):
        return {"error": "无效的时间参数"}
    return make_preview((body.get("video") or "").strip(), (body.get("sub") or "").strip(),
                        (body.get("fonts_dir") or "").strip(), tm,
                        (body.get("mode") or "frame"))


handlers = {"POST": {"/api/preview": handle_preview}}
