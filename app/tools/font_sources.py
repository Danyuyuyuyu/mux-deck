# -*- coding: utf-8 -*-
"""font_sources.py — Windows 系统已装字体作为可选额外字体源。

把 C:\\Windows\\Fonts 与当前用户（HKCU 注册表 per-user 条目）安装的字体，
与用户指定的字体目录合并成一个临时目录供 AFS/assfonts 使用（AFS 的 --fonts
是单目录，且要求目录内同族字体不重复 —— 合并时按字体族去重，用户目录优先）。

独立模块：只依赖标准库与 fontTools，不 import app 包（mux_cli.py 需可独立执行）。
合并目录每次任务重建于临时目录（merged_fonts_<uuid>），任务结束随临时目录清理；
坏字体文件解析失败返回空族名集合，绝不会让合并/体检/补给流程崩溃（跳过处理）。
"""
import os, shutil, uuid

try:
    import winreg
except ImportError:
    winreg = None   # 非 Windows 平台：跳过注册表源

try:
    from fontTools.ttLib import TTFont, TTCollection
except ImportError:
    TTFont = TTCollection = None   # fontTools 缺失时族名解析返回空集合（安全降级）

FONT_EXTS = (".ttf", ".otf", ".ttc", ".otc")

# 系统字体目录（WINDIR 缺省 C:\Windows）
SYSTEM_FONTS_DIR = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts")
_HKCU_FONTS_KEY = r"Software\Microsoft\Windows NT\CurrentVersion\Fonts"


def system_font_sources():
    """系统字体源路径列表：系统字体目录 + HKCU per-user 字体文件路径。

    HKCU 值数据是字体文件完整路径（可能含 %LOCALAPPDATA% 等环境变量，展开后使用）；
    读不到注册表 / 条目异常时跳过 per-user，只留系统目录；不存在的路径过滤掉。
    """
    out = [SYSTEM_FONTS_DIR]
    if winreg is not None:
        try:
            key = winreg.OpenKey(winreg.HKEY_CURRENT_USER, _HKCU_FONTS_KEY)
        except OSError:
            key = None
        if key is not None:
            try:
                i = 0
                while True:
                    try:
                        _, val, _ = winreg.EnumValue(key, i)
                    except OSError:
                        break
                    i += 1
                    if not isinstance(val, str) or not val.strip():
                        continue
                    try:
                        p = os.path.expandvars(val.strip())
                    except Exception:
                        continue
                    if os.path.isfile(p) and p.lower() not in {x.lower() for x in out}:
                        out.append(p)
            except OSError:
                pass
            finally:
                try:
                    key.Close()
                except Exception:
                    pass
    return [p for p in out if os.path.exists(p)]


def family_names(font_path):
    """字体文件的族名集合（name 表 nameID 1/4/6/16，与体检/收集同口径，小写）。

    解析失败（坏字体 / fontTools 缺失）返回空集合 —— 坏字体免疫，不崩溃。
    """
    names = set()
    if TTFont is None:
        return names
    try:
        if font_path.lower().endswith((".ttc", ".otc")):
            fonts = list(TTCollection(font_path, lazy=True).fonts)
        else:
            fonts = [TTFont(font_path, fontNumber=0, lazy=True)]
    except Exception:
        return names
    for f in fonts:
        try:
            for rec in f["name"].names:
                if rec.nameID in (1, 4, 6, 16):
                    s = str(rec).strip().lower()
                    if s:
                        names.add(s)
        except Exception:
            pass
        finally:
            try:
                f.close()
            except Exception:
                pass
    return names


def _place(src, dst_dir):
    """字体文件进合并目录：优先硬链接（零拷贝），跨卷/失败回退 shutil.copy2。"""
    dst = os.path.join(dst_dir, os.path.basename(src))
    if os.path.exists(dst):
        return dst
    try:
        os.link(src, dst)
        return dst
    except OSError:
        pass
    try:
        shutil.copy2(src, dst)
        return dst
    except OSError:
        return None


def _source_files(src):
    """一个字体源（目录 / 单文件）-> 其中字体文件路径列表；读不了的源返回 []。"""
    if os.path.isdir(src):
        try:
            return [os.path.join(src, fn) for fn in sorted(os.listdir(src))
                    if os.path.isfile(os.path.join(src, fn))
                    and os.path.splitext(fn)[1].lower() in FONT_EXTS]
        except OSError:
            return []
    if os.path.isfile(src) and os.path.splitext(src)[1].lower() in FONT_EXTS:
        return [src]
    return []


def build_merged_font_dir(user_dir, use_sys, tmp_root):
    """构建「合并字体目录」并返回其路径。

    - use_sys 为假、user_dir 为空或不存在 -> 原样返回 user_dir（零开销路径，不多扫任何盘）。
    - 否则在 tmp_root 下新建 merged_fonts_<uuid> 目录：
        1) 用户目录全部字体文件硬链接（失败回退复制）进去 —— 用户目录优先；
        2) 扫 system_font_sources() 各源：族名与已并入族名集合有交集的文件跳过
           （避免 AFS 报 Duplicate fonts），无族名（坏字体）的文件也跳过；
        3) 无冲突的文件硬链接/复制进去。
    单个文件读写失败只跳过该文件，不让整体崩溃。
    """
    if not use_sys or not user_dir or not os.path.isdir(user_dir):
        return user_dir
    merged = os.path.join(tmp_root, "merged_fonts_" + uuid.uuid4().hex[:8])
    os.makedirs(merged, exist_ok=True)
    merged_fams = set()
    # 1) 用户目录全部字体（不解析校验、原样并入 —— 与现状行为一致）
    try:
        entries = sorted(os.listdir(user_dir))
    except OSError:
        entries = []
    for fn in entries:
        src = os.path.join(user_dir, fn)
        if not os.path.isfile(src) or os.path.splitext(fn)[1].lower() not in FONT_EXTS:
            continue
        if _place(src, merged):
            merged_fams |= family_names(src)
    # 2) 系统字体源：族名冲突（含用户目录已覆盖的族）跳过，无族名坏文件跳过
    for src in system_font_sources():
        for fp in _source_files(src):
            fams = family_names(fp)
            if not fams or not merged_fams.isdisjoint(fams):
                continue
            if _place(fp, merged):
                merged_fams |= fams
    return merged
