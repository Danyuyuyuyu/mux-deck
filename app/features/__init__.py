# -*- coding: utf-8 -*-
# 功能注册表：每个模块暴露 handlers = {"GET": {path: fn}, "POST": {path: fn}}
# server.py 只做分发；新增功能 = 新模块 + 在此登记。
from app.features import browse, files, tracks, mux, extract, preview, misc, fonts

ROUTES = {"GET": {}, "POST": {}}


def _register(mod):
    for method, table in (getattr(mod, "handlers", {}) or {}).items():
        for path, fn in table.items():
            ROUTES.setdefault(method, {})[path] = fn


for _m in (browse, files, tracks, mux, extract, preview, misc, fonts):
    _register(_m)


def find(method, path):
    return (ROUTES.get(method) or {}).get(path)


dispatch = find
