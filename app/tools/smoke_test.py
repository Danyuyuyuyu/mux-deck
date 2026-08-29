# -*- coding: utf-8 -*-
# smoke_test.py — mux-deck 一键冒烟回归（12 端点 + 前端可达性）
# 用法: py -3 app\tools\smoke_test.py [--video 某个.mkv]
# 约束: 127.0.0.1 请求会被系统代理劫持，必须用空 ProxyHandler 绕开。
# 退出码: 0 = 全 PASS / SKIP；1 = 有 FAIL；2 = 服务未运行。
import argparse, glob, json, os, sys, urllib.error, urllib.parse, urllib.request

BASE = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# 已知可用测试素材（按优先级探测；都没有时 probe/history 相关用 SKIP 不 FAIL）
KNOWN_VIDEOS = [
    r"D:\Video\Anime\Dandadan\[Nekomoe kissaten&LoliHouse] Dandadan - s01e02 [WebRip 1080p HEVC-10bit AAC ASSx2].mkv",
]


def opener():
    return urllib.request.build_opener(urllib.request.ProxyHandler({}))


def find_video(explicit):
    """决定 probe 用素材：显式 > 已知路径 > scan_root 下第一个 .mkv。"""
    cands = []
    if explicit:
        cands.append(explicit)
    cands.extend(KNOWN_VIDEOS)
    for c in cands:
        if c and os.path.isfile(c):
            return c
    # 从配置读扫描根，浅层找一个 mkv（限 200 个，避免全盘扫）
    try:
        with open(os.path.join(BASE, "app", "config.json"), encoding="utf-8") as f:
            root = json.load(f).get("scan_root", "")
    except Exception:
        root = ""
    if root and os.path.isdir(root):
        for i, p in enumerate(glob.glob(os.path.join(root, "**", "*.mkv"), recursive=True)):
            if i >= 200:
                break
            return p
    return ""


