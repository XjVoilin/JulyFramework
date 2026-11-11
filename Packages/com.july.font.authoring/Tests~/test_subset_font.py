import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.pens.t2CharStringPen import T2CharStringPen
from fontTools.ttLib import TTFont

SCRIPT = Path(__file__).resolve().parents[1] / "Tools~" / "subset_font.py"
spec = importlib.util.spec_from_file_location("july_font_subset", SCRIPT)
tool = importlib.util.module_from_spec(spec)
spec.loader.exec_module(tool)


def make_font(path, cff=False):
    builder = FontBuilder(1000, isTTF=not cff)
    names = [".notdef", "space", "A", "B", "zhong", "supplementary"]
    builder.setupGlyphOrder(names)
    builder.setupCharacterMap({32: "space", 65: "A", 66: "B", ord("中"): "zhong", ord("𠮷"): "supplementary"})
    if cff:
        charstrings = {}
        for name in names:
            pen = T2CharStringPen(600, None)
            if name != "space":
                pen.moveTo((100, 0)); pen.lineTo((300, 700)); pen.lineTo((500, 0)); pen.closePath()
            charstrings[name] = pen.getCharString()
        builder.setupCFF("JulyTest", {"FullName": "July Test", "FamilyName": "July Test", "Weight": "Regular"}, charstrings, {})
    else:
        glyphs = {}
        for name in names:
            pen = TTGlyphPen(None)
            if name != "space":
                pen.moveTo((100, 0)); pen.lineTo((300, 700)); pen.lineTo((500, 0)); pen.closePath()
            glyphs[name] = pen.glyph()
        builder.setupGlyf(glyphs)
    builder.setupHorizontalMetrics({name: (600, 0) for name in names})
    builder.setupHorizontalHeader(ascent=800, descent=-200)
    builder.setupNameTable({"familyName": "July Test", "styleName": "Regular", "uniqueFontIdentifier": "JulyTest", "fullName": "July Test", "psName": "JulyTest"})
    builder.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200)
    builder.setupPost()
    builder.save(path)


class FontSubsetTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="July 字体 test ", dir=os.environ.get("JULY_FONT_TEST_ROOT"))
        self.root = Path(self.temporary.name)
        self.source = self.root / "测试 font.ttf"
        make_font(self.source)
        self.request = {"input": str(self.source), "useDefault": False, "extraText": "A中𠮷"}

    def tearDown(self):
        self.temporary.cleanup()

    def test_default_charset_matches_existing_script(self):
        legacy = Path(__file__).resolve().parents[4] / "Tools/FontSubset/subset_font.py"
        if not legacy.exists():
            self.skipTest("Legacy project script is not part of this package")
        import runpy
        self.assertEqual(tool.default_characters(), set(runpy.run_path(str(legacy))["build_charset"]()))

    def test_extra_and_utf8_file_merge_preserve_codepoints_and_shaping_characters(self):
        characters = self.root / "补充.txt"
        characters.write_text("\ufeff中𠮷A\n\t \u200d\ufe0f", encoding="utf-8")
        points = tool.build_characters(False, "AA𠮷", str(characters))
        self.assertEqual(set(points), {65, ord("中"), ord("𠮷"), 32, 0x200D, 0xFE0F})

    def test_empty_input_and_invalid_unicode_fail(self):
        with self.assertRaises(ValueError): tool.build_characters(False, "\n\t")
        with self.assertRaises(ValueError): tool.build_characters(False, "\ud800")

    def test_check_distinguishes_source_missing_and_does_not_write(self):
        self.request["extraText"] += "龘"
        result = tool.execute(self.request)
        self.assertEqual(result["covered"], 3)
        self.assertEqual(result["missing"], ["龘 (U+9F98)"])
        self.assertFalse(Path(result["output"]).exists())
        with self.assertRaisesRegex(ValueError, "原字体缺少"):
            tool.execute(self.request, True)

    def test_missing_acknowledgment_preserves_available_glyphs(self):
        self.request.update(extraText="A𠮷龘", allowMissing=True)
        result = tool.execute(self.request, True)
        with TTFont(result["output"]) as font:
            self.assertEqual(set(font.getBestCmap()), {65, ord("𠮷")})
        self.assertEqual(result["missingText"], "龘")
        self.assertEqual(result["lost"], [])

    def test_ttf_output_is_sibling_and_keeps_source_intact(self):
        before = self.source.read_bytes()
        result = tool.execute(self.request, True)
        self.assertEqual(Path(result["output"]), self.root / "测试 font-subset.ttf")
        self.assertEqual(self.source.read_bytes(), before)
        with TTFont(result["output"]) as font:
            self.assertEqual(set(font.getBestCmap()), {65, ord("中"), ord("𠮷")})
            self.assertIn("glyf", font)
        self.assertEqual(Path(result["charsetOutput"]).read_text(encoding="utf-8"), "A中𠮷")
        self.assertFalse(list(self.root.glob(".july-font-*")))

    def test_cff_otf_keeps_its_outline_format(self):
        source = self.root / "CFF font.OTF"
        make_font(source, cff=True)
        self.request["input"] = str(source)
        result = tool.execute(self.request, True)
        self.assertTrue(result["output"].endswith("-subset.OTF"))
        with TTFont(result["output"]) as font:
            self.assertIn("CFF ", font)
            self.assertEqual(set(font.getBestCmap()), {65, ord("中"), ord("𠮷")})

    def test_existing_outputs_require_explicit_replace(self):
        result = tool.execute(self.request, True)
        original_output = Path(result["output"]).read_bytes()
        with self.assertRaises(FileExistsError): tool.execute(self.request, True)
        self.assertEqual(Path(result["output"]).read_bytes(), original_output)
        self.request.update(extraText="B", replace=True)
        tool.execute(self.request, True)
        with TTFont(result["output"]) as font:
            self.assertEqual(set(font.getBestCmap()), {66})

    def test_invalid_utf8_fails_without_publishing(self):
        characters = self.root / "invalid.txt"
        characters.write_bytes(b"\xff")
        self.request["characterFile"] = str(characters)
        with self.assertRaises(UnicodeDecodeError): tool.execute(self.request, True)
        self.assertFalse(tool.output_paths(self.source)[0].exists())

    def test_stale_precheck_rejects_changed_character_file(self):
        characters = self.root / "extra.txt"
        characters.write_text("A", encoding="utf-8")
        self.request["characterFile"] = str(characters)
        self.request["fingerprint"] = tool.execute(self.request)["fingerprint"]
        characters.write_text("B", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "发生变化"):
            tool.execute(self.request, True)

    def test_lost_character_fails_and_preserves_existing_output(self):
        result = tool.execute(self.request, True)
        previous_font = Path(result["output"]).read_bytes()
        previous_chars = Path(result["charsetOutput"]).read_bytes()
        self.request["replace"] = True
        from fontTools.subset import Subsetter
        subset = Subsetter.subset
        def lose_character(subsetter, font):
            subset(subsetter, font)
            for table in font["cmap"].tables:
                table.cmap.pop(65, None)
        with patch.object(Subsetter, "subset", lose_character):
            with self.assertRaisesRegex(ValueError, "裁剪丢失"):
                tool.execute(self.request, True)
        self.assertEqual(Path(result["output"]).read_bytes(), previous_font)
        self.assertEqual(Path(result["charsetOutput"]).read_bytes(), previous_chars)
        self.assertFalse(list(self.root.glob(".july-font-*")))

    def test_cancellation_after_subsetting_does_not_publish(self):
        marker = self.root / "cancel"
        self.request["cancelFile"] = str(marker)
        from fontTools.subset import Subsetter
        subset = Subsetter.subset
        def cancel(subsetter, font):
            subset(subsetter, font)
            marker.touch()
        with patch.object(Subsetter, "subset", cancel):
            with self.assertRaises(InterruptedError): tool.execute(self.request, True)
        self.assertFalse(tool.output_paths(self.source)[0].exists())
        self.assertFalse(list(self.root.glob(".july-font-*")))

    def test_no_supported_characters_fails_without_output(self):
        self.request["extraText"] = "龘"
        with self.assertRaisesRegex(ValueError, "不包含任何"):
            tool.execute(self.request, True)

    def test_cli_unicode_paths_and_text_are_json_safe(self):
        result = subprocess.run([sys.executable, "-B", str(SCRIPT), "subset", str(self.source), "--no-default", "--extra-text", "A𠮷"], capture_output=True, text=True, encoding="utf-8")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertTrue(json.loads(result.stdout)["written"])


if __name__ == "__main__":
    unittest.main()
