import os
import re
import sys
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
SVG_DIR = os.path.join(HERE, "svg")
REPO_RAW = "https://raw.githubusercontent.com/microsoft/fluentui-system-icons/main"
OUT_TTF = os.path.normpath(os.path.join(HERE, "..", "..", "src", "Lumenotepad", "Assets", "Fonts", "LumenIcons.ttf"))
NOTICE = os.path.normpath(os.path.join(HERE, "..", "..", "THIRD-PARTY-NOTICES.md"))

UPM = 2048

ICONS = {
    0xE70D: ("chevron_down", ["Chevron Down"], "chevron_down"),
    0xE70E: ("chevron_up", ["Chevron Up"], "chevron_up"),
    0xE70F: ("edit", ["Edit"], "edit"),
    0xE710: ("add", ["Add"], "add"),
    0xE711: ("dismiss", ["Dismiss"], "dismiss"),
    0xE712: ("more_horizontal", ["More Horizontal"], "more_horizontal"),
    0xE713: ("settings", ["Settings"], "settings"),
    0xE71B: ("link", ["Link"], "link"),
    0xE723: ("attach", ["Attach"], "attach"),
    0xE72B: ("arrow_left", ["Arrow Left"], "arrow_left"),
    0xE72E: ("lock_closed", ["Lock Closed"], "lock_closed"),
    0xE76F: ("arrow_move", ["Arrow Move"], "arrow_move"),
    0xE790: ("color", ["Color"], "color"),
    0xE7A7: ("arrow_undo", ["Arrow Undo"], "arrow_undo"),
    0xE7E6: ("highlight", ["Highlight"], "highlight"),
    0xE80A: ("table", ["Table"], "table"),
    0xE80F: ("home", ["Home"], "home"),
    0xE81C: ("history", ["History"], "history"),
    0xE82F: ("lightbulb", ["Lightbulb"], "lightbulb"),
    0xE897: ("question", ["Question"], "question"),
    0xE8A1: ("panel_left", ["Panel Left"], "panel_left"),
    0xE8A5: ("document", ["Document"], "document"),
    0xE8BB: ("dismiss_chrome", ["Dismiss"], "dismiss"),
    0xE8CB: ("arrow_sort", ["Arrow Sort"], "arrow_sort"),
    0xE8D2: ("text_font", ["Text Font"], "text_font"),
    0xE8D3: ("text_color", ["Text Color"], "text_color"),
    0xE8DB: ("text_italic", ["Text Italic"], "text_italic"),
    0xE8DC: ("text_underline", ["Text Underline"], "text_underline"),
    0xE8DD: ("text_bold", ["Text Bold"], "text_bold"),
    0xE8E2: ("text_align_right", ["Text Align Right"], "text_align_right"),
    0xE8E3: ("text_align_center", ["Text Align Center"], "text_align_center"),
    0xE8E4: ("text_align_left", ["Text Align Left"], "text_align_left"),
    0xE8EC: ("tag", ["Tag"], "tag"),
    0xE8FD: ("text_bullet_list", ["Text Bullet List LTR", "Text Bullet List Ltr", "Text Bullet List"], "text_bullet_list_ltr"),
    0xE921: ("subtract", ["Subtract"], "subtract"),
    0xE922: ("maximize", ["Maximize"], "maximize"),
    0xE948: ("font_increase", ["Font Increase"], "font_increase"),
    0xE949: ("font_decrease", ["Font Decrease"], "font_decrease"),
    0xE9A6: ("scale_fit", ["Scale Fit"], "scale_fit"),
    0xEA90: ("document_pdf", ["Document Pdf", "Document PDF"], "document_pdf"),
    0xEB9F: ("image", ["Image"], "image"),
    0xEDE0: ("text_strikethrough", ["Text Strikethrough"], "text_strikethrough"),
}

def fetch(url: str, dest: str) -> bool:
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "lumenicons-build"})
        with urllib.request.urlopen(req, timeout=30) as r:
            data = r.read()
        with open(dest, "wb") as f:
            f.write(data)
        return True
    except Exception:
        return False

def download_all() -> dict:
    os.makedirs(SVG_DIR, exist_ok=True)
    paths, failures = {}, []
    for cp, (gname, folders, snake) in ICONS.items():
        local = os.path.join(SVG_DIR, f"{gname}.svg")
        if os.path.exists(local) and os.path.getsize(local) > 0:
            paths[cp] = local
            continue
        got = False
        for folder in folders:
            for size in (24, 20):
                url = f"{REPO_RAW}/assets/{urllib.parse.quote(folder)}/SVG/ic_fluent_{snake}_{size}_regular.svg"
                if fetch(url, local):
                    print(f"  fetched {gname} ({size}px, '{folder}')")
                    got = True
                    break
            if got:
                break
        if got:
            paths[cp] = local
        else:
            failures.append((hex(cp), gname, folders))
    if failures:
        for f in failures:
            print("MISSING:", f, file=sys.stderr)
        sys.exit(f"{len(failures)} icon(s) could not be downloaded - fix the mapping and re-run")
    return paths

