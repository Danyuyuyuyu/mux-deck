# -*- coding: utf-8 -*-
# 静态预览文件：GET /api/file（仅预览目录，防越界；兼容旧 data/previews）
# 拖放识别：POST /api/drop
import os
from app import core

def serve_file(q):
    name = (q.get("path") or [""])[0].split("?")[0]
    fp = ""
    for base_dir in (core.PREVIEW_DIR, core.LEGACY_PREVIEW_DIR):
        cand = os.path.abspath(os.path.join(base_dir, os.path.basename(name)))
        if cand.startswith(os.path.abspath(base_dir)) and os.path.exists(cand):
            fp = cand
            break
    if not fp:
        return {"_raw": None, "error": "not found"}
    ext = core.ext_of(fp)
    ctype = "image/png" if ext == ".png" else "image/jpeg" if ext in (".jpg", ".jpeg") else "application/octet-stream"
    try:
        with open(fp, "rb") as f:
            data = f.read()
        return {"_raw": data, "_ctype": ctype}
    except Exception as ex:
        return {"_raw": None, "error": str(ex)}


def handle_drop(body):
    return core.resolve_drop([s for s in (body.get("names") or []) if s])


handlers = {"GET": {"/api/file": serve_file}, "POST": {"/api/drop": handle_drop}}
