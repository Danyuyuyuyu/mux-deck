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
from app.features.tracks import sub_kind, match_subs


class SubKindTest(unittest.TestCase):
    """字幕文件 → 简/繁槽位路由。项目仅简繁两类，语言由槽位固定（SC=zh-Hans、TC=zh-Hant），不再产语言值。"""

    def test_new_tags(self):
        self.assertEqual(sub_kind("01.zh-hans.ass"), "sc")
        self.assertEqual(sub_kind("01.zh-TW.ass"), "tc")
        self.assertEqual(sub_kind("EP01.zh-CN.ass"), "sc")
        self.assertEqual(sub_kind("EP01.zh-HK.ass"), "tc")
        self.assertEqual(sub_kind("EP01.big5.ass"), "tc")
        self.assertEqual(sub_kind("EP01.gb.ass"), "sc")
        self.assertEqual(sub_kind("EP01.简体.ass"), "sc")
        self.assertEqual(sub_kind("EP01.繁体.ass"), "tc")

    def test_legacy_tags_regress(self):
        self.assertEqual(sub_kind("EP01.sc.ass"), "sc")
        self.assertEqual(sub_kind("EP01.chs.ass"), "sc")
        self.assertEqual(sub_kind("EP01.jpsc.ass"), "sc")
        self.assertEqual(sub_kind("EP01.tc.ass"), "tc")
        self.assertEqual(sub_kind("EP01.cht.ass"), "tc")
        self.assertEqual(sub_kind("EP01.jptc.ass"), "tc")

    def test_no_tag_and_boundary(self):
        self.assertEqual(sub_kind("EP01.ass"), "")
        self.assertEqual(sub_kind("school.ass"), "")     # sc 不能命中 school 的子串
        self.assertEqual(sub_kind("disc.ass"), "")       # tc 不能命中 disc 的子串


class MatchSubsTest(unittest.TestCase):
    def test_match_routes_new_style_tags_no_lang_fields(self):
        with tempfile.TemporaryDirectory() as d:
            for fn in ("Show01.mkv", "EP01.zh-Hans.ass", "EP01.zh-TW.ass", "EP02.sc.ass"):
                open(os.path.join(d, fn), "w", encoding="utf-8").close()
            r = match_subs(os.path.join(d, "Show01.mkv"))
            self.assertEqual(r["sc"], os.path.join(d, "EP01.zh-Hans.ass"))
            self.assertEqual(r["tc"], os.path.join(d, "EP01.zh-TW.ass"))
            # 语言已由槽位固定，响应不再携带 sc_lang/tc_lang
            self.assertNotIn("sc_lang", r)
            self.assertNotIn("tc_lang", r)

    def test_no_match(self):
        with tempfile.TemporaryDirectory() as d:
            open(os.path.join(d, "Show01.mkv"), "w", encoding="utf-8").close()
            r = match_subs(os.path.join(d, "Show01.mkv"))
            self.assertEqual(r["sc"], "")
            self.assertEqual(r["tc"], "")


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


class SubIntegrityTest(unittest.TestCase):
    """Phase 1 QC 完整性校验：ASS 时间解析 + 字幕首/末行时间提取（漏翻片尾/片头检测用）。"""

    def test_parse_ass_time(self):
        self.assertEqual(mc._parse_ass_time("0:01:02.50"), 62.5)
        self.assertEqual(mc._parse_ass_time("1:00:00.00"), 3600.0)
        self.assertEqual(mc._parse_ass_time("0:00:00.00"), 0.0)
        self.assertEqual(mc._parse_ass_time("1:02:03.40"), 3723.4)
        # 逗号分隔厘秒同样接受（部分 ASS 用逗号）
        self.assertEqual(mc._parse_ass_time("0:00:01,50"), 1.5)

    def test_parse_ass_time_invalid(self):
        self.assertIsNone(mc._parse_ass_time("bad"))
        self.assertIsNone(mc._parse_ass_time(""))
        self.assertIsNone(mc._parse_ass_time("00:00"))

    def test_sub_last_end_and_first_start(self):
        import tempfile
        ass = (
            "[Script Info]\n"
            "[Events]\n"
            "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n"
            "Dialogue: 0,0:00:05.00,0:00:08.00,Def,,0,0,0,,开场\n"
            "Dialogue: 0,0:10:00.00,0:10:03.50,Def,,0,0,0,,结尾\n"
        )
        with tempfile.NamedTemporaryFile("w", suffix=".ass", encoding="utf-8", delete=False) as f:
            f.write(ass)
            path = f.name
        try:
            self.assertEqual(mc.sub_first_start(path), 5.0)
            self.assertEqual(mc.sub_last_end(path), 603.5)
        finally:
            os.remove(path)

    def test_sub_empty_returns_none(self):
        import tempfile
        with tempfile.NamedTemporaryFile("w", suffix=".ass", encoding="utf-8", delete=False) as f:
            f.write("[Script Info]\n[Events]\n")
            path = f.name
        try:
            self.assertIsNone(mc.sub_first_start(path))
            self.assertIsNone(mc.sub_last_end(path))
        finally:
            os.remove(path)

    def test_sub_missing_file_returns_none(self):
        self.assertIsNone(mc.sub_last_end(os.path.join(ROOT, "no_such.ass")))
        self.assertIsNone(mc.sub_first_start(os.path.join(ROOT, "no_such.ass")))


