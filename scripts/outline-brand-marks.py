#!/usr/bin/env python3
"""Outlines the keypaste marks from Hepta Slab and writes every file drawn from them.

The icon is "k." and the wordmark "keypaste.", each ending in a square amber dot; they are never
set side by side (docs/BRAND.md). The wordmark is Hepta Slab at weight 500 tracked -1.5%, so it sits
beside the interface's Instrument Sans; the icon is 580 tracked -1%, so it holds its weight on a
tile and at 16 px. Each dot's side is a multiple of that weight's stem, set a fixed share of an em
after the last letter's ink. Outlines are the source of truth, so no font is vendored: this writes

    assets/brand/                           keypaste-glyph-{dark,light,mono}.svg,
                                             keypaste-wordmark-{dark,light,mono}.svg,
                                             keypaste-app-icon.svg, keypaste-favicon.svg
    site/public/favicon.svg                  the favicon cut
    src/Keypaste.App/Theme/BrandOutlines.axaml  the geometry BrandMark and BrandWordmark draw
    the site's pages and share worker         the wordmark each carries inline

and scripts/render-app-icon.py rasterizes the two tiles. It reads the variable font from
google/fonts, ofl/heptaslab/HeptaSlab[wght].ttf (SIL Open Font License 1.1), and refuses any other
bytes, so the same file always gives the same outlines. Needs fontTools, uharfbuzz and skia-pathops.

    python scripts/outline-brand-marks.py <path to HeptaSlab[wght].ttf>
"""

import hashlib
import io
import re
import sys
from pathlib import Path

import pathops
import uharfbuzz as hb
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont
from fontTools.ttLib.removeOverlaps import removeOverlaps
from fontTools.varLib.instancer import instantiateVariableFont

ROOT = Path(__file__).resolve().parent.parent
FONT_SHA256 = "737badc7944d5b6f92c05919995240d6ea70e86bebe45d196e61677e7a901f29"
UNITS_PER_EM = 100

# dot: the square's side in stems of its weight; gap: from the last letter's ink to the dot, in em.
GLYPH = {"text": "k", "weight": 580, "tracking": -0.01, "dot": 1.30, "gap": 0.08}
WORDMARK = {"text": "keypaste", "weight": 500, "tracking": -0.015, "dot": 1.45, "gap": 0.075}

INK_DARK, INK_LIGHT, AMBER, TILE = "#F2F2F0", "#111214", "#F2B544", "#1D1E22"
# The glyph's share of a 64-unit tile: the app icon's, and the favicon cut's, larger so its serifs survive 16 px.
APP_ICON = {"height": 0.46, "radius": 15}
FAVICON = {"height": 0.66, "radius": 12}
MAX_WIDTH = 0.62

SITE_WORDMARKS = ["site/public/index.html", "site/public/s/index.html", "site/public/thanks/index.html", "site/src/worker.js"]
SITE_WORDMARK_WIDTH = 112
INLINE_WORDMARK = re.compile(
    r'<svg width="\d+" height="[\d.]+" viewBox="[^"]*" aria-hidden="true">'
    r'<path class="mark-ink" d="[^"]*"/><path class="mark-dot" d="[^"]*"/></svg>'
)


def number(value):
    text = f"{value:.2f}".rstrip("0").rstrip(".")
    return "0" if text == "-0" else text


def recorded_font(path):
    data = Path(path).read_bytes()
    digest = hashlib.sha256(data).hexdigest()
    if digest != FONT_SHA256:
        sys.exit(f"{path} is not the recorded HeptaSlab[wght].ttf (sha256 {digest})")
    return data


def instance(data, weight):
    font = instantiateVariableFont(TTFont(io.BytesIO(data)), {"wght": weight}, inplace=False)
    removeOverlaps(font)
    buffer = io.BytesIO()
    font.save(buffer)
    return TTFont(io.BytesIO(buffer.getvalue())), buffer.getvalue()


