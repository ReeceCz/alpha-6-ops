"""Builds the Sierra 4 logo assets from Dan's lockup concepts (Option 3 header lockup, Option 6 tile).

Text is converted to outlines with fontTools so no asset depends on a web font. Fonts are SIL OFL:
Michroma (wordmark) and Barlow Semi Condensed Black (the S4 in the tile). Run from the repository root:

    python tools/brand/build_logo.py

The fonts and their licences are in tools/brand/fonts.

Writes the SVGs (header lockups in night/day and full/compact, the tile) and the favicons under
src/Alpha6Ops.Server/wwwroot. Needs fonttools and Pillow.
"""
import pathlib
from fontTools.ttLib import TTFont
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from PIL import Image, ImageDraw, ImageFont

FONTS = pathlib.Path(__file__).parent / "fonts"
SERVER = pathlib.Path("src/Alpha6Ops.Server")
WWW = SERVER / "wwwroot"
YELLOW, BLACK, WHITE = "#ffc20e", "#000000", "#ffffff"


class Face:
    def __init__(self, path):
        self.file = str(path)
        self.font = TTFont(path)
        self.upem = self.font["head"].unitsPerEm
        self.cap = self.font["OS/2"].sCapHeight or self.font["glyf"]["H"].yMax
        self.cmap = self.font.getBestCmap()
        self.glyphs = self.font.getGlyphSet()

    def size_for_cap(self, cap_px):
        return cap_px * self.upem / self.cap

    def advance(self, ch, size):
        return self.font["hmtx"][self.cmap[ord(ch)]][0] * size / self.upem

    def width(self, text, size, tracking=0.0):
        return sum(self.advance(c, size) for c in text) + tracking * (len(text) - 1)

    def path(self, text, size, x, baseline, tracking=0.0, custom=None):
        """SVG path data for text set at (x, baseline). custom maps a character to a function drawing it instead."""
        pen, scale, parts = SVGPathPen(self.glyphs, ntos=r), size / self.upem, []
        for c in text:
            if custom and c in custom:
                parts.append(custom[c](x, baseline, size))
            else:
                self.glyphs[self.cmap[ord(c)]].draw(TransformPen(pen, (scale, 0, 0, -scale, x, baseline)))
            x += self.advance(c, size) + tracking
        return pen.getCommands() + "".join(parts)

    def stem(self, size):
        g = self.font["glyf"]["I"] if "I" in self.font["glyf"] else None
        return (g.xMax - g.xMin) * size / self.upem


def r(v):
    s = f"{v:.1f}".rstrip("0").rstrip(".")
    return "0" if s == "-0" else s


wide = Face(FONTS / "Michroma-Regular.ttf")
heavy = Face(FONTS / "BarlowSemiCondensed-Black.ttf")


def s4_em(size):
    """Em size for the tile's S4: as tall as the art allows, but always clear of the inner border."""
    fit = (size * (1 - 2 * 0.055) - size * 0.14) / heavy.width("S4", 1.0, -0.02)
    return min(heavy.size_for_cap(size * 0.6), fit)


# Tile: the yellow taxiway-sign square with a black inner border and a heavy black S4 (Option 6).
def tile(size, border=True):
    rx, inset = size * 0.16, size * 0.055
    s4_size = s4_em(size)
    s4_w = heavy.width("S4", s4_size, -size * 0.02)
    s4 = heavy.path("S4", s4_size, (size - s4_w) / 2, size / 2 + size * 0.3, -size * 0.02)
    inner = (f'<rect x="{r(inset)}" y="{r(inset)}" width="{r(size - 2 * inset)}" height="{r(size - 2 * inset)}" rx="{r(rx - inset)}" '
             f'fill="none" stroke="{BLACK}" stroke-width="{r(size * 0.028)}"/>') if border else ""
    return f'<rect width="{size}" height="{size}" rx="{r(rx)}" fill="{YELLOW}"/>{inner}<path d="{s4}" fill="{BLACK}"/>'


# RYNEL: Michroma with the E drawn as three bars, as in Dan's lockups.
def bars_e(size):
    def draw(x, baseline, _):
        w, cap, t = wide.advance("E", size) * 0.86, wide.cap * size / wide.upem, wide.stem(size) * 0.82
        return "".join(f"M{r(x)} {r(y)}h{r(w)}v{r(t)}h{r(-w)}Z" for y in (baseline - cap, baseline - cap / 2 - t / 2, baseline - t))
    return {"E": draw}


H = 56  # lockup height in px, the size it is drawn at in the header