class FontSourcesTest(unittest.TestCase):
    """F2：Windows 系统已装字体作为可选额外字体源（font_sources + mux_cli 组装）。"""

    @staticmethod
    def _mk(d, name, data=b"dummy"):
        os.makedirs(d, exist_ok=True)
        p = os.path.join(d, name)
        with open(p, "wb") as f:
            f.write(data)
        return p

    def test_flag_off_zero_change(self):
        from app.tools import font_sources as fs
        with tempfile.TemporaryDirectory() as d:
            user = os.path.join(d, "fonts")
            os.makedirs(user)
            self.assertEqual(fs.build_merged_font_dir(user, False, d), user)
            self.assertEqual(os.listdir(d), ["fonts"])   # 未建任何合并目录

    def test_missing_user_dir_passthrough(self):
        from app.tools import font_sources as fs
        with tempfile.TemporaryDirectory() as d:
            ghost = os.path.join(d, "nope")
            self.assertEqual(fs.build_merged_font_dir(ghost, True, d), ghost)

    def test_family_names_bad_font_empty(self):
        from app.tools import font_sources as fs
        with tempfile.TemporaryDirectory() as d:
            bad = self._mk(d, "bad.ttf", b"not a real font file")
            self.assertEqual(fs.family_names(bad), set())

    def test_build_structure_and_dedupe(self):
        import unittest.mock as mock
        from app.tools import font_sources as fs
        with tempfile.TemporaryDirectory() as d:
            user = os.path.join(d, "user")
            sysdir = os.path.join(d, "sysfonts")
            u1 = self._mk(user, "u1.ttf")
            u_bad = self._mk(user, "u_bad.ttf", b"broken user font")
            s_own = self._mk(sysdir, "s_own.ttf")
            s_dup = self._mk(sysdir, "s_dup.ttf")
            s_bad = self._mk(sysdir, "s_bad.ttf", b"broken sys font")
            self._mk(sysdir, "ignore.txt")   # 非字体扩展名不收
            fam_map = {u1: {"arial"}, u_bad: set(), s_own: {"times"},
                       s_dup: {"arial", "arial bold"}, s_bad: set()}

            def fake_names(p):
                return set(fam_map.get(p, {"unlisted"}))

            with mock.patch.object(fs, "family_names", side_effect=fake_names), \
                 mock.patch.object(fs, "system_font_sources", return_value=[sysdir]):
                merged = fs.build_merged_font_dir(user, True, d)
            self.assertTrue(os.path.basename(merged).startswith("merged_fonts_"))
            names = set(os.listdir(merged))
            self.assertIn("u1.ttf", names)          # 用户目录文件全部并入
            self.assertIn("u_bad.ttf", names)       # 用户目录文件不做解析校验，原样并入
            self.assertIn("s_own.ttf", names)       # 无族名冲突的系统字体并入
            self.assertNotIn("s_dup.ttf", names)    # 族名与用户目录重叠：用户目录优先，跳过
            self.assertNotIn("s_bad.ttf", names)    # 无族名（坏字体）跳过，不让合并崩溃
            self.assertNotIn("ignore.txt", names)

    def test_system_dir_unavailable_safe(self):
        import unittest.mock as mock
        from app.tools import font_sources as fs
        with tempfile.TemporaryDirectory() as d:
            user = os.path.join(d, "user")
            self._mk(user, "u1.ttf")
            with mock.patch.object(fs, "system_font_sources", return_value=[os.path.join(d, "ghost")]):
                merged = fs.build_merged_font_dir(user, True, d)
            self.assertEqual(os.listdir(merged), ["u1.ttf"])

    def test_system_font_sources_exists(self):
        from app.tools import font_sources as fs
        srcs = fs.system_font_sources()
        self.assertTrue(srcs)
        self.assertTrue(all(os.path.exists(p) for p in srcs))
        self.assertEqual(srcs[0], fs.SYSTEM_FONTS_DIR)
        self.assertTrue(os.path.isdir(fs.SYSTEM_FONTS_DIR))

    def test_build_cmd_use_sys_fonts(self):
        from app.features.mux import build_cmd
        it = {"video": "V:\\v.mkv"}
        for off in (0, None, False, ""):
            self.assertNotIn("--use-sys-fonts", build_cmd(it, {"use_sys_fonts": off}),
                             "关闭态不得传参（零变化）")
        for on in (1, True):
            cmd = build_cmd(it, {"use_sys_fonts": on})
            self.assertIn("--use-sys-fonts", cmd)
            self.assertEqual(cmd[cmd.index("--use-sys-fonts") + 1], "1")


