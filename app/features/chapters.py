# -*- coding: utf-8 -*-
# 章节编辑支持：POST /api/chapters/{extract,parse,save}
#   extract：mkvextract 从源视频抽章节（XML）→ 解析为 [{time, name}]
#   parse  ：解析 OGM txt / XML 章节文件 → 同上
#   save   ：校验并写 OGM txt 到 data/tmp，返回路径（供 mux_cli --chapters 消费）
import os, re, uuid
import xml.etree.ElementTree as ET
from app import core

_NS_STRIP = re.compile(r"\{[^}]*\}")

def _xml_ts_to_ogm(ts):
    """XML 时间戳 00:00:01.234567890 -> OGM 00:00:01.234"""
    m = re.match(r"^(\d+):(\d{2}):(\d{2})[.,](\d+)$", ts.strip())
    if not m:
        return ts.strip()
    return "%s:%s:%s.%s" % (m.group(1), m.group(2), m.group(3), m.group(4)[:3])

def _parse_xml(path):
    tree = ET.parse(path)
    out = []
    for atom in tree.iter():
        if _NS_STRIP.sub("", atom.tag) != "ChapterAtom":
            continue
        start = name = ""
        for ch in atom:
            tag = _NS_STRIP.sub("", ch.tag)
            if tag == "ChapterTimeStart":
                start = (ch.text or "").strip()
            elif tag == "ChapterDisplay":
                for d in ch:
                    if _NS_STRIP.sub("", d.tag) == "ChapterString":
                        name = (d.text or "").strip()
        if start:
            out.append({"time": _xml_ts_to_ogm(start), "name": name})
    return out

def _parse_ogm(path):
    out = []
    try:
        with open(path, encoding="utf-8-sig", errors="replace") as f:
            for ln in f:
                m = re.match(r"^CHAPTER(\d+)=(.+)$", ln.strip(), re.I)
                if m:
                    ts = _xml_ts_to_ogm(m.group(2))
                    out.append({"time": ts, "name": "", "idx": int(m.group(1))})
                m2 = re.match(r"^CHAPTER\d+NAME=(.+)$", ln.strip(), re.I)
                if m2 and out:
                    out[-1]["name"] = m2.group(1).strip()
    except OSError as ex:
        return None
    for c in out:
        c.pop("idx", None)
    return out

def _validate(chapters):
    if not chapters or not isinstance(chapters, list):
        return "章节为空"
    prev = -1.0
    for i, c in enumerate(chapters, 1):
        if not isinstance(c, dict) or not re.match(r"^\d+:\d{2}:\d{2}[.,]\d{1,3}$", str(c.get("time") or "")):
            return "第 %d 条时间戳无效（应为 H:MM:SS.ms）" % i
        m = re.match(r"^(\d+):(\d{2}):(\d{2})[.,](\d+)$", str(c["time"]))
        sec = int(m.group(1)) * 3600 + int(m.group(2)) * 60 + int(m.group(3))
        if sec < prev:
            return "第 %d 条时间早于上一条" % i
        prev = sec
    return ""

def handle_extract(body):
    video = (body.get("video") or "").strip()
    if not video or not os.path.isfile(video):
        return {"error": "视频文件不存在"}
    if not core.MKVEXTRACT:
        return {"error": "未找到 mkvextract"}
    tmp_xml = os.path.join(core.TMP_DIR, "chapters_%s.xml" % uuid.uuid4().hex[:8])
    rc = core.run_to_file([core.MKVEXTRACT, video, "chapters", tmp_xml], os.path.join(core.LOG_DIR, "chapters_%s.log" % uuid.uuid4().hex[:8]), timeout=120)
    try:
        if rc != 0 or not os.path.isfile(tmp_xml):
            return {"chapters": [], "note": "源视频没有章节（或提取失败）"}
        return {"chapters": _parse_xml(tmp_xml)}
    finally:
        try:
            os.remove(tmp_xml)
        except OSError:
            pass

def handle_parse(body):
    path = (body.get("path") or "").strip()
    if not path or not os.path.isfile(path):
        return {"error": "章节文件不存在"}
    if path.lower().endswith(".xml"):
        try:
            return {"chapters": _parse_xml(path)}
        except Exception as ex:
            return {"error": "章节 XML 解析失败: %s" % ex}
    ch = _parse_ogm(path)
    if ch is None:
        return {"error": "章节文件读取失败"}
    if not ch:
        return {"error": "未识别到章节（OGM txt 应为 CHAPTER01=... / CHAPTER01NAME=... 格式）"}
    return {"chapters": ch}

def handle_save(body):
    chapters = body.get("chapters")
    err = _validate(chapters)
    if err:
        return {"error": err}
    lines = []
    for i, c in enumerate(chapters, 1):
        ts = str(c["time"]).replace(",", ".")
        m = re.match(r"^(\d+):(\d{1,2}):(\d{2})\.(\d+)$", ts)
        lines.append("CHAPTER%02d=%s:%s:%s.%s" % (i, m.group(1).zfill(2), m.group(2).zfill(2), m.group(3), (m.group(4) + "00")[:3]))
        lines.append("CHAPTER%02dNAME=%s" % (i, str(c.get("name") or "")))
    os.makedirs(core.TMP_DIR, exist_ok=True)
    path = os.path.join(core.TMP_DIR, "chapters_%s.txt" % uuid.uuid4().hex[:8])
    with open(path, "w", encoding="utf-8", newline="\r\n") as f:
        f.write("\n".join(lines) + "\n")
    return {"path": path, "count": len(chapters)}


handlers = {"POST": {"/api/chapters/extract": handle_extract,
                     "/api/chapters/parse": handle_parse,
                     "/api/chapters/save": handle_save}}
