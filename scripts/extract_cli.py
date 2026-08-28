# -*- coding: utf-8 -*-
# extract_cli.py — 字幕提取独立入口（与 Web 同一份实现：app.features.extract）
# 用法: py -3 scripts/extract_cli.py --video V [--out-dir D] --track 2:ass:zh-Hans [--track 3:srt] ...
# --track 格式: <轨道id>:<扩展名>:<语言>（扩展名默认 ass，语言可省）
import argparse, os, sys, time

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, BASE)
os.chdir(BASE)

from app import core
from app.features import extract as extract_mod

def parse_track(spec):
    parts = spec.split(":")
    try:
        tid = int(parts[0])
    except (IndexError, ValueError):
        raise argparse.ArgumentTypeError("无效轨道: " + spec)
    ext = parts[1] if len(parts) > 1 and parts[1] else "ass"
    lang = parts[2] if len(parts) > 2 else ""
    return {"id": tid, "ext": ext, "lang": lang}

def main():
    ap = argparse.ArgumentParser(description="Mux Deck 字幕提取（mkvextract 抽轨）")
    ap.add_argument("--video", required=True)
    ap.add_argument("--out-dir", default="")
    ap.add_argument("--track", action="append", type=parse_track, required=True,
                    metavar="ID:EXT:LANG", help="可重复；例: --track 2:ass:zh-Hans")
    a = ap.parse_args()

    r = extract_mod.extract_subs(a.video, a.track, a.out_dir)
    if "job" not in r:
        print("FAIL: " + str(r.get("error")), flush=True)
        sys.exit(1)
    jid = r["job"]
    print("job: " + jid, flush=True)
    while True:
        st = core.JOBS.get(jid) or {}
        if st.get("status") != "running":
            break
        time.sleep(1)
    status = st.get("status")
    print("status: %s exit=%s" % (status, st.get("exit")), flush=True)
    log = os.path.join(st.get("dir", ""), "item_01.log")
    tail = core.read_tail(log, 30)
    if tail:
        print(tail, flush=True)
    if status == "done":
        print("OK -> " + st.get("result", ""), flush=True)
        sys.exit(0)
    sys.exit(1)

if __name__ == "__main__":
    main()
