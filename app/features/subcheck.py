# -*- coding: utf-8 -*-
# 字幕内容体检：POST /api/sub_check（时间轴重叠/空行/坏时间/坏样式 + CPS/行宽预警；纯文本分析，不动文件）
import os, re


def _parse_ts(s):
    """ASS 时间 H:MM:SS.CS -> 秒；无法解析返回 None。"""
    m = re.match(r"^(\d+):(\d{1,2}):(\d{1,2})[.,](\d{1,3})$", s.strip())
    if not m:
        return None
    h, mi, se, fr = (int(x) for x in m.groups())
    return h * 3600 + mi * 60 + se + fr / (10.0 ** len(m.group(4)))


def check_sub(sub, cps_limit=15.0, line_limit=25):
    """返回 {counts, issues, ...}；软预警不打分不拦截，供校对/时轴参考。"""
    if not sub or not os.path.isfile(sub):
        return {"error": "字幕文件不存在"}
    try:
        with open(sub, encoding="utf-8-sig", errors="replace") as f:
            lines = f.read().splitlines()
    except OSError as ex:
        return {"error": "读取失败: %s" % ex}

    styles = set()
    for ln in lines:
        if ln.startswith("Style:"):
            # 样式名在 "Style:" 前缀与首个逗号之间（其后才是字体名等字段）
            styles.add(ln[len("Style:"):].split(",")[0].strip().lower())

    counts = {"overlap": 0, "empty": 0, "bad_time": 0, "bad_style": 0, "cps": 0, "long_line": 0}
    issues = []
    entries = []   # (start, end, lineno)
    n_dialogue = 0
    for idx, ln in enumerate(lines, 1):
        if not ln.startswith("Dialogue:"):
            continue
        n_dialogue += 1
        parts = ln.split(",", 9)
        if len(parts) < 10:
            counts["bad_time"] += 1
            issues.append({"line": idx, "type": "bad_time", "detail": "Dialogue 字段数不足（%d）" % len(parts)})
            continue
        s, e = _parse_ts(parts[1]), _parse_ts(parts[2])
        if s is None or e is None:
            counts["bad_time"] += 1
            issues.append({"line": idx, "type": "bad_time", "detail": "时间无法解析: %s → %s" % (parts[1].strip(), parts[2].strip())})
            continue
        if e <= s:
            counts["bad_time"] += 1
            issues.append({"line": idx, "type": "bad_time", "detail": "结束不晚于开始（时长 %.2fs）" % (e - s)})
            continue
        style = parts[3].strip()
        if styles and style.lower() not in styles:
            counts["bad_style"] += 1
            issues.append({"line": idx, "type": "bad_style", "detail": "引用未定义样式: %s" % style})
        clean = re.sub(r"\{[^}]*\}", "", parts[9])            # 去掉 {} 覆写标签
        disp = [seg.strip() for seg in clean.split("\\N") if seg.strip()]
        chars = sum(len(seg) for seg in disp)
        dur = e - s
        if not disp or not chars:
            counts["empty"] += 1
            issues.append({"line": idx, "type": "empty", "detail": "空台词（无可见文字）"})
        else:
            cps = chars / dur
            if cps > cps_limit:
                counts["cps"] += 1
                issues.append({"line": idx, "type": "cps", "detail": "CPS %.1f 超过 %.0f（%d 字 / %.2fs）" % (cps, cps_limit, chars, dur)})
            for seg in disp:
                if len(seg) > line_limit:
                    counts["long_line"] += 1
                    issues.append({"line": idx, "type": "long_line", "detail": "单行 %d 字超 %d：%s…" % (len(seg), line_limit, seg[:24])})
        entries.append((s, e, idx))

    # 时间轴重叠：按开始时间排序线性扫描（特效/注释行重叠常见，作软提醒）
    entries.sort()
    prev_end = prev_idx = None
    for s, e, idx in entries:
        if prev_end is not None and s < prev_end:
            counts["overlap"] += 1
            issues.append({"line": idx, "type": "overlap", "detail": "与第 %d 行时间重叠（该行 %.2f 开始，前一行 %.2f 结束）" % (prev_idx, s, prev_end)})
        if prev_end is None or e > prev_end:
            prev_end, prev_idx = e, idx

    total = sum(counts.values())
    return {"ok": True, "dialogue": n_dialogue, "counts": counts,
            "issues": issues[:200], "total_issues": total, "truncated": len(issues) > 200,
            "status": "ok" if total == 0 else "warn"}


def handle_sub_check(body):
    try:
        cps = float(body.get("cps_limit") or 15)
        ll = int(body.get("line_limit") or 25)
    except (TypeError, ValueError):
        return {"error": "无效的阈值参数"}
    return check_sub((body.get("sub") or "").strip(), cps, ll)


handlers = {"POST": {"/api/sub_check": handle_sub_check}}