def lockup(full, ink, accent, rule, muted):
    """Option 3: tile | SIERRA 4 over the subtitle | POWERED BY over RYNEL.
    Michroma has one weight, so the display words get a same-colour stroke to match the heavier lettering in Dan's art."""
    def bold(colour, weight):
        return f'fill="{colour}" stroke="{colour}" stroke-width="{weight}" stroke-linejoin="round"'
    x = H + 14
    sub_text, sub_size = "AIRLINE OPERATIONS PLATFORM", wide.size_for_cap(4.4)
    title_size = wide.size_for_cap(19)
    title_track = title_size * 0.06
    title_w = wide.width("SIERRA 4", title_size, title_track)
    sub_track = max(sub_size * 0.3, (title_w - wide.width(sub_text, sub_size)) / (len(sub_text) - 1))
    sub_w = wide.width(sub_text, sub_size, sub_track)
    if sub_w > title_w:  # widen the title to the subtitle, as the two lines share a measure in the art
        title_track += (sub_w - title_w) / 7
        title_w = sub_w
    parts = [tile(H),
             f'<path {bold(ink, 0.9)} d="{wide.path("SIERRA 4", title_size, x, 30, title_track)}"/>',
             f'<path fill="{accent}" d="{wide.path(sub_text, sub_size, x, 45, sub_track)}"/>']
    width = x + title_w
    if full:
        rx = width + 18
        parts.append(f'<line x1="{r(rx)}" y1="9" x2="{r(rx)}" y2="47" stroke="{rule}" stroke-width="1.2"/>')
        px = rx + 18
        rynel_size = wide.size_for_cap(16)
        rynel_track = rynel_size * 0.08
        rynel_w = wide.width("RYNEL", rynel_size, rynel_track)
        pb_size = wide.size_for_cap(4.4)
        pb_track = (rynel_w * 0.8 - wide.width("POWERED BY", pb_size)) / 9
        parts.append(f'<path fill="{muted}" d="{wide.path("POWERED BY", pb_size, px, 20, pb_track)}"/>')
        parts.append(f'<path {bold(ink, 0.8)} d="{wide.path("RYNEL", rynel_size, px, 43, rynel_track, bars_e(rynel_size))}"/>')
        width = px + rynel_w + 1
    return width, "".join(parts)


def svg_file(width, height, body, label):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{r(width)}" height="{height}" viewBox="0 0 {r(width)} {height}" '
            f'role="img" aria-label="{label}"><title>{label}</title>{body}</svg>\n')


brand = WWW / "images" / "brand"
brand.mkdir(parents=True, exist_ok=True)
(brand / "s4-tile.svg").write_text(svg_file(64, 64, tile(64), "Sierra 4"), encoding="utf-8")
# Header lockups: night (dark) and day (light) colours match the site tokens; compact drops POWERED BY RYNEL.
THEMES = {"dark": (WHITE, YELLOW, "#5b626a", "#c3cad1"), "light": ("#0f1316", "#7a5800", "#aeb6be", "#3b444c")}
widths = {}
for theme, colours in THEMES.items():
    for variant, full in (("full", True), ("compact", False)):
        w, body = lockup(full, *colours)
        widths[variant] = w
        label = "Sierra 4, airline operations platform" + (", powered by RYNEL" if full else "")
        (brand / f"s4-lockup-{variant}-{theme}.svg").write_text(svg_file(w, H, body, label), encoding="utf-8")
(WWW / "favicon.svg").write_text(svg_file(64, 64, tile(64), "S4 OPS"), encoding="utf-8")


# Raster icons: drawn with Pillow from the same fonts, supersampled. Small sizes drop the inner border.
def raster(size, border):
    scale = 8
    s = size * scale
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((0, 0, s - 1, s - 1), radius=s * 0.16, fill=YELLOW)
    if border:
        inset = s * 0.055
        d.rounded_rectangle((inset, inset, s - inset, s - inset), radius=s * 0.105, outline=BLACK, width=max(1, round(s * 0.028)))
    font = ImageFont.truetype(heavy.file, round(s4_em(s)))
    d.text((s / 2, s / 2 + s * 0.3), "S4", font=font, fill=BLACK, anchor="ms")
    return img.resize((size, size), Image.LANCZOS)


ico_frames = [raster(n, n >= 32) for n in (16, 32, 48)]
ico_frames[-1].save(WWW / "favicon.ico", sizes=[(16, 16), (32, 32), (48, 48)], append_images=ico_frames[:-1])
raster(180, True).save(WWW / "apple-touch-icon.png")
raster(512, True).save(brand / "s4-tile-512.png")
print("wrote", brand, WWW / "favicon.ico", {k: round(v) for k, v in widths.items()})