def rectangle(left, bottom, right, top):
    path = pathops.Path()
    path.moveTo(left, bottom)
    path.lineTo(right, bottom)
    path.lineTo(right, top)
    path.lineTo(left, top)
    path.close()
    return path


def stem(font):
    """The width of the "l"'s stem halfway up the x-height, in font units."""
    l = pathops.Path()
    font.getGlyphSet()[font.getBestCmap()[ord("l")]].draw(l.getPen())
    middle = font["OS/2"].sxHeight / 2
    left, _, right, _ = pathops.op(l, rectangle(-1, middle, font["head"].unitsPerEm, middle + 1), pathops.PathOp.INTERSECTION).bounds
    return right - left


def outline(data, mark):
    """The ink and the dot of a mark, in units of 1/100 em with the box's top-left at the origin."""
    font, face = instance(data, mark["weight"])
    upm = font["head"].unitsPerEm
    glyphs = font.getGlyphSet()
    order = font.getGlyphOrder()

    buffer = hb.Buffer()
    buffer.add_str(mark["text"])
    buffer.guess_segment_properties()
    hb.shape(hb.Font(hb.Face(face)), buffer, {"kern": True, "liga": False})

    ink, x = pathops.Path(), 0.0
    for info, position in zip(buffer.glyph_infos, buffer.glyph_positions):
        glyphs[order[info.codepoint]].draw(TransformPen(ink.getPen(), (1, 0, 0, 1, x + position.x_offset, position.y_offset)))
        x += position.x_advance + mark["tracking"] * upm
    # Tracked letters may touch, and the app fills even-odd, so the letters become one outline.
    ink.simplify(fix_winding=True)

    start, side = ink.bounds[2] + mark["gap"] * upm, mark["dot"] * stem(font)
    dot = rectangle(start, 0, start + side, side)

    left, bottom, right, top = ink.bounds[0], min(ink.bounds[1], 0), start + side, ink.bounds[3]
    scale = UNITS_PER_EM / upm

    def path(shape):
        pen = SVGPathPen(None, ntos=number)
        shape.draw(TransformPen(pen, (scale, 0, 0, -scale, -left * scale, top * scale)))
        return pen.getCommands()

    return {"ink": path(ink), "dot": path(dot), "width": (right - left) * scale, "height": (top - bottom) * scale}


def placed_in_tile(mark, cut):
    """The outline moved and scaled into a 64-unit tile, centred, as the app icon and favicon draw it."""
    aspect = mark["width"] / mark["height"]
    height = 64 * cut["height"]
    width = height * aspect
    if width > 64 * MAX_WIDTH:
        width = 64 * MAX_WIDTH
        height = width / aspect
    scale = height / mark["height"]
    x, y = (64 - width) / 2, (64 - height) / 2

    def moved(d):
        pen = SVGPathPen(None, ntos=number)
        transform = TransformPen(pen, (scale, 0, 0, scale, x, y))
        replay(d, transform)
        return pen.getCommands()

    return moved(mark["ink"]), moved(mark["dot"])


def replay(d, pen):
    """Draws an absolute path, as SVGPathPen writes one, into another pen."""
    for letter in "MLHVQCZ":
        d = d.replace(letter, f" {letter} ")
    tokens = d.split()
    arity = {"M": 2, "L": 2, "H": 1, "V": 1, "Q": 4, "C": 6, "Z": 0}
    index, current, is_open = 0, (0.0, 0.0), False
    while index < len(tokens):
        command = tokens[index]
        values = [float(token) for token in tokens[index + 1:index + 1 + arity[command]]]
        index += 1 + arity[command]
        if command == "Z":
            pen.closePath()
            is_open = False
            continue
        if command == "H":
            points = [(values[0], current[1])]
        elif command == "V":
            points = [(current[0], values[0])]
        else:
            points = list(zip(values[0::2], values[1::2]))
        if command == "M":
            if is_open:
                pen.closePath()
            pen.moveTo(points[0])
            is_open = True
        elif command in "LHV":
            pen.lineTo(points[0])
        elif command == "Q":
            pen.qCurveTo(*points)
        else:
            pen.curveTo(*points)
        current = points[-1]


