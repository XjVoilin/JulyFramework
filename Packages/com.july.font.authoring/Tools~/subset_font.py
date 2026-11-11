#!/usr/bin/env python3
"""July source font subsetting. No Unity or game dependencies."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
import unicodedata
import uuid

FONTTOOLS_VERSION = Path(__file__).with_name("requirements.txt").read_text(encoding="utf-8").strip().partition("==")[2]


def default_characters():
    chars = {chr(cp) for cp in range(0x20, 0x7F)}
    chars.update(chr(cp) for cp in range(0xFF01, 0xFF5F))
    for high in range(0xB0, 0xF8):
        for low in range(0xA1, 0xFF):
            try:
                chars.add(bytes([high, low]).decode("gb2312"))
            except UnicodeDecodeError:
                pass  # Unassigned positions in the GB2312 character grid.
    chars.update(
        "，。！？、；：“”‘’（）【】《》〈〉「」『』〔〕"
        "—–…·～￥×÷±∞≈≠≤≥①②③④⑤⑥⑦⑧⑨⑩"
        "★☆●○■□▲△▼▽◆◇→←↑↓↔↕♠♣♥♦\u3000"
    )
    return chars


def text_characters(text):
    # Keep format characters such as variation selectors / ZWJ for font shaping.
    chars = {char for char in text if unicodedata.category(char) != "Cc" and char != "\ufeff"}
    if any(unicodedata.category(char) == "Cs" for char in chars):
        raise ValueError("字符输入包含无效 Unicode 代理码点。")
    return chars


def build_characters(use_default=True, extra_text="", character_file=""):
    chars = default_characters() if use_default else set()
    chars.update(text_characters(extra_text))
    if character_file:
        chars.update(text_characters(Path(character_file).read_text(encoding="utf-8-sig")))
    if not chars:
        raise ValueError("请启用基础字符集，或提供额外字符／字符文件。")
    return sorted(ord(char) for char in chars)


def output_paths(source):
    source = Path(source).resolve(strict=True)
    if source.suffix.lower() not in {".ttf", ".otf"}:
        raise ValueError("仅支持 TTF／OTF 源字体；不执行格式转换。")
    return source.with_name(source.stem + "-subset" + source.suffix), source.with_name(source.stem + "-subset.charset.txt")


def load_fonttools(dependencies=""):
    if dependencies:
        root = Path(dependencies).resolve(strict=True)
        sys.path.insert(0, str(root))
    import fontTools
    from fontTools.subset import Options, Subsetter
    from fontTools.ttLib import TTFont
    if fontTools.__version__ != FONTTOOLS_VERSION:
        raise ValueError(f"需要 fontTools {FONTTOOLS_VERSION}，当前为 {fontTools.__version__}。")
    if dependencies and not Path(fontTools.__file__).resolve().is_relative_to(root):
        raise ValueError("未加载项目独立工具环境，请初始化工具环境。")
    return TTFont, Options, Subsetter


def describe_codepoints(points):
    return [f"{chr(cp)} (U+{cp:04X})" for cp in sorted(points)]


def inspect_font(source, points, font_type):
    with font_type(source) as font:
        if font.flavor is not None:
            raise ValueError("不支持 WOFF／WOFF2 容器，请使用原始 TTF／OTF 字体。")
        cmap = font.getBestCmap()
        if cmap is None:
            raise ValueError("字体没有可用的 Unicode 字符映射。")
        available = set(cmap)
    requested = set(points)
    missing = requested - available
    covered = requested & available
    if not covered:
        raise ValueError("原字体不包含任何请求字符，未生成裁剪文件。")
    return available, covered, missing


def request_fingerprint(source, points):
    digest = hashlib.sha256()
    with open(source, "rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    digest.update(json.dumps(points, separators=(",", ":")).encode("ascii"))
    return digest.hexdigest()


def publish_new(temporary, destination):
    # Windows rename refuses an existing destination. POSIX rename replaces it,
    # so use an exclusive same-filesystem hard link there. Both publish complete files.
    if os.name == "nt":
        os.rename(temporary, destination)
    else:
        os.link(temporary, destination)
        os.unlink(temporary)


def execute(request, subset=False, dependencies=""):
    def check_cancelled():
        if request.get("cancelFile") and Path(request["cancelFile"]).exists():
            raise InterruptedError("任务已取消。")

    check_cancelled()
    font_type, options_type, subsetter_type = load_fonttools(dependencies)
    source = Path(request["input"]).resolve(strict=True)
    output, charset_output = output_paths(source)
    points = build_characters(request.get("useDefault", True), request.get("extraText", ""), request.get("characterFile", ""))
    _, expected, missing = inspect_font(source, points, font_type)
    fingerprint = request_fingerprint(source, points)
    result = {
        "input": str(source), "output": str(output), "charsetOutput": str(charset_output),
        "requested": len(points), "covered": len(expected), "missing": describe_codepoints(missing),
        "missingText": "".join(chr(cp) for cp in sorted(missing)),
        "lost": [], "originalBytes": source.stat().st_size, "outputBytes": 0,
        "fingerprint": fingerprint, "written": False,
    }
    if not subset:
        check_cancelled()
        return result
    # Unity requires a fresh precheck before acknowledging missing source glyphs.
    if request.get("fingerprint") and request["fingerprint"] != fingerprint:
        raise ValueError("字体或字符输入在预检查后发生变化，请重新预检查。")
    if missing and not request.get("allowMissing", False):
        raise ValueError("原字体缺少请求字符，请先预检查并确认缺字后再裁剪。")
    if not request.get("replace", False) and (output.exists() or charset_output.exists()):
        raise FileExistsError(f"输出已存在，请确认替换：{output}")

    font_temp = charset_temp = None
    prefix = ".july-font-" + uuid.UUID(request["jobId"]).hex + "-" if request.get("jobId") else ".july-font-"
    try:
        check_cancelled()
        handle, font_temp = tempfile.mkstemp(prefix=prefix, suffix=source.suffix, dir=source.parent)
        os.close(handle)
        with font_type(source) as font:
            options = options_type()
            options.layout_features = ["*"]
            options.drop_tables = []
            subsetter = subsetter_type(options=options)
            subsetter.populate(unicodes=points)
            subsetter.subset(font)
            font.save(font_temp)
        with font_type(font_temp) as font:
            actual = set(font.getBestCmap() or {})
        lost = expected - actual
        if lost:
            raise ValueError("裁剪丢失原字体已有字符，输出未发布：" + ", ".join(describe_codepoints(lost)))
        if request_fingerprint(source, points) != fingerprint:
            raise ValueError("源字体在裁剪期间发生变化，输出未发布。")
        check_cancelled()
        handle, charset_temp = tempfile.mkstemp(prefix=prefix, suffix=".txt", dir=source.parent)
        with os.fdopen(handle, "w", encoding="utf-8") as stream:
            stream.write("".join(chr(cp) for cp in points))
        result["outputBytes"] = os.path.getsize(font_temp)
        check_cancelled()
        # Each file is published only after validation. Preserve the source and output GUID.
        # A filesystem failure between these two renames is reported, never hidden as success.
        if request.get("replace", False):
            os.replace(charset_temp, charset_output)
            charset_temp = None
            os.replace(font_temp, output)
            font_temp = None
        else:
            # Exclusive creation cannot overwrite an output created concurrently.
            publish_new(charset_temp, charset_output)
            try:
                publish_new(font_temp, output)
            except OSError:
                charset_output.unlink()
                raise
        result["written"] = True
        return result
    finally:
        for temporary in (font_temp, charset_temp):
            if temporary and os.path.exists(temporary):
                os.unlink(temporary)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["environment", "check", "subset"])
    parser.add_argument("input", nargs="?")
    parser.add_argument("--dependencies", default="", help="Project-local fontTools directory")
    parser.add_argument("--request", help="UTF-8 JSON request from the Unity Editor")
    parser.add_argument("--extra-text", default="")
    parser.add_argument("--charset-file", default="")
    parser.add_argument("--no-default", action="store_true")
    parser.add_argument("--allow-missing", action="store_true")
    parser.add_argument("--replace", action="store_true")
    args = parser.parse_args()
    if args.action == "environment":
        load_fonttools(args.dependencies)
        print(json.dumps({"version": FONTTOOLS_VERSION}))
        return
    if args.request:
        request = json.loads(Path(args.request).read_text(encoding="utf-8-sig"))
    else:
        if not args.input:
            parser.error("需要输入字体路径或 --request。")
        request = {"input": args.input, "extraText": args.extra_text, "characterFile": args.charset_file,
                   "useDefault": not args.no_default, "allowMissing": args.allow_missing, "replace": args.replace}
    print(json.dumps(execute(request, args.action == "subset", args.dependencies), ensure_ascii=True))


if __name__ == "__main__":
    try:
        main()
    except InterruptedError as error:
        print(f"[July Font Authoring] {error}", file=sys.stderr)
        sys.exit(130)
    except (OSError, ValueError, ImportError) as error:
        print(f"[July Font Authoring] {error}", file=sys.stderr)
        sys.exit(1)
