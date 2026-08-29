# -*- coding: utf-8 -*-
# mux_cli.py — 单任务封装编排（ass_mux_manual.ps1 的 Python 继任者）
# 独立运行：py -3 app\tools\mux_cli.py --video V [--sc-sub A] [--tc-sub B] [--fonts-dir D] ...
# 行为与旧 ps1 完全对齐：assfonts 两阶段子集化 -> mkvmerge 组装 -> 校验 -> 安装(替换/备份)。
import argparse, json, os, re, shutil, subprocess, sys, tempfile, uuid

BASE = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))  # 项目根（本文件位于 app/tools/）

TMP = ""

def fail(msg):
    print("FAIL: " + msg, flush=True)
    global TMP
    if TMP and os.path.isdir(TMP):
        shutil.rmtree(TMP, ignore_errors=True)
    sys.exit(1)

def first_existing(*paths):
    for p in paths:
        if p and os.path.isfile(p):
            return p
    return None

MKVMERGE = first_existing(os.path.join(BASE, "bin", "mkvtoolnix", "mkvmerge.exe")) \
    or shutil.which("mkvmerge") or r"C:\Program Files\MKVToolNix\mkvmerge.exe"
ASSFONTS = first_existing(os.path.join(BASE, "bin", "assfonts", "assfonts.exe")) \
    or shutil.which("assfonts") or os.path.join(BASE, "bin", "assfonts", "assfonts.exe")
AFS = first_existing(os.path.join(BASE, "bin", "assfontsubset", "AssFontSubset.Console.exe")) \
    or shutil.which("AssFontSubset.Console") or ""
PY_SCRIPTS = os.path.join(os.path.dirname(sys.executable), "Scripts")

def subset_tool():
    """双轨配置：config.json 的 subset_tool（默认 afs）；AFS 缺失时回落 assfonts。"""
    tool = "afs"
    try:
        with open(os.path.join(BASE, "app", "config.json"), encoding="utf-8") as f:
            c = json.load(f)
        if c.get("subset_tool") in ("afs", "assfonts"):
            tool = c["subset_tool"]
    except Exception:
        pass
    if tool == "afs" and not AFS:
        return "assfonts"
    return tool

def run_stream(cmd, log_path=None):
    """stdout 直通（服务器重定向到任务日志 / CLI 直跑则上屏），可选同时 tee 一份到日志文件。"""
    if log_path:
        with open(log_path, "wb") as f:
            proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
            while True:
                chunk = proc.stdout.read(4096)
                if not chunk:
                    break
                f.write(chunk)
                f.flush()
                try:
                    sys.stdout.buffer.write(chunk)
                    sys.stdout.buffer.flush()
                except Exception:
                    pass
            rc = proc.wait()
    else:
        proc = subprocess.Popen(cmd)
        rc = proc.wait()
    return rc

def _tail_text(path, limit=20000):
    try:
        with open(path, "rb") as f:
            return f.read().decode("utf-8", errors="replace")[-limit:]
    except Exception:
        return ""

def _afs_fail_reason(txt):
    """AFS 子集化失败时从其输出里分类根因（与体检端 fonts.py 同口径）。"""
    m = re.search(r"Command execution failed:.*?pyftsubset (\S+)", txt)
    if m:
        return ("AFS 子集化失败：字体文件 %s 不规范或损坏，无法解析"
                "——建议在高级选项把子集化工具切换为 assfonts 后重试" % os.path.basename(m.group(1)))
    if "Duplicate fonts" in txt or "duplicate fonts" in txt:
        return "AFS 子集化失败：字体目录存在重复字体（AFS 要求同族同样式只保留一份）——请精简字体目录或切换 assfonts"
    names = []
    for line in txt.splitlines():
        if "Not found font file:" in line:
            rest = line.split("Not found font file:", 1)[1].strip()
            for item in rest.split("、"):
                n = item.split(",", 1)[0].strip().lstrip("@")
                if n and n not in names:
                    names.append(n)
    if names:
        more = "等 %d 个" % len(names) if len(names) > 8 else ""
        return "AFS 子集化失败：缺字体 %s%s——请先做字体体检并补给，或补齐字体目录" % ("、".join(names[:8]), more)
    m = re.search(r"Return exitcode (-?\d+)", txt)
    return "AFS 子集化失败（退出码 %s），详见任务日志" % (m.group(1) if m else "?")

