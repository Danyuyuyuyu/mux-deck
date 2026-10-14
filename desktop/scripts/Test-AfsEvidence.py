"""对公开对照 CLI 的真实证据反例；不进入解决方案或 CI。"""
import argparse
import copy
import hashlib
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from fontTools import subset
from fontTools.ttLib import TTFont

SOURCE = None
SCRIPT = Path(__file__).with_name("Normalize-AfsEvidence.py")

class EvidenceRulesTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="MuxDeck-afs-counter-")
        self.root = Path(self.temp.name)
        self.report = json.loads(SOURCE.read_text(encoding="utf-8-sig"))
        for side in ("baseline", "current"):
            shutil.copytree(SOURCE.parent / side, self.root / side)
        self.addCleanup(self.temp.cleanup)

    def entry(self, name):
        return next(e for e in self.report["Comparisons"] if e["Name"] == name)

    def font_path(self):
        identity = self.entry("Attachments")["Current"][0]
        return self.root / "current" / f"attachment-{identity['Id']}.bin"

    def refresh_font(self):
        identity = self.entry("Attachments")["Current"][0]
        raw = self.font_path().read_bytes()
        identity["Sha256"] = hashlib.sha256(raw).hexdigest().upper()
        identity["Size"] = len(raw)

    def edit_font(self, action):
        path = self.font_path()
        with TTFont(path, recalcTimestamp=False) as font:
            action(font)
            font.save(path)
        self.refresh_font()

    def edit_ass(self, action):
        track = self.entry("Ass")["Current"][0]
        path = self.root / "current" / f"track-{track['Id']}.ass"
        data = action(path.read_bytes())
        path.write_bytes(data)
        track["Text"] = data.decode("utf-8-sig")

    def check_cli(self, expected, category=None):
        path = self.root / "raw-comparison.json"
        path.write_text(json.dumps(self.report, ensure_ascii=False), encoding="utf8")
        output = self.root / "normalized"
        result = subprocess.run([sys.executable, "-X", "utf8", str(SCRIPT), "--report", str(path),
            "--output-directory", str(output)], capture_output=True, text=True, encoding="utf8")
        self.assertEqual(expected, result.returncode, result.stdout + result.stderr)
        report = json.loads((output / "comparison.json").read_text(encoding="utf8"))
        if category:
            self.assertFalse(next(e["Equal"] for e in report["Comparisons"] if e["Name"] == category))
        return report

    def test_old_old_allowed_random_names_and_two_head_fields_pass(self):
        self.check_cli(0)

    def test_real_missing_glyph_is_rejected(self):
        def remove_glyph(font):
            before = font.getBestCmap()
            # 正文 English 使用 Arial 的 E；删除该真实使用的码位与字形。
            self.assertIn("English", self.entry("Ass")["Current"][0]["Text"])
            codepoint = ord("E")
            self.assertIn(codepoint, before)
            victim = before[codepoint]
            options = subset.Options()
            options.name_IDs = ["*"]
            options.name_languages = ["*"]
            reducer = subset.Subsetter(options=options)
            reducer.populate(unicodes=set(before) - {codepoint})
            reducer.subset(font)
            self.assertNotIn(codepoint, font.getBestCmap())
            self.assertNotIn(victim, font.getGlyphOrder())
        self.edit_font(remove_glyph)
        self.check_cli(1, "Fonts")

    def test_dialogue_text_change_is_rejected(self):
        def change(data):
            self.assertIn(b"Simplified Chinese", data)
            return data.replace(b"Simplified Chinese", b"Changed dialogue", 1)
        self.edit_ass(change)
        self.check_cli(1, "Ass")

    def test_random_token_in_plain_dialogue_is_not_restored(self):
        def change(data):
            import re
            token = re.search(rb"; Font Subset: ([A-Z0-9]{8})", data).group(1)
            return data.replace(b"Simplified Chinese", token, 1)
        self.edit_ass(change)
        self.check_cli(1, "Ass")

    def test_dialogue_time_change_is_rejected(self):
        self.edit_ass(lambda data: data.replace(b"0:00:01.00", b"0:00:02.00", 1))
        self.check_cli(1, "Ass")

    def test_ass_whitespace_change_is_rejected(self):
        self.edit_ass(lambda data: data.replace(b"ScriptType:", b"ScriptType: ", 1))
        self.check_cli(1, "Ass")

    def test_ass_line_ending_change_is_rejected(self):
        self.edit_ass(lambda data: data.replace(b"\r\n", b"\n") if b"\r\n" in data else data.replace(b"\n", b"\r\n"))
        self.check_cli(1, "Ass")

    def test_mapping_original_name_change_is_rejected(self):
        self.edit_ass(lambda data: data.replace(b" - Arial", b" - Changed Arial", 1))
        self.check_cli(1, "Ass")

    def test_mapping_duplicate_is_not_discarded(self):
        def change(data):
            line = next(line for line in data.splitlines(keepends=True) if b"; Font Subset:" in line)
            return data.replace(line, line + line, 1)
        self.edit_ass(change)
        self.check_cli(1, "Ass")

    def test_real_os2_table_content_change_is_rejected(self):
        self.edit_font(lambda font: setattr(font["OS/2"], "sTypoAscender", font["OS/2"].sTypoAscender + 1))
        self.check_cli(1, "Fonts")

    def test_unrelated_name_record_is_not_ignored(self):
        def change(font):
            record = next(r for r in font["name"].names if r.nameID == 0)
            record.string = "Changed copyright".encode(record.getEncoding())
        self.edit_font(change)
        self.check_cli(1, "Fonts")

    def test_other_head_field_created_is_rejected(self):
        path = self.font_path()
        with TTFont(path, lazy=True) as font:
            offset = font.reader.tables["head"].offset
        data = bytearray(path.read_bytes())
        data[offset + 27] ^= 1
        path.write_bytes(data)
        self.refresh_font()
        self.check_cli(1, "Fonts")

    def test_attachment_order_change_is_rejected(self):
        self.entry("Attachments")["Current"].reverse()
        self.check_cli(1, "Attachments")

    def test_attachment_count_change_is_rejected(self):
        self.entry("Attachments")["Current"].pop()
        self.check_cli(1, "Attachments")

    def test_attachment_mime_change_is_rejected(self):
        self.entry("Attachments")["Current"][0]["ContentType"] = "font/otf"
        self.check_cli(1, "Attachments")

    def test_attachment_nonrandom_filename_change_is_rejected(self):
        item = self.entry("Attachments")["Current"][0]
        item["Name"] = item["Name"].replace("arial.", "changed-arial.", 1)
        self.check_cli(1, "Attachments")

    def test_attachment_source_hash_integrity_is_checked(self):
        item = self.entry("Attachments")["Current"][0]
        item["Sha256"] = "0" * 64
        path = self.root / "raw-comparison.json"
        path.write_text(json.dumps(self.report), encoding="utf8")
        result = subprocess.run([sys.executable, str(SCRIPT), "--report", str(path),
            "--output-directory", str(self.root / "normalized")], capture_output=True)
        self.assertNotEqual(0, result.returncode)

    def test_strict_track_information_stays_rejected(self):
        self.entry("Tracks")["Equal"] = False
        self.check_cli(1, "Tracks")

    def test_strict_audio_fullraw_stays_rejected(self):
        self.entry("AudioFullRaw")["Equal"] = False
        self.check_cli(1, "AudioFullRaw")

    def test_strict_checks_stay_rejected(self):
        self.entry("Checks")["Equal"] = False
        self.check_cli(1, "Checks")

    def test_strict_warnings_stay_rejected(self):
        self.entry("Warnings")["Equal"] = False
        self.check_cli(1, "Warnings")

    def test_strict_progress_stays_rejected(self):
        self.entry("Progress")["Equal"] = False
        self.check_cli(1, "Progress")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, type=Path)
    options, rest = parser.parse_known_args()
    SOURCE = options.source.resolve()
    unittest.main(argv=[sys.argv[0]] + rest)
