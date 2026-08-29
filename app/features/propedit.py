# -*- coding: utf-8 -*-
# 快速修补：POST /api/propedit（mkvpropedit 原地改轨道旗标/名称/语言、MKV 标题、章节，不重封装）
# 章节仅支持 XML（mkvpropedit 限制）；应用成功后自动重 probe 供前端展示新状态
import json, os, re, uuid
from app import core


def handle_propedit(body):
    video = (body.get("video") or "").strip()
    if not video or not os.path.isfile(video):
        return {"error": "视频文件不存在"}
    if not core.MKVPROPEDIT or not os.path.isfile(core.MKVPROPEDIT):
        return {"error": "未找到 mkvpropedit（请安装 MKVToolNix 或在网页内一键安装组件）"}
    title = str(body.get("title") or "").strip()
    chapters = (body.get("chapters") or "").strip()
    tracks = body.get("tracks") or []
    if not isinstance(tracks, list):
        return {"error": "无效的轨道列表"}

    args = [core.MKVPROPEDIT, video]
    if title:
        args += ["--edit", "info", "--set", "title=" + title]   # segment 标题需 info 作用域
    n_edits = 1 if title else 0
    for t in tracks:
        if not isinstance(t, dict):
            continue
        kind = t.get("kind")
        if kind not in ("audio", "subtitles", "video"):
            continue
        try:
            ordinal = int(t.get("index"))
        except (TypeError, ValueError):
            continue
        if ordinal < 1:
            continue
        args += ["--edit", "track:%s%d" % ({"audio": "a", "subtitles": "s", "video": "v"}[kind], ordinal)]
        seg = 0
        if t.get("name") is not None:
            args += ["--set", "name=" + str(t.get("name"))]
            seg += 1
        if t.get("language") is not None:
            args += ["--set", "language=" + str(t.get("language"))]
            seg += 1
        if t.get("default") is not None:
            args += ["--set", "flag-default=" + ("1" if t.get("default") else "0")]
            seg += 1
        if t.get("forced") is not None:
            args += ["--set", "flag-forced=" + ("1" if t.get("forced") else "0")]
            seg += 1
        n_edits += seg
    if chapters:
        if not os.path.isfile(chapters):
            return {"error": "章节文件不存在: " + chapters}
        args += ["--chapters", chapters]
        n_edits += 1
    if n_edits == 0:
        return {"error": "没有任何修改（请先在轨道表或标题/章节里改动）"}

    log = os.path.join(core.LOG_DIR, "propedit_%s.log" % uuid.uuid4().hex[:10])
    rc = core.run_to_file(args, log, timeout=120)
    tail = core.read_tail(log, 40)
    if rc != 0:
        if re.search(r"没有修改|Nothing to do", tail):
            return {"error": "没有任何修改（新旧值相同）"}
        return {"error": "mkvpropedit 失败（退出码 %d）" % rc, "log": tail}
    # 应用后重探，前端可直接展示新状态
    probe = {}
    pj = os.path.join(core.JOBS_DIR, "probe_%s.json" % uuid.uuid4().hex[:10])
    if core.run_to_file([core.MKVMERGE, "-J", video], pj, timeout=120) == 0:
        try:
            with open(pj, encoding="utf-8", errors="replace") as f:
                probe = json.load(f)
        except Exception:
            probe = {}
    try:
        os.remove(pj)
    except OSError:
        pass
    return {"ok": True, "log": tail, "probe": probe}


handlers = {"POST": {"/api/propedit": handle_propedit}}
