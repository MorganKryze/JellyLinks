# /// script
# requires-python = ">=3.11"
# dependencies = ["fonttools>=4.50", "uharfbuzz>=0.39"]
# ///
"""Outlines the JellyLinks lettering from Albert Sans (SIL Open Font License 1.1) into plain SVG path data.
Run it only when the lettering changes: `uv run assets/brand/outline.py`. The font is downloaded, used, and not kept."""
import io
import json
import pathlib
import urllib.request

import uharfbuzz as hb
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

URL = "https://github.com/google/fonts/raw/main/ofl/albertsans/AlbertSans%5Bwght%5D.ttf"
HERE = pathlib.Path(__file__).parent


def outline(data, text, weight, size=100):
    """Path data for text set at `size` px, baseline at y=0, x from 0; kerning applied."""
    font = instantiateVariableFont(TTFont(io.BytesIO(data)), {"wght": weight})
    blob = io.BytesIO()
    font.save(blob)
    face = hb.Face(blob.getvalue())
    buf = hb.Buffer()
    buf.add_str(text)
    buf.guess_segment_properties()
    hb.shape(hb.Font(face), buf, {"kern": True, "liga": True})
    scale = size / font["head"].unitsPerEm
    glyphs, order = font.getGlyphSet(), font.getGlyphOrder()
    pen, x = SVGPathPen(glyphs), 0
    for info, pos in zip(buf.glyph_infos, buf.glyph_positions):
        # font units are y-up, SVG is y-down
        glyphs[order[info.codepoint]].draw(TransformPen(pen, (scale, 0, 0, -scale, (x + pos.x_offset) * scale, -pos.y_offset * scale)))
        x += pos.x_advance
    return {"d": pen.getCommands(), "width": round(x * scale, 2), "capHeight": round(font["OS/2"].sCapHeight * scale, 2), "size": size}


with urllib.request.urlopen(URL) as response:
    data = response.read()
lettering = {
    "wordmark": outline(data, "JellyLinks", 450),
    "tagline": outline(data, "Signed download links for your Jellyfin library", 400),
}
(HERE / "lettering.json").write_text(json.dumps(lettering, indent=1) + "\n")
print("lettering.json written:", lettering["wordmark"]["width"], lettering["tagline"]["width"])
