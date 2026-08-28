# -*- coding: utf-8 -*-
# selfcheck.py — Mux Deck 环境自检（环境自检.bat 的 Python 继任者）
import os, shutil, socket, subprocess, sys

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FAIL = 0

def ok(msg):
    print("[PASS] " + msg, flush=True)

def bad(msg):
    global FAIL
    FAIL += 1
    print("[FAIL] " + msg, flush=True)

def warn(msg):
    print("[WARN] " + msg, flush=True)

def first_existing(*paths):
    for p in paths:
        if p and os.path.isfile(p):
            return p
    return None

print("=" * 44, flush=True)
print("  Mux Deck 环境自检", flush=True)
print("=" * 44, flush=True)

# ---- Python（就是当前解释器；能跑到这里说明已安装，报告版本）----
v = sys.version_info
if v >= (3, 8):
    ok("Python: %d.%d.%d (%s)" % (v[0], v[1], v[2], sys.executable))
else:
    bad("Python 版本过低: %d.%d（需要 3.8+）" % (v[0], v[1]))

# ---- mkvmerge / mkvextract ----
mkv = first_existing(os.path.join(BASE, "bin", "mkvtoolnix", "mkvmerge.exe"),
                     r"C:\Program Files\MKVToolNix\mkvmerge.exe") or shutil.which("mkvmerge")
if mkv:
    ok("mkvmerge: " + mkv)
else:
    bad("mkvmerge: 未找到（缺 bin\\mkvtoolnix 或系统安装）")
mke = first_existing(os.path.join(BASE, "bin", "mkvtoolnix", "mkvextract.exe"),
                     r"C:\Program Files\MKVToolNix\mkvextract.exe") or shutil.which("mkvextract")
if mke:
    ok("mkvextract: " + mke)
else:
    bad("mkvextract: 未找到")

# ---- ffmpeg ----
ff = first_existing(os.path.join(BASE, "bin", "ffmpeg", "bin", "ffmpeg.exe")) or shutil.which("ffmpeg")
if ff:
    ok("ffmpeg: " + ff)
else:
    bad("ffmpeg: 未找到（缺 bin\\ffmpeg 或 PATH）")

# ---- assfonts ----
af = first_existing(os.path.join(BASE, "bin", "assfonts", "assfonts.exe")) or shutil.which("assfonts")
if af:
    ok("assfonts: " + af)
else:
    bad("assfonts: 未找到（缺 bin\\assfonts\\assfonts.exe）")

# ---- AssFontSubset（默认主用子集工具；缺失时自动回退 assfonts，故只 WARN） ----
afs = first_existing(os.path.join(BASE, "bin", "assfontsubset", "AssFontSubset.Console.exe")) or shutil.which("AssFontSubset.Console")
if afs:
    ok("AssFontSubset: " + afs)
else:
    warn("AssFontSubset: 未找到（默认子集工具缺失，将回退 assfonts；运行 安装环境.bat 补齐）")

# ---- fonttools（AFS 默认后端 PyFontTools 依赖 pyftsubset/ttx） ----
try:
    import fontTools
    ok("fonttools: %s" % fontTools.version)
except ImportError:
    warn("fonttools: 未安装（AFS 默认后端不可用；pip install fonttools 或运行 安装环境.bat）")

# ---- 端口 8765 ----
s = socket.socket()
s.settimeout(1)
try:
    s.connect(("127.0.0.1", 8765))
    warn("端口 8765: 已被占用（服务会自动试 8766-8774，但启动脚本探测只认 8765）")
except OSError:
    ok("端口 8765: 空闲")
finally:
    s.close()

print("", flush=True)
if FAIL == 0:
    print("全部关键依赖就绪，可以运行 start_mux_ui.bat 启动。", flush=True)
else:
    print("有 %d 项缺失，请按上方 [FAIL] 提示补齐后再启动。" % FAIL, flush=True)
sys.exit(0 if FAIL == 0 else 1)