class PostcmdTest(unittest.TestCase):
    """F4 后处理命令模板：expand_postcmd 占位符展开 + run_postcmd 执行语义（绝不抛出）+ build_cmd 组装。"""

    def test_expand_all_placeholders(self):
        self.assertEqual(mc.expand_postcmd("copy {out} {src}", "D:\\o.mkv", "D:\\s.mkv", 3),
                         "copy D:\\o.mkv D:\\s.mkv")
        self.assertEqual(mc.expand_postcmd("ep={ep}", "o", "s", "12"), "ep=12")
        self.assertEqual(mc.expand_postcmd("ep={ep}", "o", "s", 12), "ep=12")

    def test_expand_ep_unrecognized_becomes_empty(self):
        self.assertEqual(mc.expand_postcmd("a {ep} b", "o", "s", -1), "a  b")
        self.assertEqual(mc.expand_postcmd("a {ep} b", "o", "s", "-1"), "a  b")
        self.assertEqual(mc.expand_postcmd("a {ep} b", "o", "s", None), "a  b")

    def test_expand_no_placeholder_passthrough(self):
        self.assertEqual(mc.expand_postcmd("echo hi", "o", "s", 1), "echo hi")
        self.assertEqual(mc.expand_postcmd("", "o", "s", 1), "")

    def test_expand_extra_braces_kept_intact(self):
        self.assertEqual(mc.expand_postcmd("x {bogus} {out}", "o", "s", 1), "x {bogus} o")
        self.assertEqual(mc.expand_postcmd("{out}{out}", "o", "s", 1), "oo")

    def test_run_postcmd_ok_captures_output(self):
        rc, out = mc.run_postcmd('"%s" -c "print(\'hi\')"' % sys.executable, "o", "s", "EP03")
        self.assertEqual(rc, 0)
        self.assertIn("hi", out)

    def test_run_postcmd_expands_placeholders_with_detected_ep(self):
        rc, out = mc.run_postcmd('"%s" -c "import sys; print(sys.argv[1])" {ep}' % sys.executable,
                                 "o", "s", "Show EP07 1080P")
        self.assertEqual(rc, 0)
        self.assertIn("07", out)

    def test_run_postcmd_nonzero_exit_no_raise(self):
        rc, out = mc.run_postcmd('"%s" -c "import sys; sys.exit(3)"' % sys.executable, "o", "s", "x")
        self.assertEqual(rc, 3)
        self.assertIsInstance(out, str)

    def test_run_postcmd_missing_binary_no_raise(self):
        rc, out = mc.run_postcmd("definitely_not_a_real_cmd_9x7x5", "o", "s", "x")
        self.assertNotEqual(rc, 0)
        self.assertTrue(out)

    def test_run_postcmd_empty_command_skips(self):
        rc, out = mc.run_postcmd("", "o", "s", "x")
        self.assertEqual((rc, out), (0, ""))
        rc, out = mc.run_postcmd("   ", "o", "s", "x")
        self.assertEqual((rc, out), (0, ""))

    def test_build_cmd_postcmd_empty_falls_back(self):
        from app.features.mux import build_cmd
        it = {"video": "V:\\v.mkv"}
        for empty in ("", "   ", None):
            self.assertNotIn("--postcmd", build_cmd(it, {"postcmd": empty}),
                             "任务级空串不传参 = CLI 回落全局默认")
        cmd = build_cmd(it, {"postcmd": "echo hi"})
        self.assertIn("--postcmd", cmd)
        self.assertEqual(cmd[cmd.index("--postcmd") + 1], "echo hi")

    def test_qc_from_log_accepts_postproc_warn(self):
        from app.features.mux import _qc_from_log
        q = _qc_from_log("POSTPROC-OK: 后处理完成\nPOSTPROC-WARN: 后处理命令失败（退出码 1）")
        self.assertEqual(q["status"], "warn")
        self.assertEqual(q["warn"], ["后处理命令失败（退出码 1）"])

    def test_decode_postcmd_output_gbk(self):
        # cmd.exe 内置报错走系统 ACP(GBK)：不得按 UTF-8 解成乱码
        raw = "不是内部或外部命令".encode("gbk")
        self.assertEqual(mc._decode_postcmd_output(raw), "不是内部或外部命令")

    def test_decode_postcmd_output_utf8(self):
        raw = "后处理完成".encode("utf-8")
        self.assertEqual(mc._decode_postcmd_output(raw), "后处理完成")

    def test_decode_postcmd_output_empty_and_utf16(self):
        self.assertEqual(mc._decode_postcmd_output(b""), "")
        self.assertEqual(mc._decode_postcmd_output("乱码修".encode("utf-16")), "乱码修")


