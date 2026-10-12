"""Package the desktop's complete text and Segoe Fluent Icons WOFF2 fonts.

Requires fonttools and brotli. No subsetting or outline edits: user device names
retain the desktop's character coverage. Run from any directory. Use
--icons-only to package the icon face without rebuilding the large text faces.
"""
import argparse
from pathlib import Path
from fontTools.ttLib import TTCollection, TTFont
from fontTools.pens.recordingPen import DecomposingRecordingPen

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--icons-only", action="store_true")
args = parser.parse_args()

root = Path(__file__).resolve().parents[1] / "src" / "LanDeviceMonitor.App"
output = root / "Web" / "fonts"
output.mkdir(parents=True, exist_ok=True)
def package(font, source, filename):
    cmap = font.getBestCmap()
    metrics = dict(font["hmtx"].metrics)
    font.flavor = "woff2"
    path = output / filename
    font.save(path)
    restored = TTFont(path)
    assert restored.getBestCmap() == cmap, f"Character coverage changed: {source}"
    assert restored["hmtx"].metrics == metrics, f"Advance widths changed: {source}"
    assert restored.getGlyphOrder() == font.getGlyphOrder(), f"Glyph order changed: {source}"
    # Preserve the face's source metadata, including family, vendor, version,
    # copyright and licensing records. Only the transport container changes.
    names = lambda f: [(n.nameID, n.platformID, n.platEncID, n.langID, n.string) for n in f["name"].names]
    assert names(restored) == names(font), f"Font metadata changed: {source}"
    for table in ["hhea", "OS/2"]:
        assert restored[table].compile(restored) == font[table].compile(font), f"{table} metrics changed: {source}"
    assert restored["head"].unitsPerEm == font["head"].unitsPerEm, f"Em size changed: {source}"
    original_glyphs, restored_glyphs = font.getGlyphSet(), restored.getGlyphSet()
    for glyph in font.getGlyphOrder():
        original_pen = DecomposingRecordingPen(original_glyphs)
        restored_pen = DecomposingRecordingPen(restored_glyphs)
        original_glyphs[glyph].draw(original_pen)
        restored_glyphs[glyph].draw(restored_pen)
        assert original_pen.value == restored_pen.value, f"Outline changed: {source}, {glyph}"
    print(f"{source} -> {path.name}: {len(cmap)} code points, {path.stat().st_size} bytes")
    print("PASS WOFF2 roundtrip: cmap, all outlines, glyph order, hmtx, hhea, OS/2, em size, name metadata")
    return restored

if not args.icons_only:
    for source, name in [("msyhl.ttc", "light"), ("msyh.ttc", "regular"), ("msyhbd.ttc", "bold")]:
        collection = TTCollection(root / "Assets" / "Fonts" / source, recalcTimestamp=False)
        font = next(f for f in collection.fonts
                    if (f["name"].getDebugName(16) or f["name"].getDebugName(1)) == "Microsoft YaHei UI")
        package(font, source, f"app-text-{name}.woff2")

icons = TTFont(root / "Assets" / "Fonts" / "SegoeIcons.ttf", recalcTimestamp=False)
icons = package(icons, "SegoeIcons.ttf", "app-icons.woff2")
print(f"Icon family: {icons['name'].getDebugName(16) or icons['name'].getDebugName(1)}")
for point in [0xE713, 0xE74D, 0xE777]:
    glyph = icons.getBestCmap()[point]
    print(f"U+{point:04X}: {glyph}; advance, left bearing = {icons['hmtx'].metrics[glyph]}")
