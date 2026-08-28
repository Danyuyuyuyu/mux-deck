# -*- coding: utf-8 -*-
# batch_cli.py — 目录级批量：自动按集数匹配 sc/tc 字幕、子集化并封装（ass_subset_mux.ps1 的 Python 继任者）
# 用法: py -3 scripts/batch_cli.py --root "目标目录" [--fonts-dir D] [--out-dir D] [--force]
# 目标目录内应包含 MKV + .sc.ass/.tc.ass + Fonts（或 Font）文件夹。
# 默认替换模式：原 MKV 移入视频旁 __mux_tmp_manual（同名加序号，永不覆盖）；--out-dir 则输出到指定目录（同名冲突拒绝）。
import argparse, json, os, re, shutil, subprocess, sys, tempfile

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

def first_existing(*paths):
    for p in paths:
        if p and os.path.isfile(p):
            return p
    return None

ASSFONTS = first_existing(os.path.join(BASE, "bin", "assfonts", "assfonts.exe")) \
    or shutil.which("assfonts") or os.path.join(BASE, "bin", "assfonts", "assfonts.exe")
MKVMERGE = first_existing(os.path.join(BASE, "bin", "mkvtoolnix", "mkvmerge.exe")) \
    or shutil.which("mkvmerge") or r"C:\Program Files\MKVToolNix\mkvmerge.exe"

EP_RE = [re.compile(r'S(\d{1,2})E(\d{1,3})', re.I),
         re.compile(r'\[(\d{1,3})\]'),
         re.compile(r'[-_]\s*(\d{1,3})(?![0-9])'),
         re.compile(r'EP(\d{1,3})', re.I),
         re.compile(r'(?<![0-9A-Za-z])E(\d{1,3})(?![0-9A-Za-z])', re.I),
         re.compile(r'(\d{1,3})(?![0-9])$')]

def ep_of(name):
    for pat in EP_RE:
        m = pat.search(name)
        if m:
            return int(m.groups()[-1])
    return -1

def is_utf8(path):
    try:
        with open(path, "rb") as f:
            data = f.read()
    except OSError:
        return True
    if data[:3] == bytes([239, 187, 191]):
        return True
    try:
        data.decode("utf-8")
        return True
    except UnicodeDecodeError:
        return False

def probe_json(path):
    out = subprocess.run([MKVMERGE, "-J", "--ui-language", "en", path], capture_output=True)
    if out.returncode != 0:
        return {}
    try:
        return json.loads(out.stdout.decode("utf-8", errors="replace"))
    except Exception:
        return {}

def unique_path(dest):
    if not os.path.exists(dest):
        return dest
    d = os.path.dirname(dest)
    stem, ext = os.path.splitext(os.path.basename(dest))
    n = 1
    while True:
        cand = os.path.join(d, "%s_%d%s" % (stem, n, ext))
        if not os.path.exists(cand):
            return cand
        n += 1

