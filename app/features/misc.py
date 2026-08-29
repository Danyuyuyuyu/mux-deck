# -*- coding: utf-8 -*-
# 杂项：GET /api/version、GET/POST /api/config（工作目录 + 子集化工具）、/api/presets（封装预设）
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

# ---------------- 封装预设（轨道名/旗标/字体目录/工具等，存 config.json） ----------------

def handle_presets_get(q):
    return {"presets": core.CONFIG.get("presets") or {}}

def handle_presets_post(body):
    name = (body.get("name") or "").strip()
    data = body.get("data")
    if not name or len(name) > 50:
        return {"error": "预设名无效（1-50 字符）"}
    if not isinstance(data, dict):
        return {"error": "无效的预设数据"}
    presets = dict(core.CONFIG.get("presets") or {})
    presets[name] = data
    try:
        core.save_config(presets=presets)
    except Exception as ex:
        return {"error": "保存失败: %s" % ex}
    return {"ok": True, "presets": presets}

def handle_presets_delete(body):
    name = (body.get("name") or "").strip()
    presets = dict(core.CONFIG.get("presets") or {})
    if name not in presets:
        return {"error": "预设不存在: " + name}
    del presets[name]
    try:
        core.save_config(presets=presets)
    except Exception as ex:
        return {"error": "删除失败: %s" % ex}
    return {"ok": True, "presets": presets}

handlers = {
    "GET": {"/api/version": handle_version, "/api/config": handle_config_get, "/api/presets": handle_presets_get},
    "POST": {"/api/config": handle_config_post,
             "/api/presets": handle_presets_post, "/api/presets/delete": handle_presets_delete},
}
