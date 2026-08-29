# -*- coding: utf-8 -*-
# 杂项：GET /api/version、GET/POST /api/config（工作目录 + 子集化工具）、/api/presets（封装预设）、
#       /api/backups（替换模式备份目录列出/清理）
# 字体体检/补给已拆至 app/features/fonts.py（字体域）
import os, shutil, time
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

# ---------------- 备份目录（替换模式产生的 __mux_tmp_manual，列出/清理） ----------------

BACKUPS_LOG = os.path.join(core.APP_DIR, "data", "backups.log")

def _backup_log_paths():
    seen, out = set(), []
    try:
        with open(BACKUPS_LOG, encoding="utf-8", errors="replace") as f:
            for ln in f:
                p = ln.strip()
                if p and p not in seen:
                    seen.add(p)
                    out.append(p)
    except OSError:
        pass
    return out

def _dir_size(path):
    total = 0
    for root, _, files in os.walk(path):
        for fn in files:
            try:
                total += os.path.getsize(os.path.join(root, fn))
            except OSError:
                pass
    return total

def handle_backups_get(q):
    items = []
    for p in _backup_log_paths():
        if not os.path.isdir(p):
            continue   # 已被手动删除的记录自动视为失效，清理时一并从记录移除
        items.append({"path": p, "size": _dir_size(p)})
    return {"items": items}

def handle_backups_clean(body):
    paths = body.get("paths") or []
    if not isinstance(paths, list):
        return {"error": "无效的路径列表"}
    keep = set(_backup_log_paths()) - set(paths)
    cleaned, errors = [], []
    for p in paths:
        if os.path.isdir(p):
            try:
                shutil.rmtree(p)
                cleaned.append(p)
            except OSError as ex:
                errors.append("%s: %s" % (p, ex))
    try:
        os.makedirs(os.path.dirname(BACKUPS_LOG), exist_ok=True)
        with open(BACKUPS_LOG, "w", encoding="utf-8") as f:
            f.write("".join(p + "\n" for p in sorted(keep)))
    except OSError:
        pass
    return {"ok": True, "cleaned": cleaned, "errors": errors}

handlers = {
    "GET": {"/api/version": handle_version, "/api/config": handle_config_get, "/api/presets": handle_presets_get,
            "/api/backups": handle_backups_get},
    "POST": {"/api/config": handle_config_post,
             "/api/presets": handle_presets_post, "/api/presets/delete": handle_presets_delete,
             "/api/backups/clean": handle_backups_clean},
}