def main():
    ap = argparse.ArgumentParser(description="Mux Deck 目录级批量子集封装")
    ap.add_argument("--root", required=True, help="包含 MKV + sc/tc ass + Fonts 的目标目录")
    ap.add_argument("--fonts-dir", default="")
    ap.add_argument("--out-dir", default="")
    ap.add_argument("--sc-lang", default="zh-Hans")
    ap.add_argument("--tc-lang", default="zh-Hant")
    ap.add_argument("--sc-name", default="SC")
    ap.add_argument("--tc-name", default="TC")
    ap.add_argument("--force", action="store_true", help="重新处理已含字体附件的 MKV")
    ap.add_argument("--sc-default", default="", help="覆盖 SC 默认轨旗标: 0|1（空=自动：SC 在则默认）")
    ap.add_argument("--tc-default", default="", help="覆盖 TC 默认轨旗标: 0|1（空=自动）")
    ap.add_argument("--sc-forced", action="store_true", help="SC 轨打 forced 旗标")
    ap.add_argument("--tc-forced", action="store_true", help="TC 轨打 forced 旗标")
    a = ap.parse_args()

    root = a.root
    if not os.path.isdir(root):
        print("Root not found: " + root, flush=True)
        sys.exit(1)
    fonts_dir = a.fonts_dir
    if not fonts_dir:
        for cand in ("Fonts", "Font"):
            p = os.path.join(root, cand)
            if os.path.isdir(p):
                fonts_dir = p
                break
    if not os.path.isdir(fonts_dir or ""):
        print("Fonts dir not found: %s" % (fonts_dir or "(none)"), flush=True)
        sys.exit(1)
    if not os.path.isfile(ASSFONTS or ""):
        print("assfonts not found; expected in bin\\assfonts or on PATH", flush=True)
        sys.exit(1)

    work = os.path.join(tempfile.gettempdir(), "ass_subset_mux")
    os.makedirs(work, exist_ok=True)

    print("Building fonts DB from: " + fonts_dir, flush=True)
    build_log = os.path.join(work, "assfonts_build.log")
    with open(build_log, "wb") as f:
        rc = subprocess.run([ASSFONTS, "-f", fonts_dir, "-b", "-d", work], stdout=f, stderr=subprocess.STDOUT).returncode
    if rc != 0:
        print("FATAL: assfonts 建库失败 (exit %d)，请检查字体目录后重试" % rc, flush=True)
        print("日志位置: " + build_log, flush=True)
        sys.exit(1)

    mkvs = sorted(fn for fn in os.listdir(root) if fn.lower().endswith(".mkv"))
    if not mkvs:
        print("No MKV found.", flush=True)
        sys.exit(0)
    print("Found %d MKV(s)." % len(mkvs), flush=True)

    ok = skip = failed = 0
    for name in mkvs:
        mkv = os.path.join(root, name)
        base_name = os.path.splitext(name)[0]
        print("===== %s =====" % name, flush=True)
        info = probe_json(mkv)
        has_font = any("font" in str(att.get("mime_type") or "").lower()
                       for att in (info.get("attachments") or []))
        if has_font and not a.force:
            print("SKIP (already has fonts)", flush=True)
            skip += 1
            continue

        # --- 按集数匹配 sc/tc 字幕 ---
        mkv_ep = ep_of(base_name)
        sc = tc = ""
        d = os.path.dirname(mkv)
        for kind in ("sc", "tc"):
            cand = os.path.join(d, "%s.%s.ass" % (base_name, kind))
            if os.path.isfile(cand):
                if kind == "sc":
                    sc = cand
                else:
                    tc = cand
        if (not sc or not tc) and mkv_ep >= 0:
            try:
                cands = [fn for fn in os.listdir(d) if fn.lower().endswith(".ass")]
            except OSError:
                cands = []
            for cn in cands:
                cbase = os.path.splitext(cn)[0]
                if cn.lower() in ("%s.sc.ass" % base_name.lower(), "%s.tc.ass" % base_name.lower()):
                    continue
                if ep_of(cbase) != mkv_ep:
                    continue
                if re.search(r'\.sc\.ass$', cn, re.I) and not sc:
                    sc = os.path.join(d, cn)
                    print("  matched by ep#: " + cn, flush=True)
                if re.search(r'\.tc\.ass$', cn, re.I) and not tc:
                    tc = os.path.join(d, cn)
                    print("  matched by ep#: " + cn, flush=True)
        if not sc and not tc:
            print("SKIP (no matching sc/tc ass, ep#=%d)" % mkv_ep, flush=True)
            skip += 1
            continue

        # 编码检查：CLI 路径不做编码转换，非 UTF-8 只醒目警告、不中断
        for s in (sc, tc):
            if s and not is_utf8(s):
                print("  WARNING: %s 该字幕不是 UTF-8，CLI 路径不做编码转换，建议先用 Web UI 的编码检查/转换" % s, flush=True)

        ep_tag = re.sub(r'[\\/:*?"<>|\[\]()]', "_", base_name)
        ep_work = os.path.join(work, ep_tag)
        os.makedirs(ep_work, exist_ok=True)
        sf = os.path.join(ep_work, "sf")
        os.makedirs(sf, exist_ok=True)
        sub_args = [s for s in (sc, tc) if s]
        sub_log = os.path.join(ep_work, "subset.log")
        with open(sub_log, "wb") as f:
            subprocess.run([ASSFONTS, "-f", fonts_dir, "-d", work, "-o", sf, "-s", "-c", "-i"] + sub_args,
                           stdout=f, stderr=subprocess.STDOUT)
        sfdir = os.path.join(sf, "subsetted_fonts")
        fonts = []
        if os.path.isdir(sfdir):
            fonts = [os.path.join(sfdir, fn) for fn in os.listdir(sfdir)
                     if os.path.splitext(fn)[1].lower() in (".ttf", ".otf", ".ttc", ".otc", ".woff", ".woff2")]
        if not fonts:
            print("FAIL (subset produced no fonts)", flush=True)
            failed += 1
            continue
        print("  subsetted %d fonts" % len(fonts), flush=True)

        if a.out_dir:
            os.makedirs(a.out_dir, exist_ok=True)
            out = os.path.join(a.out_dir, name)
            if os.path.exists(out):
                print("FAIL (同名冲突: %s 已存在，不覆盖)" % out, flush=True)
                failed += 1
                continue
        else:
            out = os.path.join(ep_work, "out.mkv")
        margs = ["-o", out, "--ui-language", "en", "-S", "-M", mkv]
        sc_def, tc_def = "0:1", "0:0"
        if not sc and tc:
            tc_def = "0:1"
        if a.sc_default in ("0", "1"):
            sc_def = "0:" + a.sc_default
        if a.tc_default in ("0", "1"):
            tc_def = "0:" + a.tc_default
        if sc:
            margs += ["--language", "0:" + a.sc_lang, "--track-name", "0:" + a.sc_name,
                      "--default-track-flag", sc_def,
                      "--forced-display-flag", "0:1" if a.sc_forced else "0:0", sc]
        if tc:
            margs += ["--language", "0:" + a.tc_lang, "--track-name", "0:" + a.tc_name,
                      "--default-track-flag", tc_def,
                      "--forced-display-flag", "0:1" if a.tc_forced else "0:0", tc]
        for fp in fonts:
            margs += ["--attach-file", fp]
        mux_log = os.path.join(ep_work, "mux.log")
        with open(mux_log, "wb") as f:
            rc = subprocess.run([MKVMERGE] + margs, stdout=f, stderr=subprocess.STDOUT).returncode
        if rc != 0:
            print("FAIL (mux error)，日志: " + mux_log, flush=True)
            failed += 1
            continue

        vid = probe_json(out)
        new_subs = [t for t in (vid.get("tracks") or []) if t.get("type") == "subtitles"]
        if not new_subs or not (vid.get("attachments") or []):
            print("FAIL (verify)", flush=True)
            failed += 1
            continue

        if not a.out_dir:
            # 替换语义：原片移入视频旁 __mux_tmp_manual（同名加序号，永不覆盖已有备份）
            bak_dir = os.path.join(d, "__mux_tmp_manual")
            os.makedirs(bak_dir, exist_ok=True)
            bak_dest = unique_path(os.path.join(bak_dir, name))
            try:
                shutil.move(mkv, bak_dest)
                shutil.move(out, mkv)
                print("  original kept in: " + bak_dest, flush=True)
            except Exception as ex:
                if os.path.exists(bak_dest) and not os.path.exists(mkv):
                    try:
                        shutil.move(bak_dest, mkv)
                    except Exception:
                        pass
                print("FAIL (替换失败，原片已还原): %s" % ex, flush=True)
                failed += 1
                continue
        print("OK: subs=%d fonts=%d" % (len(new_subs), len(vid.get("attachments") or [])), flush=True)
        ok += 1

    print("", flush=True)
    print("===== DONE: OK=%d SKIP=%d FAIL=%d =====" % (ok, skip, failed), flush=True)
    sys.exit(0 if failed == 0 else 1)

if __name__ == "__main__":
    main()
