# -*- coding: utf-8 -*-
# 纯函数单元测试（stdlib unittest，无第三方依赖）：
#   python -m unittest discover -s tests -v
import importlib.util
import os
import sys
import tempfile
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, ROOT)


def _load_mux_cli():
    spec = importlib.util.spec_from_file_location(
        "mux_cli", os.path.join(ROOT, "app", "tools", "mux_cli.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


mc = _load_mux_cli()
from app.features.mux import display_cmd
from app.features.subcheck import _parse_ts, check_sub


class ResolveOutNameTest(unittest.TestCase):
    BASE = "[KissSub&FZSD][Kono_Subarashii_Sekai_ni_Shukufuku_o!][01][1080P][AVC_AAC](97BCD5EB)"

    def test_bracket_episode_and_res(self):
        self.assertEqual(mc.resolve_out_name("[测试组] {ep} [{res}]", self.BASE, 1080),
                         "[测试组] 01 [1080P]")

    def test_ep_token(self):
        self.assertEqual(mc.resolve_out_name("{ep}话", "Show EP12 1080P", 1080), "12话")

    def test_chinese_episode(self):
        self.assertEqual(mc.resolve_out_name("第{ep}话", "某动画 第3话 raw", 720), "第3话")

    def test_no_res_height_zero(self):
        self.assertEqual(mc.resolve_out_name("[{res}]", "x", 0), "[]")

    def test_src_passthrough(self):
        self.assertEqual(mc.resolve_out_name("{src}", "EP05 x", 0), "EP05 x")

    def test_forbidden_chars_sanitized(self):
        self.assertEqual(mc.resolve_out_name('a<b>{src}', "x", 0), "a_b_x")

    def test_empty_template_falls_back(self):
        self.assertEqual(mc.resolve_out_name("   ", "keep", 0), "keep")

    def test_title_placeholder(self):
        self.assertEqual(mc.resolve_out_name("{title} [{res}]", "src", 1080, "我的标题"),
                         "我的标题 [1080P]")

    def test_title_empty_falls_back_to_src(self):
        self.assertEqual(mc.resolve_out_name("{title}", "EP05 x", 0, ""), "EP05 x")
        self.assertEqual(mc.resolve_out_name("{title}", "EP05 x", 0, "   "), "EP05 x")

    def test_title_stripped(self):
        self.assertEqual(mc.resolve_out_name("{title}", "x", 0, "  标题  "), "标题")

    def test_dangling_separator_trimmed(self):
        self.assertEqual(mc.resolve_out_name("{src} - {res}", "Movie", 0), "Movie")
        self.assertEqual(mc.resolve_out_name("{src}_{res}", "Movie", 0), "Movie")


class DisplayCmdTest(unittest.TestCase):
    def test_quotes_spaces(self):
        self.assertEqual(display_cmd(["py", "-3", "x.py", "--video", "a b.mkv"]),
                         'py -3 x.py --video "a b.mkv"')

    def test_plain(self):
        self.assertEqual(display_cmd(["mkvmerge", "-o", "out.mkv"]), "mkvmerge -o out.mkv")


class ParseTsTest(unittest.TestCase):
    def test_ass_time(self):
        self.assertEqual(_parse_ts("0:01:02.50"), 62.5)
        self.assertEqual(_parse_ts("1:00:00.000"), 3600.0)

    def test_invalid(self):
        self.assertIsNone(_parse_ts("bad"))
        self.assertIsNone(_parse_ts("00:00"))


class CheckSubTest(unittest.TestCase):
    LONG = "非常长的一句台词超过了单行限制的长度测试用再补几个字充数凑够二十五字以上"
    ASS = (
        "[Script Info]\n"
        "[Events]\n"
        "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n"
        "Style: Def,Font,20,&H00FFFFFF,-1,0,0,0,0,0,0,0,0,1,0,0,1,0,0,0,1\n"
        "Dialogue: 0,0:00:01.00,0:00:00.50,Def,,0,0,0,,T1\n"
        "Dialogue: 0,0:00:02.00,0:00:03.00,Def,,0,0,0,," + LONG + "\n"
        "Dialogue: 0,0:00:02.50,0:00:07.00,Def,,0,0,0,,与上一行重叠\n"
        "Dialogue: 0,0:00:08.00,0:00:09.00,Ghost,,0,0,0,,坏样式\n"
        "Dialogue: 0,0:00:10.00,0:00:20.00,Def,,0,0,0,,\n"
        "Dialogue: 0,0:00:21.00,0:00:22.00,Def,,0,0,0,,短\n"
        "Dialogue: broken\n"
    )

    def setUp(self):
        fd, self.path = tempfile.mkstemp(suffix=".ass")
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            f.write(self.ASS)

    def tearDown(self):
        os.remove(self.path)

    def test_all_issue_kinds_detected(self):
        r = check_sub(self.path)
        self.assertEqual(r["status"], "warn")
        self.assertEqual(r["dialogue"], 7)
        c = r["counts"]
        self.assertEqual(c["bad_time"], 2)      # 负时长 + 字段不足
        self.assertEqual(c["overlap"], 1)
        self.assertEqual(c["bad_style"], 1)     # Ghost 未定义
        self.assertEqual(c["empty"], 1)
        self.assertGreaterEqual(c["cps"], 1)
        self.assertGreaterEqual(c["long_line"], 1)

    def test_thresholds_relaxed(self):
        r = check_sub(self.path, cps_limit=999, line_limit=999)
        self.assertEqual(r["counts"]["cps"], 0)
        self.assertEqual(r["counts"]["long_line"], 0)

    def test_missing_file(self):
        self.assertIn("error", check_sub(os.path.join(ROOT, "no_such.ass")))


class ChaptersTest(unittest.TestCase):
    CHS = [
        {"time": "00:00:00.000", "name": "OP"},
        {"time": "00:01:30.500", "name": "Part A"},
        {"time": "01:20:00.250", "name": "ED"},
    ]

    def test_save_parse_roundtrip(self):
        from app.features import chapters
        r = chapters.handle_save({"chapters": self.CHS})
        self.assertIn("path", r)
        try:
            r2 = chapters.handle_parse({"path": r["path"]})
            self.assertEqual(r2["chapters"], self.CHS)
        finally:
            os.remove(r["path"])

    def test_validation(self):
        from app.features import chapters
        self.assertIn("无效", chapters.handle_save({"chapters": [{"time": "bad"}]})["error"])
        self.assertIn("早于", chapters.handle_save({"chapters": [{"time": "0:00:10.000"}, {"time": "0:00:05.000"}]})["error"])

    def test_xml_parse(self):
        from app.features import chapters
        fd, path = tempfile.mkstemp(suffix=".xml")
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            f.write('<Chapters><EditionEntry><ChapterAtom><ChapterTimeStart>00:00:00.000000000</ChapterTimeStart>'
                    '<ChapterDisplay><ChapterString>Intro</ChapterString></ChapterDisplay></ChapterAtom></EditionEntry></Chapters>')
        try:
            r = chapters.handle_parse({"path": path})
            self.assertEqual(r["chapters"], [{"time": "00:00:00.000", "name": "Intro"}])
        finally:
            os.remove(path)


class QcFromLogTest(unittest.TestCase):
    def test_statuses(self):
        from app.features.mux import _qc_from_log
        self.assertEqual(_qc_from_log("QC: 通过")["status"], "ok")
        self.assertEqual(_qc_from_log('QC-WARN: a\nQC-WARN: b')["status"], "warn")
        self.assertEqual(_qc_from_log("FAIL: QC 失败：x")["status"], "fail")
        self.assertIsNone(_qc_from_log("nothing"))


if __name__ == "__main__":
    unittest.main()