def svg_view_size(svg: str) -> float:
    m = re.search(r'viewBox="\s*0\s+0\s+([\d.]+)\s+([\d.]+)', svg)
    if m:
        return float(m.group(2))
    m = re.search(r'height="(\d+)', svg)
    return float(m.group(1)) if m else 24.0

CHROME_FILL = {0xE8BB, 0xE921, 0xE922}

def build_font(paths: dict) -> None:
    from fontTools.fontBuilder import FontBuilder
    from fontTools.pens.boundsPen import BoundsPen
    from fontTools.pens.cu2quPen import Cu2QuPen
    from fontTools.pens.recordingPen import RecordingPen
    from fontTools.pens.transformPen import TransformPen
    from fontTools.pens.ttGlyphPen import TTGlyphPen
    from fontTools.svgLib.path import parse_path

    glyphs, cmap = {}, {}
    order = [".notdef"]
    pen = TTGlyphPen(None)
    glyphs[".notdef"] = pen.glyph()

    for cp in sorted(paths):
        gname, _, _ = ICONS[cp]
        if gname in glyphs:
            cmap[cp] = gname
            continue
        with open(paths[cp], encoding="utf-8") as f:
            svg = f.read()
        box = svg_view_size(svg)
        s = UPM / box
        rec = RecordingPen()
        xform = TransformPen(rec, (s, 0, 0, -s, 0, UPM))
        for d in re.findall(r'\bd="([^"]+)"', svg):
            parse_path(d, xform)
        tt = TTGlyphPen(None)
        quad = Cu2QuPen(tt, max_err=UPM / 1000)
        if cp in CHROME_FILL:
            bp = BoundsPen(None)
            rec.replay(bp)
            x0, y0, x1, y1 = bp.bounds
            k = UPM / max(x1 - x0, y1 - y0)
            c = UPM / 2 * (1 - k)
            rec.replay(TransformPen(quad, (k, 0, 0, k, c, c)))
        else:
            rec.replay(quad)
        glyphs[gname] = tt.glyph()
        order.append(gname)
        cmap[cp] = gname

    fb = FontBuilder(UPM, isTTF=True)
    fb.setupGlyphOrder(order)
    fb.setupCharacterMap(cmap)
    fb.setupGlyf(glyphs)
    glyf = fb.font["glyf"]
    metrics = {}
    for name in order:
        g = glyf[name]
        lsb = g.xMin if g.numberOfContours else 0
        metrics[name] = (UPM, lsb)
    fb.setupHorizontalMetrics(metrics)
    fb.setupHorizontalHeader(ascent=UPM, descent=0)
    fb.setupNameTable({
        "familyName": "Lumen Icons",
        "styleName": "Regular",
        "uniqueFontIdentifier": "LumenIcons-Regular;Lumenotepad",
        "fullName": "Lumen Icons Regular",
        "psName": "LumenIcons-Regular",
        "version": "Version 1.0",
        "copyright": "Glyph outlines from Microsoft fluentui-system-icons (MIT).",
        "licenseDescription": "Glyphs (c) Microsoft Corporation, MIT license - see THIRD-PARTY-NOTICES.md.",
    })
    fb.setupOS2(sTypoAscender=UPM, sTypoDescender=0, sTypoLineGap=0,
                usWinAscent=UPM, usWinDescent=0,
                sxHeight=UPM // 2, sCapHeight=UPM, achVendID="LUMN")
    fb.setupPost()
    os.makedirs(os.path.dirname(OUT_TTF), exist_ok=True)
    fb.save(OUT_TTF)
    print(f"wrote {OUT_TTF} ({os.path.getsize(OUT_TTF)} bytes, {len(order) - 1} glyphs, {len(cmap)} codepoints)")

def write_notice() -> None:
    text = """# Third-party notices

## Fluent UI System Icons (glyph outlines in Assets/Fonts/LumenIcons.ttf)

The icon shapes compiled into LumenIcons.ttf are from Microsoft's fluentui-system-icons
project (https://github.com/microsoft/fluentui-system-icons), used under the MIT License:

MIT License

Copyright (c) 2020 Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy of this
software and associated documentation files (the "Software"), to deal in the Software
without restriction, including without limitation the rights to use, copy, modify, merge,
publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.
"""
    with open(NOTICE, "w", encoding="utf-8") as f:
        f.write(text)
    print(f"wrote {NOTICE}")

if __name__ == "__main__":
    import urllib.parse
    print("downloading SVGs...")
    svg_paths = download_all()
    print("building font...")
    build_font(svg_paths)
    write_notice()
