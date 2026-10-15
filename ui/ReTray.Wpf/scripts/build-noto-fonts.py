"""Embed the local Noto Sans SC VF and derive genuine WPF-compatible weights.

The source font is copied byte-for-byte. The nine static faces are full axis
instances, without subsetting, outline emboldening, or system font installation.
Requires fontTools; --fonttools-path can point to an existing local tool folder.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import sys


WEIGHTS = {
    100: "Thin",
    200: "ExtraLight",
    300: "Light",
    400: "Regular",
    500: "Medium",
    600: "SemiBold",
    700: "Bold",
    800: "ExtraBold",
    900: "Black",
}
FAMILY = "Noto Sans SC"
SAMPLE = "ReTray永中设置显示隐藏恢复保存登录退出Aa0%"


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def rename_static_face(font, weight: int, style: str) -> None:
    # With an unnamed source axis position, fontTools leaves an empty STAT
    # AxisValueArray. WPF then advertises weight 1 instead of usWeightClass.
    # A full static face has no variation axis; omit that obsolete table and
    # let WPF use its actual OS/2 weight and typographic names.
    if "STAT" in font:
        del font["STAT"]
    # Windows legacy style linking groups only Regular/Bold. The typographic
    # family (name 16) keeps all nine faces in one WPF FontFamily.
    legacy_family = FAMILY if weight in (400, 700) else f"{FAMILY} {style}"
    legacy_style = "Bold" if weight == 700 else "Regular"
    replacements = {
        1: legacy_family,
        2: legacy_style,
        3: f"2.004;ADBO;NotoSansSC-{style};local-wght-{weight}",
        4: f"{FAMILY} {style}",
        6: f"NotoSansSC-{style}",
        16: FAMILY,
        17: style,
    }
    for name_id, text in replacements.items():
        font["name"].removeNames(nameID=name_id)
        font["name"].setName(text, name_id, 3, 1, 0x409)
        font["name"].setName(text, name_id, 3, 1, 0x804)
    font["OS/2"].usWeightClass = weight
    font["OS/2"].fsSelection &= ~(1 | 32 | 64)
    if weight == 700:
        font["OS/2"].fsSelection |= 32
    elif weight == 400:
        font["OS/2"].fsSelection |= 64
    font["head"].macStyle &= ~3
    if weight == 700:
        font["head"].macStyle |= 1


def outline_points(glyph_set, glyph_name: str, pen_type):
    pen = pen_type(glyph_set)
    glyph_set[glyph_name].draw(pen)
    return pen.value


def compare_outlines(expected, actual) -> float:
    assert len(expected) == len(actual), "Glyph contour topology changed"
    largest_delta = 0.0
    for (op1, points1), (op2, points2) in zip(expected, actual):
        assert op1 == op2 and len(points1) == len(points2), "Glyph contour topology changed"
        for point1, point2 in zip(points1, points2):
            if point1 is None or point2 is None:
                assert point1 is None and point2 is None
                continue
            for expected_coordinate, actual_coordinate in zip(point1, point2):
                largest_delta = max(largest_delta, abs(expected_coordinate - actual_coordinate))
    # Pinning TrueType variation deltas to integer coordinates introduces only
    # the required font-unit rounding; it must not invent a different outline.
    assert largest_delta <= 1.0, f"Outline differs by {largest_delta} font units"
    return largest_delta


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--fonttools-path", type=Path)
    args = parser.parse_args()
    if args.fonttools_path:
        sys.path.insert(0, str(args.fonttools_path.resolve()))
    import fontTools
    from fontTools.pens.recordingPen import DecomposingRecordingPen
    from fontTools.ttLib import TTFont
    from fontTools.varLib.instancer import instantiateVariableFont

    source = args.source.resolve(strict=True)
    output = Path(__file__).resolve().parents[1] / "src" / "LanDeviceMonitor.App" / "Assets" / "Fonts"
    variable_dir = output / "Variable"
    static_dir = output / "Static"
    variable_dir.mkdir(parents=True, exist_ok=True)
    static_dir.mkdir(parents=True, exist_ok=True)
    variable_path = variable_dir / "NotoSansSC-VF.ttf"
    shutil.copyfile(source, variable_path)
    assert digest(source) == digest(variable_path), "Variable source bytes changed"

    with TTFont(source, recalcTimestamp=False) as variable:
        axes = {axis.axisTag: (axis.minValue, axis.defaultValue, axis.maxValue) for axis in variable["fvar"].axes}
        assert axes == {"wght": (100.0, 100.0, 900.0)}, f"Unexpected axes: {axes}"
        assert variable["name"].getDebugName(1) == FAMILY, "Unexpected local font family"
        source_cmap = variable.getBestCmap()
        source_glyph_order = variable.getGlyphOrder()
        source_license_names = {name_id: variable["name"].getDebugName(name_id) for name_id in (0, 13, 14)}
        records = []
        for weight, style in WEIGHTS.items():
            print(f"Instantiating wght={weight} ({style})", flush=True)
            with TTFont(source, recalcTimestamp=False) as fresh:
                instance = instantiateVariableFont(fresh, {"wght": weight}, inplace=True, updateFontNames=False, static=True)
                rename_static_face(instance, weight, style)
                path = static_dir / f"NotoSansSC-{style}.ttf"
                instance.save(path)
            with TTFont(path, recalcTimestamp=False) as restored:
                assert "fvar" not in restored and "gvar" not in restored
                assert "STAT" not in restored
                assert restored.getBestCmap() == source_cmap, "Character coverage changed"
                assert restored.getGlyphOrder() == source_glyph_order, "Glyph order changed"
                assert restored["head"].unitsPerEm == variable["head"].unitsPerEm
                assert restored["OS/2"].usWeightClass == weight
                assert {name_id: restored["name"].getDebugName(name_id) for name_id in (0, 13, 14)} == source_license_names
                actual_set = restored.getGlyphSet()
                expected_set = variable.getGlyphSet(location={"wght": weight})
                max_delta = max(compare_outlines(
                    outline_points(expected_set, source_cmap[ord(character)], DecomposingRecordingPen),
                    outline_points(actual_set, source_cmap[ord(character)], DecomposingRecordingPen),
                ) for character in SAMPLE)
                records.append({
                    "weight": weight,
                    "style": style,
                    "family": FAMILY,
                    "path": str(path.relative_to(output)).replace("\\", "/"),
                    "sha256": digest(path),
                    "bytes": path.stat().st_size,
                    "codePoints": len(source_cmap),
                    "glyphs": len(source_glyph_order),
                    "sampleMaximumOutlineDeltaFontUnits": max_delta,
                    "sourceAxisLocation": {"wght": weight},
                    "static": True,
                    "obsoleteVariationStatTableRemoved": True,
                })
        manifest = {
            "source": str(source),
            "sourceSha256": digest(source),
            "sourceFontVersion": variable["name"].getDebugName(5),
            "sourceAxes": {tag: {"min": values[0], "default": values[1], "max": values[2]} for tag, values in axes.items()},
            "fontToolsVersion": fontTools.__version__,
            "variablePath": "Variable/NotoSansSC-VF.ttf",
            "variableCopiedWithoutChanges": True,
            "noSubsetting": True,
            "noSyntheticEmboldening": True,
            "sample": SAMPLE,
            "faces": records,
        }
        (output / "noto-font-manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"PASS: original VF preserved; {len(records)} real static weights; full cmap/glyph order; sample outlines within TrueType rounding.")


if __name__ == "__main__":
    main()