def probe_json(path):
    out = subprocess.run([MKVMERGE, "-J", path], capture_output=True)
    if out.returncode != 0:
        fail("读取视频信息失败（mkvmerge -J）: " + path)
    try:
        return json.loads(out.stdout.decode("utf-8", errors="replace"))
    except Exception as ex:
        fail("视频信息解析失败: %s" % ex)

def unique_path(dest):
    if not os.path.exists(dest):
        return dest
    d = os.path.dirname(dest)
    stem, ext = os.path.splitext(os.path.basename(dest))
    n = 1
    while True:
        cand = os.path.join(d, "%s.%d%s" % (stem, n, ext))
        if not os.path.exists(cand):
            return cand
        n += 1

def main():
    global TMP
    ap = argparse.ArgumentParser(description="Mux Deck 单任务封装编排")
    ap.add_argument("--video", required=True)
    ap.add_argument("--sc-sub", default="")
    ap.add_argument("--tc-sub", default="")
    ap.add_argument("--fonts-dir", default="")
    ap.add_argument("--audio", default="all")
    ap.add_argument("--audio-lang", default="")
    ap.add_argument("--audio-name", default="")
    ap.add_argument("--sc-lang", default="zh-Hans")
    ap.add_argument("--sc-name", default="SC")
    ap.add_argument("--tc-lang", default="zh-Hant")
    ap.add_argument("--tc-name", default="TC")
    ap.add_argument("--out-dir", default="")
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--no-backup", action="store_true")
    ap.add_argument("--audio-tracks", default="")
    ap.add_argument("--subtitle-tracks", default="")
    ap.add_argument("--keep-attachments", action="store_true")
    ap.add_argument("--sc-default", default="", help="覆盖 SC 默认轨旗标: 0|1（空=自动：SC 在则默认）")
    ap.add_argument("--tc-default", default="", help="覆盖 TC 默认轨旗标: 0|1（空=自动）")
    ap.add_argument("--sc-forced", action="store_true", help="SC 轨打 forced 旗标")
    ap.add_argument("--tc-forced", action="store_true", help="TC 轨打 forced 旗标")
    a = ap.parse_args()

    # ---------- 校验输入 ----------
    video = a.video
    if not os.path.isfile(video):
        fail("视频文件不存在: " + video)
    if not os.path.isfile(MKVMERGE or ""):
        fail("找不到 mkvmerge——请确认 bin\\mkvtoolnix 存在、已安装 MKVToolNix 或已加入 PATH")
    video_dir = os.path.dirname(os.path.abspath(video))
    base = os.path.splitext(os.path.basename(video))[0]
    ext = os.path.splitext(video)[1]

    fonts_dir = a.fonts_dir
    if not fonts_dir:
        for cand in ("Fonts", "Font"):
            p = os.path.join(video_dir, cand)
            if os.path.isdir(p):
                fonts_dir = p
                break
    subs = []
    if a.sc_sub:
        if not os.path.isfile(a.sc_sub):
            fail("简体字幕文件不存在: " + a.sc_sub)
        subs.append(a.sc_sub)
    if a.tc_sub:
        if not os.path.isfile(a.tc_sub):
            fail("繁体字幕文件不存在: " + a.tc_sub)
        subs.append(a.tc_sub)
    if subs and not fonts_dir:
        fail("未指定字体目录，且视频旁未找到 Fonts/Font 目录；请用 --fonts-dir")
    if fonts_dir and not os.path.isdir(fonts_dir):
        fail("字体目录不存在: " + fonts_dir)

    print("Source : " + video, flush=True)
    print("Fonts  : " + (fonts_dir or "(none)"), flush=True)
    print("Subs   : " + (", ".join(subs) if subs else "(none - source subtitles kept)"), flush=True)

    # ---------- 源信息 ----------
    j = probe_json(video)
    attach_count = len(j.get("attachments") or [])
    # 仅在将重建附件（给了新 ASS 字幕）时守卫
    if subs and attach_count > 0 and not a.force and not a.keep_attachments:
        fail("源视频自带 %d 个附件；如确要重封装请勾选强制封装，或勾选保留源附件" % attach_count)

    # ---------- 临时工作目录 ----------
    TMP = os.path.join(tempfile.gettempdir(), "manual_mux_" + uuid.uuid4().hex)
    os.makedirs(TMP, exist_ok=True)
    subset_dir = os.path.join(TMP, "subs")
    os.makedirs(subset_dir, exist_ok=True)

    # ---------- 子集化（双轨：AFS 主用，assfonts 回退） ----------
    tool = subset_tool() if subs else "-"
    if subs:
        print("Subset tool: " + tool, flush=True)
    if subs and tool == "afs":
        # AFS：每个字幕独立一次调用（AFS 要求多文件同目录，且修正后的 ASS 按输入basename落在输出目录）
        corrected = []
        for i, sub in enumerate(subs):
            out_i = os.path.join(TMP, "afs_%d" % i)
            print("AFS: subsetting %s ..." % os.path.basename(sub), flush=True)
            rc = run_stream([AFS, sub, "--fonts", fonts_dir, "--output", out_i,
                             "--bin-path", PY_SCRIPTS], os.path.join(TMP, "afs_%d.log" % i))
            if rc != 0:
                fail(_afs_fail_reason(_tail_text(os.path.join(TMP, "afs_%d.log" % i))))
            fixed = os.path.join(out_i, os.path.basename(sub))
            if not os.path.isfile(fixed):
                fail("AFS 未产出修正字幕: " + fixed)
            corrected.append(fixed)
        subs = corrected
        if a.sc_sub:
            a.sc_sub = corrected[0]
        if a.tc_sub:
            a.tc_sub = corrected[-1]
        fonts = []
        for i in range(len(subs)):
            out_i = os.path.join(TMP, "afs_%d" % i)
            for fn in os.listdir(out_i):
                if os.path.splitext(fn)[1].lower() in (".ttf", ".otf", ".ttc", ".otc"):
                    fonts.append(os.path.join(out_i, fn))
    elif subs:
        if not os.path.isfile(ASSFONTS or ""):
            fail("找不到 assfonts——请确认 bin\\assfonts 存在或已加入 PATH")
        print("assfonts: building database and subsetting fonts...", flush=True)
        db_dir = os.path.join(TMP, "db")
        os.makedirs(db_dir, exist_ok=True)
        rc = run_stream([ASSFONTS, "-f", fonts_dir, "-b", "-d", db_dir],
                        os.path.join(TMP, "assfonts_build.log"))
        if rc != 0:
            fail("assfonts 字体库构建失败（退出码 %d），详见任务日志" % rc)
        if not os.path.isfile(os.path.join(db_dir, "fonts.json")):
            fail("assfonts 字体库未生成: " + db_dir)
        rc = run_stream([ASSFONTS, "-f", fonts_dir, "-s", "-c", "-d", db_dir, "-o", subset_dir] + subs,
                        os.path.join(TMP, "assfonts_subset.log"))
        if rc != 0:
            names = re.findall(r'Missing the font "([^"]+)"', _tail_text(os.path.join(TMP, "assfonts_subset.log")))
            if names:
                fail("assfonts 子集化失败：缺字体 " + "、".join(dict.fromkeys(names)))
            fail("assfonts 子集化失败（退出码 %d），详见任务日志中的 assfonts 输出" % rc)
        fonts = []
        sf = os.path.join(subset_dir, "subsetted_fonts")
        if os.path.isdir(sf):
            for fn in os.listdir(sf):
                if os.path.splitext(fn)[1].lower() in (".ttf", ".otf", ".ttc", ".otc", ".woff", ".woff2"):
                    fonts.append(os.path.join(sf, fn))
    else:
        fonts = []
    print("Fonts to embed: %d" % len(fonts), flush=True)

    # ---------- 音频选择（源轨与外部文件正交） ----------
    audio_args = []
    ext_audio = ""
    if a.audio_tracks == "none":
        audio_args.append("--no-audio")
    elif a.audio_tracks:
        audio_args += ["--audio-tracks", a.audio_tracks]
    if a.audio and os.path.isfile(a.audio):
        ext_audio = a.audio
    elif not a.audio_tracks and a.audio:
        if a.audio == "none":
            audio_args.append("--no-audio")
        elif a.audio != "all":
            if all(ch in "0123456789, " for ch in a.audio):
                audio_args += ["--audio-tracks", a.audio]
            else:
                langs = [s.strip().lower() for s in a.audio.split(",") if s.strip()]
                ids = [str(t.get("id")) for t in (j.get("tracks") or [])
                       if t.get("type") == "audio"
                       and str((t.get("properties") or {}).get("language") or "").lower() in langs]
                if not ids:
                    fail("没有语言为 %s 的源音轨" % a.audio)
                audio_args += ["--audio-tracks", ",".join(ids)]

    # ---------- 组装 mkvmerge ----------
    out_tmp = os.path.join(TMP, base + ".muxed" + ext)
    margs = ["-o", out_tmp]
    if a.subtitle_tracks:
        margs += ["--subtitle-tracks", a.subtitle_tracks]
    elif subs:
        margs.append("--no-subtitles")
    # 没有新子集字体 -> 保留源附件（mkvmerge 默认即拷贝）
    if subs and not a.keep_attachments:
        margs.append("--no-attachments")
    margs += audio_args
    margs.append(video)
    sc_def, tc_def = "0:1", "0:0"
    if not a.sc_sub and a.tc_sub:
        tc_def = "0:1"
    if a.sc_default in ("0", "1"):
        sc_def = "0:" + a.sc_default
    if a.tc_default in ("0", "1"):
        tc_def = "0:" + a.tc_default
    if a.sc_sub:
        margs += ["--language", "0:" + a.sc_lang, "--track-name", "0:" + a.sc_name,
                  "--default-track-flag", sc_def]
        if a.sc_forced:
            margs += ["--forced-display-flag", "0:1"]
        margs.append(a.sc_sub)
    if a.tc_sub:
        margs += ["--language", "0:" + a.tc_lang, "--track-name", "0:" + a.tc_name,
                  "--default-track-flag", tc_def]
        if a.tc_forced:
            margs += ["--forced-display-flag", "0:1"]
        margs.append(a.tc_sub)
    if ext_audio:
        if a.audio_lang:
            margs += ["--language", "0:" + a.audio_lang]
        if a.audio_name:
            margs += ["--track-name", "0:" + a.audio_name]
        margs.append(ext_audio)
    for fpath in fonts:
        fext = os.path.splitext(fpath)[1].lower()
        mime = "application/font-woff" if fext in (".woff", ".woff2") else "application/x-truetype-font"
        margs += ["--attachment-mime-type", mime, "--attach-file", fpath]

    print("Muxing...", flush=True)
    rc = run_stream([MKVMERGE] + margs, os.path.join(TMP, "mux.log"))
    if rc != 0:
        mtxt = _tail_text(os.path.join(TMP, "mux.log"))
        errs = re.findall(r"^(?:Error|错误)[:：]\s*(.+)$", mtxt, re.M)
        if errs:
            fail("mkvmerge 失败：" + errs[-1].strip()[:300])
        fail("mkvmerge 失败（退出码 %d），详见任务日志中的 mkvmerge 输出" % rc)

    # ---------- 校验 ----------
    vo = probe_json(out_tmp)
    st = len([t for t in (vo.get("tracks") or []) if t.get("type") == "subtitles"])
    if a.subtitle_tracks:
        keep_ids = {s.strip() for s in a.subtitle_tracks.split(",") if s.strip()}
        kept = len([t for t in (j.get("tracks") or [])
                    if t.get("type") == "subtitles" and str(t.get("id")) in keep_ids])
    elif not subs:
        kept = len([t for t in (j.get("tracks") or []) if t.get("type") == "subtitles"])
    else:
        kept = 0
    expect = len(subs) + kept
    if st != expect:
        fail("封装校验失败：预期 %d 条字幕轨，实际 %d 条" % (expect, st))
    print("--- Result ---", flush=True)
    for tr in (vo.get("tracks") or []):
        pr = tr.get("properties") or {}
        print("  track %s: %s  lang=%s  name=%s  default=%s" % (
            tr.get("id"), tr.get("type"), pr.get("language") or "-",
            pr.get("track_name") or "", bool(pr.get("default_track"))), flush=True)
    print("  attachments: %d" % len(vo.get("attachments") or []), flush=True)

    # ---------- 安装 ----------
    if a.out_dir:
        os.makedirs(a.out_dir, exist_ok=True)
        dest = os.path.join(a.out_dir, base + ext)
        shutil.move(out_tmp, dest)
        print("OK -> " + dest, flush=True)
    else:
        dest = os.path.join(video_dir, base + ext)
        if a.no_backup:
            # 安全顺序：原件先进临时区 -> 成品落位 -> 成功后才删原件
            staged = os.path.join(TMP, base + ext)
            try:
                shutil.move(video, staged)
            except Exception as ex:
                fail("无法暂存原件（可能被占用）: %s" % ex)
            try:
                shutil.move(out_tmp, dest)
            except Exception as ex:
                err = str(ex)
                try:
                    shutil.move(staged, video)
                except Exception:
                    pass
                if os.path.exists(staged):
                    rescue = unique_path(os.path.join(video_dir, base + ".restore_failed" + ext))
                    try:
                        shutil.move(staged, rescue)
                    except Exception:
                        pass
                    if os.path.exists(rescue):
                        fail("成品安装失败，且原件无法放回原位，已救援至 %s: %s" % (rescue, err))
                    else:
                        TMP = ""  # 保住临时目录，原件还在里面
                        fail("成品安装失败，原件保留在 %s（临时目录未删除）: %s" % (staged, err))
                else:
                    fail("成品安装失败，原件已放回原位: " + err)
            try:
                os.remove(staged)
            except OSError:
                pass
            print("OK -> " + dest + "  (original deleted, no backup)", flush=True)
        else:
            bak_dir = os.path.join(video_dir, "__mux_tmp_manual")
            os.makedirs(bak_dir, exist_ok=True)
            # 永不覆盖已有备份：取第一个空闲名字
            bak_dest = unique_path(os.path.join(bak_dir, base + ext))
            shutil.move(video, bak_dest)
            try:
                shutil.move(out_tmp, dest)
            except Exception as ex:
                err = str(ex)
                try:
                    shutil.move(bak_dest, video)
                except Exception:
                    pass
                if os.path.exists(bak_dest):
                    fail("成品安装失败，且备份无法移回原位（留在 %s）: %s" % (bak_dest, err))
                else:
                    fail("成品安装失败，备份已移回原位置: " + err)
            print("OK -> " + dest + "  (original kept in " + bak_dest + ")", flush=True)

    shutil.rmtree(TMP, ignore_errors=True)
    TMP = ""

if __name__ == "__main__":
    main()
