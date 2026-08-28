# -*- coding: utf-8 -*-
# 杂项：GET /api/version、GET/POST /api/config（工作目录 + 子集化工具）
# 字体体检/补给已拆至 app/features/fonts.py（字体域）
import os, time
from app import core

# ---------------- version / config ----------------

def handle_version(q):
    return {"ok": True, "time": time.time()}

def handle_config_get(q):
    return {"scan_root": core.CONFIG["scan_root"],
            "valid": os.path.isdir(core.CONFIG["scan_root"]),
            "configured": os.path.isfile(core.CONFIG_PATH),
            "subset_tool": core.CONFIG.get("subset_tool", "afs")}

def handle_config_post(body):
    tool = (body.get("subset_tool") or "").strip()
    if tool:
        if tool not in ("afs", "assfonts"):
            return {"error": "未知子集工具: " + tool}
        try:
            core.save_config(subset_tool=tool)
        except Exception as ex:
            return {"error": "保存失败: %s" % ex}
        return {"ok": True, "subset_tool": tool}
    p = (body.get("scan_root") or "").strip()
    if not p or not os.path.isdir(p):
        return {"error": "目录不存在: %s" % (p or "(空)")}
    try:
        core.save_config(scan_root=p)
    except Exception as ex:
        return {"error": "保存失败: %s" % ex}
    return {"ok": True, "scan_root": p}

handlers = {
    "GET": {"/api/version": handle_version, "/api/config": handle_config_get},
    "POST": {"/api/config": handle_config_post},
}
