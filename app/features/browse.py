# -*- coding: utf-8 -*-
# 文件浏览器：GET /api/list；系统打开：GET /api/open
import os, subprocess
from app import core

def browse_dir(path):
    if not path:
        drives = []
        for d in "ABCDEFGHIJKLMNOPQRSTUVWXYZ":
            if os.path.exists(d + ":" + chr(92)):
                drives.append(d + ":" + chr(92))
        return {"path": "", "drives": drives, "dirs": [], "files": []}
    path = os.path.normpath(path)
    if not os.path.isdir(path):
        return {"path": path, "dirs": [], "files": [], "error": "not a directory"}
    dirs, files = [], []
    try:
        with os.scandir(path) as it:
            for e in it:
                try:
                    if e.is_dir():
                        dirs.append(e.name)
                    else:
                        try:
                            sz = e.stat().st_size
                        except OSError:
                            sz = 0
                        files.append((e.name, sz))
                except OSError:
                    pass
    except OSError as ex:
        return {"path": path, "dirs": [], "files": [], "error": str(ex)}
    dirs.sort(key=str.lower)
    files.sort(key=lambda x: str.lower(x[0]))
    return {"path": path, "dirs": dirs, "files": files}


def handle(q):
    return browse_dir((q.get("path") or [""])[0])


def open_path(path):
    if not path or not os.path.exists(path):
        return {"error": "路径不存在"}
    try:
        subprocess.Popen(["explorer", "/select,", path])
        return {"ok": True}
    except Exception as ex:
        return {"error": str(ex)}


def handle_open(q):
    return open_path((q.get("path") or [""])[0])


handlers = {"GET": {"/api/list": handle, "/api/open": handle_open}}
