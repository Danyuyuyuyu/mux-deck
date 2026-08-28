# -*- coding: utf-8 -*-
# bootstrap.py — Mux Deck 运行时引导：下载 MKVToolNix / ffmpeg / assfonts 到 bin\（可重复运行，已存在则跳过）
# 用法: py -3 scripts/bootstrap.py [--proxy http://127.0.0.1:7890]
# 也可用环境变量 BOOTSTRAP_PROXY 指定代理。
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

def main():
    ap = argparse.ArgumentParser(description="Mux Deck 运行时引导")
    ap.add_argument("--proxy", default=os.environ.get("BOOTSTRAP_PROXY", ""))
    a = ap.parse_args()
    proxy = a.proxy
    tmp = os.path.join(tempfile.gettempdir(), "muxdeck_bootstrap")
    os.makedirs(tmp, exist_ok=True)
    os.makedirs(BIN, exist_ok=True)

    print("== Mux Deck 运行时引导 ==", flush=True)

    # ---------- 1) MKVToolNix ----------
    mkm = os.path.join(BIN, "mkvtoolnix", "mkvmerge.exe")
    if os.path.isfile(mkm):
        print("[1/5] MKVToolNix 已存在，跳过。", flush=True)
    else:
        print("[1/5] 下载 MKVToolNix 101.0 ...", flush=True)
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
        print("  -> bin\\mkvtoolnix", flush=True)

    # ---------- 2) ffmpeg ----------
    ff = os.path.join(BIN, "ffmpeg", "bin", "ffmpeg.exe")
    if os.path.isfile(ff):
        print("[2/5] ffmpeg 已存在，跳过。", flush=True)
    else:
        print("[2/5] 下载 ffmpeg essentials (含 libass，预览必需) ...", flush=True)
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
        print("  -> bin\\ffmpeg", flush=True)

    # ---------- 3) assfonts ----------
    af = os.path.join(BIN, "assfonts", "assfonts.exe")
    if os.path.isfile(af):
        print("[3/5] assfonts 已存在，跳过。", flush=True)
    else:
        print("[3/5] 下载 assfonts v0.7.3 ...", flush=True)
        zp = os.path.join(tmp, "af.zip")
        get_zip_checked("https://github.com/wyzdwdz/assfonts/releases/download/v0.7.3/assfonts-v0.7.3-x86_64-Windows.zip", zp, proxy)
        dst = os.path.join(BIN, "assfonts")
        os.makedirs(dst, exist_ok=True)
        extract_zip(zp, dst)
        print("  -> bin\\assfonts", flush=True)

    # ---------- 4) AssFontSubset（主用子集工具，双轨之 A 轨） ----------
    afs = os.path.join(BIN, "assfontsubset", "AssFontSubset.Console.exe")
    if os.path.isfile(afs):
        print("[4/5] AssFontSubset 已存在，跳过。", flush=True)
    else:
        print("[4/5] 下载 AssFontSubset v2.2.0 (win-x64) ...", flush=True)
        zp = os.path.join(tmp, "afs.zip")
        get_zip_checked("https://github.com/AmusementClub/AssFontSubset/releases/download/v2.2.0/AssFontSubset.Console_v2.2.0_win-x64.zip", zp, proxy)
        dst = os.path.join(BIN, "assfontsubset")
        os.makedirs(dst, exist_ok=True)
        extract_zip(zp, dst)
        print("  -> bin\\assfontsubset", flush=True)

    # ---------- 5) fonttools（AFS 默认后端 PyFontTools 依赖 pyftsubset/ttx） ----------
    try:
        import fontTools  # noqa: F401
        print("[5/5] fonttools 已安装（%s），跳过。" % fontTools.version, flush=True)
    except ImportError:
        print("[5/5] pip 安装 fonttools（AFS 默认后端依赖）...", flush=True)
        cmd = [sys.executable, "-m", "pip", "install", "fonttools"]
        if proxy:
            cmd += ["--proxy", proxy]
        rc = subprocess.run(cmd).returncode
        if rc != 0:
            print("  pip 直连失败，尝试清华镜像...", flush=True)
            cmd = [sys.executable, "-m", "pip", "install", "-i",
                   "https://pypi.tuna.tsinghua.edu.cn/simple", "fonttools"]
            rc = subprocess.run(cmd).returncode
        if rc != 0:
            print("FAIL: fonttools 安装失败，AFS 默认后端不可用（可手动 pip install fonttools）", flush=True)
            sys.exit(1)
        print("  -> fonttools 就绪", flush=True)

    shutil.rmtree(tmp, ignore_errors=True)
    print("", flush=True)
    print("引导完成。运行 环境自检.bat 验证，再双击 start_mux_ui.bat 启动。", flush=True)

if __name__ == "__main__":
    try:
        main()
    except Exception as ex:
        print("FAIL: %s" % ex, flush=True)
        sys.exit(1)
