# -*- coding: utf-8 -*-
# 杂项：GET /api/version、GET/POST /api/config（工作目录）、POST /api/check_fonts（字体体检）
import os, re, time, uuid
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

# ---------------- 字体体检（双轨：AFS 主用=跑一遍子集当体检；assfonts 回退=建库+子集检查） ----------------

def _parse_afs_missing(txt):
    """从 AFS 输出解析缺字体报告：'Not found font file: name,0,0,1、name2,...'"""
    missing = []
    for line in txt.splitlines():
        if "Not found font file:" in line:
            rest = line.split("Not found font file:", 1)[1].strip()
            for item in rest.split("、"):
                name = item.split(",", 1)[0].strip().lstrip("@")
                if name and name not in missing:
                    missing.append(name)
    return missing

def check_fonts_afs(subs, fonts_dir):
    ck = os.path.join(core.TMP_DIR, "fontcheck_" + uuid.uuid4().hex[:8])
    os.makedirs(ck)
    logs = []
    missing = []
    for i, sub in enumerate(subs):
        out_i = os.path.join(ck, "out_%d" % i)
        log_i = os.path.join(ck, "afs_%d.log" % i)
        rc = core.run_to_file([core.AFS, sub, "--fonts", fonts_dir, "--output", out_i,
                               "--bin-path", core.PY_SCRIPTS], log_i, timeout=600)
        txt = core.read_tail(log_i, 5000)
        logs.append(txt)
        for name in _parse_afs_missing(txt):
            if name not in missing:
                missing.append(name)
        if "Duplicate fonts" in txt or "duplicate fonts" in txt:
            return {"ok": False,
                    "error": "字体目录存在重复字体（AFS 要求目录内同族字体只保留一份），请精简字体目录，或在 config.json 把 subset_tool 改为 assfonts",
                    "missing": missing, "log": "\n".join(logs)}
        if rc != 0 and not missing:
            return {"ok": False, "error": "AFS 检查失败", "missing": [], "log": "\n".join(logs)}
    return {"ok": len(missing) == 0, "missing": missing, "log": "\n".join(logs)}

def check_fonts_assfonts(subs, fonts_dir):
    if not fonts_dir or not os.path.isdir(fonts_dir):
        return {"ok": False, "error": "字体目录不存在"}
    if not subs:
        return {"ok": False, "error": "请先提供字幕文件"}
    ck = os.path.join(core.TMP_DIR, "fontcheck_" + uuid.uuid4().hex[:8])
    os.makedirs(ck)
    dbdir = os.path.join(ck, "db")
    os.makedirs(dbdir)
    outdir = os.path.join(ck, "out")
    os.makedirs(outdir)
    build_log = os.path.join(ck, "build.log")
    rc = core.run_to_file([core.ASSFONTS, "-f", fonts_dir, "-b", "-d", dbdir], build_log, timeout=300)
    if rc != 0:
        return {"ok": False, "error": "assfonts 数据库构建失败", "log": core.read_tail(build_log, 80)}
    db = os.path.join(dbdir, "fonts.json")
    if not os.path.exists(db):
        return {"ok": False, "error": "字体数据库未生成"}
    sub_log = os.path.join(ck, "sub.log")
    args = [core.ASSFONTS, "-f", fonts_dir, "-s", "-c",
            "-d", dbdir, "-o", outdir] + list(subs)
    rc = core.run_to_file(args, sub_log, timeout=600)
    txt = core.read_tail(sub_log, 5000)
    if rc != 0:
        return {"ok": False, "error": "assfonts 检查失败", "missing": [], "log": txt}
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
            if not name:
                item = "未命名字体缺字形"
                if item not in missing:
                    missing.append(item)
            elif name not in missing:
                missing.append(name)
    return {"ok": len(missing) == 0 and rc == 0, "missing": missing, "log": txt}


def check_fonts(subs, fonts_dir):
    if not fonts_dir or not os.path.isdir(fonts_dir):
        return {"ok": False, "error": "字体目录不存在"}
    if not subs:
        return {"ok": False, "error": "请先提供字幕文件"}
    if core.CONFIG.get("subset_tool") == "afs" and core.AFS:
        return check_fonts_afs(subs, fonts_dir)
    return check_fonts_assfonts(subs, fonts_dir)


def handle_check_fonts(body):
    return check_fonts([s for s in (body.get("subs") or []) if s], (body.get("fonts_dir") or "").strip())


handlers = {
    "GET": {"/api/version": handle_version, "/api/config": handle_config_get},
    "POST": {"/api/config": handle_config_post, "/api/check_fonts": handle_check_fonts},
}