class SubsetTest(unittest.TestCase):
    """F3 独立子集化：路由注册 + 参数校验（校验失败不建 job）。"""

    def test_routes_registered(self):
        from app.features import find, subset
        self.assertIsNotNone(find("POST", "/api/subset_run"))
        self.assertIsNotNone(find("GET", "/api/subset_status"))
        self.assertIsNotNone(find("POST", "/api/subset_stop"))
        self.assertIn("/api/subset_run", subset.handlers["POST"])
        self.assertIn("/api/subset_status", subset.handlers["GET"])
        self.assertIn("/api/subset_stop", subset.handlers["POST"])

    def test_validation_no_job_created(self):
        from app.features import subset
        from app import core
        with tempfile.TemporaryDirectory() as d:
            sub = os.path.join(d, "EP01.ass")
            with open(sub, "w", encoding="utf-8"):
                pass
            fonts = os.path.join(d, "fonts")
            os.makedirs(fonts)
            out = os.path.join(d, "out")
            jobs_before = set(core.JOBS)
            cases = [
                ({}, "字幕"),
                ({"subs": [], "fonts_dir": fonts, "out_dir": out}, "字幕"),
                ({"subs": ["   "], "fonts_dir": fonts, "out_dir": out}, "字幕"),
                ({"subs": [sub], "fonts_dir": "", "out_dir": out}, "字体目录"),
                ({"subs": [sub], "fonts_dir": os.path.join(d, "nope"), "out_dir": out}, "字体目录"),
                ({"subs": [sub], "fonts_dir": fonts, "out_dir": ""}, "输出目录"),
                ({"subs": [sub], "fonts_dir": fonts, "out_dir": "   "}, "输出目录"),
                ({"subs": [os.path.join(d, "ghost.ass")], "fonts_dir": fonts, "out_dir": out}, "不存在"),
            ]
            for body, kw in cases:
                r = subset.handle_run(body)
                self.assertIn("error", r, body)
                self.assertIn(kw, r["error"], body)
            self.assertEqual(set(core.JOBS), jobs_before, "校验失败不得创建 job")

    def test_out_dir_same_as_fonts_dir_rejected(self):
        from app.features import subset
        with tempfile.TemporaryDirectory() as d:
            sub = os.path.join(d, "EP01.ass")
            with open(sub, "w", encoding="utf-8"):
                pass
            r = subset.handle_run({"subs": [sub], "fonts_dir": d, "out_dir": d})
            self.assertIn("error", r)
            self.assertIn("字体目录", r["error"])
            # 大小写 / 尾斜杠差异（Windows 口径）同样拒绝
            r2 = subset.handle_run({"subs": [sub], "fonts_dir": d, "out_dir": d.upper() + os.sep})
            self.assertIn("error", r2)

    def test_status_and_stop_unknown_id(self):
        from app.features import subset
        self.assertIn("error", subset.handle_status({"id": ["no_such_job"]}))
        self.assertIn("error", subset.handle_stop({"id": "no_such_job"}))
        # 非 subset 任务（共享 JOBS 注册表）不可经 subset 端点查询/停止
        from app import core
        core.JOBS["foreign_id"] = {"id": "foreign_id", "status": "running"}
        try:
            self.assertIn("error", subset.handle_status({"id": ["foreign_id"]}))
            self.assertIn("error", subset.handle_stop({"id": "foreign_id"}))
        finally:
            del core.JOBS["foreign_id"]


if __name__ == "__main__":
    unittest.main()
