# -*- coding: utf-8 -*-
# fontcheck_cli.py — 字体体检独立入口（与 Web 同一份实现：app.features.misc）
# 用法: py -3 scripts/fontcheck_cli.py --fonts-dir D sub1.ass [sub2.ass ...]
# 换子集工具时的隔离试验场：先在这里校准，再动服务器。
import argparse, sys, os

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, BASE)
os.chdir(BASE)

from app.features import misc

def main():
    ap = argparse.ArgumentParser(description="Mux Deck 字体体检")
    ap.add_argument("--fonts-dir", required=True)
    ap.add_argument("subs", nargs="+", help="ASS/SSA/SRT 字幕文件")
    a = ap.parse_args()

    r = misc.check_fonts([s for s in a.subs if s], a.fonts_dir)
    if r.get("error"):
        print("FAIL: " + str(r["error"]), flush=True)
        if r.get("log"):
            print(r["log"][-2000:], flush=True)
        sys.exit(1)
    if r.get("ok"):
        print("OK: 字体齐全", flush=True)
        sys.exit(0)
    print("缺字体/缺字形:", flush=True)
    for m in r.get("missing") or []:
        print("  - " + m, flush=True)
    sys.exit(2)

if __name__ == "__main__":
    main()
