# -*- coding: utf-8 -*-
# bootstrap.py — Mux Deck 运行时引导：下载 MKVToolNix / ffmpeg / assfonts / AssFontSubset 到 bin\（可重复运行，已存在则跳过）
# 用法: py -3 scripts/bootstrap.py [--proxy http://127.0.0.1:7890]
# 也可用环境变量 BOOTSTRAP_PROXY 指定代理。
#
# 核心逻辑函数化（ensure_xxx + run_bootstrap），同一份实现同时服务两处：
#   - CLI：py -3 scripts/bootstrap.py（main）
#   - 网页内安装：app/features/env.py 通过 run_bootstrap(proxy, items, log=...) 复用，
#     log 回调负责把进度输出捕获进安装日志，而非只打到终端。
import argparse, os, shutil, subprocess, sys, tempfile, urllib.request, zipfile

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BIN = os.path.join(BASE, "bin")

def get_file(url, out, proxy="", min_bytes=1000000):
    handlers = []
    if proxy:
        handlers.append(urllib.request.ProxyHandler({"http": proxy, "https": proxy}))
    opener = urllib.request.build_opener(*handlers)
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    with opener.open(req, timeout=600) as r, open(out, "wb") as f:
        while True:
            chunk = r.read(65536)
            if not chunk:
                break
            f.write(chunk)
    sz = os.path.getsize(out)
    if sz < min_bytes:
        raise RuntimeError("下载文件过小 (%d 字节)，疑似失败: %s" % (sz, url))

def test_zip(path):
    try:
        with zipfile.ZipFile(path) as z:
            return z.testzip() is None
    except Exception:
        return False

def get_zip_checked(url, out, proxy=""):
    get_file(url, out, proxy)
    if not test_zip(out):
        print("  zip 完整性校验失败，重试一次...", flush=True)
        get_file(url, out, proxy)
        if not test_zip(out):
            raise RuntimeError("zip 仍损坏: " + url)

def extract_zip(zpath, dest):
    with zipfile.ZipFile(zpath) as z:
        z.extractall(dest)

def _decode(data):
    try:
        return data.decode("utf-8")
    except UnicodeDecodeError:
        return data.decode("gbk", errors="replace")

# ---------- 各组件独立安装（确保"已存在即跳过"） ----------

def _ensure_mkvtoolnix(proxy, log, tmp):
    mkm = os.path.join(BIN, "mkvtoolnix", "mkvmerge.exe")
    if os.path.isfile(mkm):
        log("[1/5] MKVToolNix 已存在，跳过。")
        return
    log("[1/5] 下载 MKVToolNix 101.0 ...")
    zp = os.path.join(tmp, "mkv.zip")
    get_zip_checked("https://mkvtoolnix.download/windows/releases/101.0/mkvtoolnix-64-bit-101.0.zip", zp, proxy)
    dst = os.path.join(BIN, "mkvtoolnix")
    os.makedirs(dst, exist_ok=True)
    extract_zip(zp, dst)
    # zip 内有一层 mkvtoolnix-64-bit-XXX 目录，拍平
    for name in os.listdir(dst):
        inner = os.path.join(dst, name)
        if os.path.isdir(inner) and name.lower().startswith("mkvtoolnix") and not os.path.isfile(mkm):
            for item in os.listdir(inner):
                shutil.move(os.path.join(inner, item), os.path.join(dst, item))
            shutil.rmtree(inner, ignore_errors=True)
    log("  -> bin\\mkvtoolnix")

def _ensure_ffmpeg(proxy, log, tmp):
    ff = os.path.join(BIN, "ffmpeg", "bin", "ffmpeg.exe")
    if os.path.isfile(ff):
        log("[2/5] ffmpeg 已存在，跳过。")
        return
    log("[2/5] 下载 ffmpeg essentials (含 libass，预览必需) ...")
    zp = os.path.join(tmp, "ff.zip")
    get_zip_checked("https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip", zp, proxy)
    extract_zip(zp, BIN)
    for name in os.listdir(BIN):
        p = os.path.join(BIN, name)
        if os.path.isdir(p) and "ffmpeg" in name.lower() and "essentials" in name.lower():
            final = os.path.join(BIN, "ffmpeg")
            if os.path.isdir(final):
                shutil.rmtree(final, ignore_errors=True)
            shutil.move(p, final)
            break
    log("  -> bin\\ffmpeg")