def svg(width, height, body, size=None):
    w, h = size or (number(width), number(height))
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" '
        f'viewBox="0 0 {number(width)} {number(height)}" role="img" aria-label="keypaste">\n{body}</svg>\n'
    )


def marks(glyph, wordmark):
    files = {}
    for name, mark in (("glyph", glyph), ("wordmark", wordmark)):
        for variant, ink, dot in (("dark", INK_DARK, AMBER), ("light", INK_LIGHT, AMBER), ("mono", "currentColor", "currentColor")):
            body = f'  <path fill="{ink}" d="{mark["ink"]}"/>\n  <path fill="{dot}" d="{mark["dot"]}"/>\n'
            files[f"keypaste-{name}-{variant}.svg"] = svg(mark["width"], mark["height"], body)

    for name, cut, size in (("keypaste-app-icon.svg", APP_ICON, 1024), ("keypaste-favicon.svg", FAVICON, 32)):
        ink, dot = placed_in_tile(glyph, cut)
        body = (
            f'  <rect width="64" height="64" rx="{cut["radius"]}" fill="{TILE}"/>\n'
            f'  <path fill="{INK_DARK}" d="{ink}"/>\n  <path fill="{AMBER}" d="{dot}"/>\n'
        )
        files[name] = svg(64, 64, body, size=(size, size))
    return files


def axaml(glyph, wordmark):
    return (
        '<ResourceDictionary xmlns="https://github.com/avaloniaui"\n'
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">\n\n'
        "  <!-- Written by scripts/outline-brand-marks.py from Hepta Slab; regenerate rather than edit.\n"
        "       Units are 1/100 em with each mark's top-left at the origin. -->\n"
        f'  <StreamGeometry x:Key="KpGlyphInkGeometry">{glyph["ink"]}</StreamGeometry>\n'
        f'  <StreamGeometry x:Key="KpGlyphDotGeometry">{glyph["dot"]}</StreamGeometry>\n'
        f'  <StreamGeometry x:Key="KpWordmarkInkGeometry">{wordmark["ink"]}</StreamGeometry>\n'
        f'  <StreamGeometry x:Key="KpWordmarkDotGeometry">{wordmark["dot"]}</StreamGeometry>\n\n'
        "</ResourceDictionary>\n"
    )


def inline_wordmark(wordmark):
    height = SITE_WORDMARK_WIDTH * wordmark["height"] / wordmark["width"]
    return (
        f'<svg width="{SITE_WORDMARK_WIDTH}" height="{number(height)}" '
        f'viewBox="0 0 {number(wordmark["width"])} {number(wordmark["height"])}" aria-hidden="true">'
        f'<path class="mark-ink" d="{wordmark["ink"]}"/><path class="mark-dot" d="{wordmark["dot"]}"/></svg>'
    )


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    data = recorded_font(sys.argv[1])
    glyph, wordmark = outline(data, GLYPH), outline(data, WORDMARK)
    files = marks(glyph, wordmark)
    for name, text in files.items():
        (ROOT / "assets" / "brand" / name).write_text(text, encoding="utf-8", newline="\n")
    (ROOT / "site" / "public" / "favicon.svg").write_text(files["keypaste-favicon.svg"], encoding="utf-8", newline="\n")
    (ROOT / "src" / "Keypaste.App" / "Theme" / "BrandOutlines.axaml").write_text(axaml(glyph, wordmark), encoding="utf-8", newline="\n")
    for name in SITE_WORDMARKS:
        page = ROOT / name
        text, count = INLINE_WORDMARK.subn(lambda _: inline_wordmark(wordmark), page.read_text(encoding="utf-8"))
        if count == 0:
            sys.exit(f"{name} carries no inline wordmark to replace")
        page.write_text(text, encoding="utf-8", newline="\n")
    print(f"wrote {len(files)} marks to assets/brand, the site favicon, BrandOutlines.axaml "
          f"and the inline wordmark in {len(SITE_WORDMARKS)} site files")


if __name__ == "__main__":
    main()
