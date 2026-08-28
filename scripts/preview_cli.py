# -*- coding: utf-8 -*-
# preview_cli.py — 预览帧渲染独立入口（与 Web 同一份实现：app.features.preview）
# 用法: py -3 scripts/preview_cli.py --video V --sub A.ssa --fonts-dir D [--time 30] [--mode frame|subtitle]
#   --sub 也支持内封轨: "track:<id>:<ext>"
import argparse, os, sys

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, BASE)
os.chdir(BASE)

from app.features import preview as preview_mod

def main():
    ap = argparse.ArgumentParser(description="Mux Deck 预览帧渲染（ffmpeg+libass）")
    ap.add_argument("--video", default="")
    ap.add_argument("--sub", required=True, help="字幕文件路径，或 track:<id>:<ext> 内封轨")
    ap.add_argument("--fonts-dir", default="")
    ap.add_argument("--time", type=float, default=0)
    ap.add_argument("--mode", choices=["frame", "subtitle", "grid"], default="frame",
                    help="frame=视频烧录帧 / subtitle=黑底纯字幕 / grid=按字幕行抽8帧拼4x2网格（忽略 --time）")
    a = ap.parse_args()

    r = preview_mod.make_preview(a.video, a.sub, a.fonts_dir, a.time, a.mode)
    if not r.get("ok"):
        print("FAIL: " + str(r.get("error")), flush=True)
        if r.get("log"):
            print(r["log"], flush=True)
        sys.exit(1)
    # url 形如 /api/file?path=<pid>.png；CLI 直接给出磁盘路径
    from app import core
    pid = r.get("pid", "")
    print("OK -> " + os.path.join(core.PREVIEW_DIR, pid + ".png"), flush=True)
    sys.exit(0)

if __name__ == "__main__":
    main()