def main():
    ap = argparse.ArgumentParser(description="mux-deck 冒烟回归")
    ap.add_argument("--video", help="probe 用的 MKV 素材路径（默认自动探测）")
    a = ap.parse_args()

    op = opener()

    def get(p, timeout=120):
        return json.loads(op.open("http://127.0.0.1:8765" + p, timeout=timeout).read().decode())
    def post(p, b, timeout=900):
        req = urllib.request.Request("http://127.0.0.1:8765" + p, data=json.dumps(b).encode(),
                                     headers={"Content-Type": "application/json"})
        return json.loads(op.open(req, timeout=timeout).read().decode())

    # 服务存活
    try:
        get("/api/version", timeout=5)
    except Exception as ex:
        print("FATAL 服务未运行 (%s)。请双击 start_mux_ui.bat 后重试。" % ex)
        return 2

    video = find_video(a.video)
    results = []  # (状态, 名称, 备注)
    def check(name, ok, note=""):
        results.append(("PASS" if ok else "FAIL", name, note))

    # 1) version
    try:
        check("version", bool(get("/api/version").get("ok")))
    except Exception as ex:
        check("version", False, str(ex))

    # 2) config
    try:
        c = get("/api/config")
        check("config", isinstance(c.get("scan_root"), str) and c.get("subset_tool") in ("afs", "assfonts"),
              "scan_root=%s subset_tool=%s" % (c.get("scan_root"), c.get("subset_tool")))
    except Exception as ex:
        check("config", False, str(ex))

    # 3) list（盘符枚举）
    try:
        check("list", "drives" in get("/api/list?path="))
    except Exception as ex:
        check("list", False, str(ex))

    # 4) probe（依赖素材）
    if video:
        try:
            pr = get("/api/probe?path=" + urllib.parse.quote(video))
            att = pr.get("attachments")
            att_n = att if isinstance(att, int) else len(att or [])
            check("probe", len(pr.get("tracks", [])) > 0, "%d 轨道 / %s 附件" % (
                len(pr.get("tracks", [])), att_n))
        except Exception as ex:
            check("probe", False, str(ex))
    else:
        results.append(("SKIP", "probe", "无可用 MKV 素材（--video 指定一个即可启用）"))

    # 5) history
    try:
        check("history", "items" in get("/api/history"))
    except Exception as ex:
        check("history", False, str(ex))

    # 6) match_subs
    if video:
        try:
            ms = get("/api/match_subs?path=" + urllib.parse.quote(video))
            check("match_subs", set(ms.keys()) == {"sc", "tc"})
        except Exception as ex:
            check("match_subs", False, str(ex))
    else:
        results.append(("SKIP", "match_subs", "无素材"))

    # 7) prep_subs（空字幕的边界行为）
    try:
        ps = post("/api/prep_subs", {"sc": "", "tc": ""})
        check("prep_subs", ps.get("sc", {}).get("encoding") == "-")
    except Exception as ex:
        check("prep_subs", False, str(ex))

    # 8) check_fonts 边界（空字幕应报 error 而不是崩）
    try:
        cf = post("/api/check_fonts", {"subs": [], "fonts_dir": r"D:\Video\Font_AFS"})
        check("check_fonts(空字幕)", "error" in cf)
    except Exception as ex:
        check("check_fonts(空字幕)", False, str(ex))

    # 9) drop 边界（不存在文件应返回空命中）
    try:
        dr = post("/api/drop", {"names": ["__smoke_test__.mkv"]})
        check("drop(不存在文件)", dr == {"__smoke_test__.mkv": []})
    except Exception as ex:
        check("drop(不存在文件)", False, str(ex))

    # 10) stop 边界（不存在任务应报 error）
    try:
        st = post("/api/stop", {"id": "__smoke_test__"})
        check("stop(不存在任务)", "error" in st)
    except Exception as ex:
        check("stop(不存在任务)", False, str(ex))

    # 11) 前端页面可达（拆分后 index.html 为骨架）：关键元素 + 各静态资源可达
    try:
        body = op.open("http://127.0.0.1:8765/", timeout=10).read().decode("utf-8", errors="replace")
        ui_ok = "cfg_tool" in body and "envList" in body and "btnEnvInstall" in body
        need = ["style.css", "app.js", "batch.js", "extract.js", "preview.js", "env.js", "init.js"]
        sizes = {}
        for f in need:
            r = op.open("http://127.0.0.1:8765/" + f, timeout=10)
            sizes[f] = len(r.read())
        all_ok = ui_ok and all(sizes.get(f, 0) > 100 for f in need)
        check("前端页面", all_ok, "%d bytes + 静态资源 %s" % (len(body), "OK" if all_ok else str(sizes)))
    except Exception as ex:
        check("前端页面", False, str(ex))

    # 12) env_check（环境检测：overall 合法 + 6 项组件齐全）
    try:
        ec = get("/api/env_check")
        keys = {i.get("key") for i in ec.get("items", [])}
        ok = ec.get("overall") in ("ready", "partial", "broken") and \
             keys == {"mkvtoolnix", "mkvextract", "ffmpeg", "afs", "assfonts", "fonttools"}
        check("env_check", ok, "overall=%s %d 项" % (ec.get("overall"), len(ec.get("items", []))))
    except Exception as ex:
        check("env_check", False, str(ex))

    # 13) detect_fonts_dir（识别服务：空路径返回空，不报错）
    try:
        fd = get("/api/detect_fonts_dir?path=")
        check("detect_fonts_dir", "fonts_dir" in fd and fd.get("fonts_dir") == "", "空路径=%s" % fd.get("fonts_dir"))
    except Exception as ex:
        check("detect_fonts_dir", False, str(ex))

    # 汇总
    print()
    print("=" * 60)
    for status, name, note in results:
        line = "%-4s %-24s %s" % (status, name, note)
        print(line)
    n_pass = sum(1 for s, _, _ in results if s == "PASS")
    n_skip = sum(1 for s, _, _ in results if s == "SKIP")
    n_fail = sum(1 for s, _, _ in results if s == "FAIL")
    print("=" * 60)
    print("PASS %d / FAIL %d / SKIP %d" % (n_pass, n_fail, n_skip))
    return 0 if n_fail == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