def _ensure_assfonts(proxy, log, tmp):
    af = os.path.join(BIN, "assfonts", "assfonts.exe")
    if os.path.isfile(af):
        log("[3/5] assfonts 已存在，跳过。")
        return
    log("[3/5] 下载 assfonts v0.7.3 ...")
    zp = os.path.join(tmp, "af.zip")
    get_zip_checked("https://github.com/wyzdwdz/assfonts/releases/download/v0.7.3/assfonts-v0.7.3-x86_64-Windows.zip", zp, proxy)
    dst = os.path.join(BIN, "assfonts")
    os.makedirs(dst, exist_ok=True)
    extract_zip(zp, dst)
    log("  -> bin\\assfonts")

def _ensure_afs(proxy, log, tmp):
    afs = os.path.join(BIN, "assfontsubset", "AssFontSubset.Console.exe")
    if os.path.isfile(afs):
        log("[4/5] AssFontSubset 已存在，跳过。")
        return
    log("[4/5] 下载 AssFontSubset v2.2.0 (win-x64) ...")
    zp = os.path.join(tmp, "afs.zip")
    get_zip_checked("https://github.com/AmusementClub/AssFontSubset/releases/download/v2.2.0/AssFontSubset.Console_v2.2.0_win-x64.zip", zp, proxy)
    dst = os.path.join(BIN, "assfontsubset")
    os.makedirs(dst, exist_ok=True)
    extract_zip(zp, dst)
    log("  -> bin\\assfontsubset")

def _ensure_fonttools(proxy, log, tmp):
    try:
        import fontTools  # noqa: F401
        log("[5/5] fonttools 已安装（%s），跳过。" % fontTools.version)
        return
    except ImportError:
        pass
    log("[5/5] pip 安装 fonttools（AFS 默认后端依赖）...")
    cmd = [sys.executable, "-m", "pip", "install", "fonttools"]
    if proxy:
        cmd += ["--proxy", proxy]
    r = subprocess.run(cmd, capture_output=True, timeout=600)
    if r.returncode != 0:
        log("  pip 直连失败，尝试清华镜像...")
        cmd = [sys.executable, "-m", "pip", "install", "-i",
               "https://pypi.tuna.tsinghua.edu.cn/simple", "fonttools"]
        r = subprocess.run(cmd, capture_output=True, timeout=600)
    out = (_decode(r.stdout) + _decode(r.stderr)).strip()
    for line in out.splitlines():
        log("  " + line.strip())
    if r.returncode != 0:
        log("FAIL: fonttools 安装失败，AFS 默认后端不可用（可手动 pip install fonttools）")
        raise RuntimeError("fonttools 安装失败")
    log("  -> fonttools 就绪")

# ---------- 汇总入口 ----------
STEPS = {
    "mkvtoolnix":     ("MKVToolNix (mkvmerge/mkvextract)", _ensure_mkvtoolnix),
    "ffmpeg":         ("ffmpeg (含 libass)",              _ensure_ffmpeg),
    "assfonts":       ("assfonts (回退子集工具)",          _ensure_assfonts),
    "assfontsubset":  ("AssFontSubset (主用子集工具)",     _ensure_afs),
    "fonttools":      ("fonttools (AFS 后端依赖)",         _ensure_fonttools),
}

def run_bootstrap(proxy="", items=None, log=print):
    """执行引导安装。items: None=全部步骤；否则为 STEPS 的 key 列表（只装缺失的也会自跳过）。
    返回 {"ok": [key...], "fail": [key...]}。log 回调接收每一行进度文本。"""
    os.makedirs(BIN, exist_ok=True)
    todo = list(STEPS) if items is None else [k for k in items if k in STEPS]
    ok, fail = [], []
    tmp = os.path.join(tempfile.gettempdir(), "muxdeck_bootstrap")
    os.makedirs(tmp, exist_ok=True)
    try:
        for key in todo:
            name, fn = STEPS[key]
            try:
                fn(proxy, log, tmp)
                ok.append(key)
            except Exception as ex:
                log("FAIL: %s — %s" % (name, ex))
                fail.append(key)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    return {"ok": ok, "fail": fail}

def main():
    ap = argparse.ArgumentParser(description="Mux Deck 运行时引导")
    ap.add_argument("--proxy", default=os.environ.get("BOOTSTRAP_PROXY", ""))
    a = ap.parse_args()
    print("== Mux Deck 运行时引导 ==", flush=True)
    res = run_bootstrap(a.proxy, None, lambda s: print(s, flush=True))
    print("", flush=True)
    if res["fail"]:
        print("引导未完成：%d 项失败 → %s" % (len(res["fail"]), "、".join(res["fail"])), flush=True)
        sys.exit(1)
    print("引导完成。运行 环境自检.bat 验证，再双击 start_mux_ui.bat 启动。", flush=True)

if __name__ == "__main__":
    try:
        main()
    except Exception as ex:
        print("FAIL: %s" % ex, flush=True)
        sys.exit(1)
