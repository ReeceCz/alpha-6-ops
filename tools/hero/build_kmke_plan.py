"""Builds the landing-page background: a stylised night plan of General Mitchell International (KMKE).

Map data © OpenStreetMap contributors (ODbL); the page credits it. Run from the repository root:

    python tools/hero/build_kmke_plan.py --fetch   # download work/osm/kmke.json from the Overpass API, then build
    python tools/hero/build_kmke_plan.py           # rebuild from the downloaded file

Writes src/Alpha6Ops.Server/wwwroot/images/hero/kmke-plan.svg. North is up. Approach lighting follows the FAA data
for each runway end (AirNav, October 2026): ALSF-2 on 1L, MALSR on 19R, 7R and 25L.
"""
import json, math, pathlib, sys, urllib.parse, urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[2]
RAW = ROOT / "work" / "osm" / "kmke.json"
OUT = ROOT / "src" / "Alpha6Ops.Server" / "wwwroot" / "images" / "hero" / "kmke-plan.svg"
QUERY = """[out:json][timeout:60];(
way["aeroway"~"runway|taxiway|apron|taxilane|terminal|hangar"](42.915,-87.935,42.975,-87.860);
way["building"](42.932,-87.915,42.963,-87.875);
way["highway"~"motorway|trunk|primary|secondary|tertiary|residential|unclassified|motorway_link|trunk_link"](42.905,-87.950,42.985,-87.845););
out geom;"""

if "--fetch" in sys.argv:
    RAW.parent.mkdir(parents=True, exist_ok=True)
    req = urllib.request.Request("https://overpass-api.de/api/interpreter", data=urllib.parse.urlencode({"data": QUERY}).encode(),
                                 headers={"User-Agent": "S4OPS-hero-builder/1.0 (map art for a flight-sim site)", "Accept": "application/json"})
    RAW.write_bytes(urllib.request.urlopen(req, timeout=180).read())

data = json.loads(RAW.read_text(encoding="utf-8"))

# Local metres around the field, north up. The view puts the airport in the right-hand part of the frame, where the
# hero leaves room beside its copy.
LAT0, LON0 = 42.9450, -87.8970
MPN, MPE = 111132.0, 111320.0 * math.cos(math.radians(LAT0))
VIEW = (-6480, -2040, 8800, 4950)  # x, y, width, height in metres; the field sits about two thirds across


def xy(lat, lon):
    return ((lon - LON0) * MPE, -(lat - LAT0) * MPN)


def simplify(pts, tol):
    """Douglas–Peucker, so the street grid stays light."""
    if len(pts) < 3:
        return pts
    if pts[0] == pts[-1]:
        # A closed ring has no chord to measure from: split it at its midpoint and simplify each half.
        mid = len(pts) // 2
        return simplify(pts[: mid + 1], tol)[:-1] + simplify(pts[mid:], tol)
    (ax, ay), (bx, by) = pts[0], pts[-1]
    dx, dy = bx - ax, by - ay
    norm = math.hypot(dx, dy) or 1
    far, idx = 0.0, 0
    for i, (px, py) in enumerate(pts[1:-1], 1):
        d = abs(dy * px - dx * py + bx * ay - by * ax) / norm
        if d > far:
            far, idx = d, i
    if far <= tol:
        return [pts[0], pts[-1]]
    return simplify(pts[: idx + 1], tol)[:-1] + simplify(pts[idx:], tol)


def inside(pts, margin=800):
    x0, y0, w, h = VIEW
    return any(x0 - margin < x < x0 + w + margin and y0 - margin < y < y0 + h + margin for x, y in pts)


def path(pts, close=False):
    return "M" + " L".join(f"{x:.0f} {y:.0f}" for x, y in pts) + (" Z" if close else "")


def offset(pts, d):
    """A polyline shifted sideways by d metres (left of travel for positive d)."""
    out = []
    for i, (x, y) in enumerate(pts):
        (ax, ay) = pts[max(i - 1, 0)]
        (bx, by) = pts[min(i + 1, len(pts) - 1)]
        tx, ty = bx - ax, by - ay
        n = math.hypot(tx, ty) or 1
        out.append((x - ty / n * d, y + tx / n * d))
    return out


layers = {k: [] for k in ("road-minor", "road-major", "road-motorway", "building", "apron", "taxiway", "taxi-line", "runway", "airport-building")}
runways = []
for e in data["elements"]:
    t, geom = e.get("tags", {}), e.get("geometry")
    if not geom:
        continue
    pts = [xy(p["lat"], p["lon"]) for p in geom]
    if not inside(pts):
        continue
    aero, hw = t.get("aeroway"), t.get("highway")
    if aero == "runway":
        width = float(t["width"]) if t.get("width", "").replace(".", "", 1).isdigit() else 45
        runways.append((t.get("ref", ""), pts, width))
    elif aero in ("taxiway", "taxilane"):
        layers["taxiway"].append(path(simplify(pts, 1.5)))
        layers["taxi-line"].append(path(simplify(pts, 1.5)))
    elif aero == "apron":
        layers["apron"].append(path(simplify(pts, 2), True))
    elif aero in ("terminal", "hangar") or ("building" in t and aero):
        layers["airport-building"].append(path(simplify(pts, 1), True))
    elif "building" in t:
        layers["building"].append(path(simplify(pts, 1.5), True))
    elif hw in ("motorway", "trunk"):
        layers["road-motorway"].append(path(simplify(pts, 4)))
    elif hw in ("primary", "secondary", "motorway_link", "trunk_link", "primary_link", "secondary_link"):
        layers["road-major"].append(path(simplify(pts, 3)))
    elif hw:
        layers["road-minor"].append(path(simplify(pts, 3)))

# Runways: pavement, edge lights and centre-line lights. Lights are drawn as dotted strokes (round caps on a zero-length
# dash), so the spacing is real: 200 ft edge, 50 ft centre line.
lights_edge, lights_centre, runway_paths = [], [], []
by_ref = {}
for ref, pts, width in runways:
    runway_paths.append(f'<path class="runway" d="{path(pts)}" stroke-width="{width:.0f}" />')
    for side in (1, -1):
        lights_edge.append(path(offset(pts, side * width / 2)))
    lights_centre.append(path(pts))
    by_ref.setdefault(ref, []).extend(pts)


def ends(ref):
    pts = by_ref[ref]
    # The two points furthest apart are the runway ends.
    best, pair = -1, None
    for a in pts:
        for b in pts:
            d = math.dist(a, b)
            if d > best:
                best, pair = d, (a, b)
    return pair


approach = []


def approach_lights(end, toward, length_ft, crossbar_ft, side_rows_ft=0):
    """Approach lights extending outwards from a runway end, away from the runway."""
    (ex, ey), (tx, ty) = end, toward
    ux, uy = ex - tx, ey - ty
    n = math.hypot(ux, uy)
    ux, uy = ux / n, uy / n
    px, py = -uy, ux
    for d_ft in range(100, length_ft + 1, 100 if length_ft > 1400 else 200):
        d = d_ft * 0.3048
        cx, cy = ex + ux * d, ey + uy * d
        approach.append(f'<circle cx="{cx:.0f}" cy="{cy:.0f}" r="6" />')
        if d_ft == crossbar_ft:
            for k in range(-5, 6):
                if abs(k) >= 1:
                    approach.append(f'<circle cx="{cx + px * k * 4.5:.0f}" cy="{cy + py * k * 4.5:.0f}" r="5" />')
        if side_rows_ft and d_ft <= side_rows_ft:
            for s in (-1, 1):
                approach.append(f'<circle class="red" cx="{cx + px * s * 12:.0f}" cy="{cy + py * s * 12:.0f}" r="5" />')


if "01L/19R" in by_ref or any(r.startswith("01L") for r in by_ref):
    key = next(r for r in by_ref if r.startswith("01L") or r.startswith("1L"))
    a, b = ends(key)
    south, north = (a, b) if a[1] > b[1] else (b, a)
    approach_lights(south, north, 2400, 1000, 1000)  # 1L: ALSF-2
    approach_lights(north, south, 1400, 1000)        # 19R: MALSR
if any(r.startswith("07R") or r.startswith("7R") for r in by_ref):
    key = next(r for r in by_ref if r.startswith("07R") or r.startswith("7R"))
    a, b = ends(key)
    west, east = (a, b) if a[0] < b[0] else (b, a)
    approach_lights(west, east, 1400, 1000)          # 7R: MALSR
    approach_lights(east, west, 1400, 1000)          # 25L: MALSR

x0, y0, w, h = VIEW
svg = f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="{x0} {y0} {w} {h}" width="{w // 4}" height="{h // 4}" preserveAspectRatio="xMidYMid slice">
<title>General Mitchell International Airport (KMKE) at night. Map data © OpenStreetMap contributors</title>
<defs>
<filter id="glow" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="14" result="b" /><feMerge><feMergeNode in="b" /><feMergeNode in="b" /><feMergeNode in="SourceGraphic" /></feMerge></filter>
<radialGradient id="pool" cx="0.5" cy="0.5" r="0.5"><stop offset="0" stop-color="#ffcf6b" stop-opacity="0.16" /><stop offset="1" stop-color="#ffcf6b" stop-opacity="0" /></radialGradient>
<style>
.ground {{ fill: #0a0d11; }}
.road-minor {{ fill: none; stroke: #12161b; stroke-width: 7; stroke-linecap: round; stroke-linejoin: round; }}
.road-major {{ fill: none; stroke: #191e24; stroke-width: 13; stroke-linecap: round; stroke-linejoin: round; }}
.road-motorway {{ fill: none; stroke: #1f252c; stroke-width: 24; stroke-linecap: round; stroke-linejoin: round; }}
.building {{ fill: #11151a; }}
.apron {{ fill: #1f252c; }}
.taxiway {{ fill: none; stroke: #2a3139; stroke-width: 23; stroke-linecap: round; stroke-linejoin: round; }}
.taxi-line {{ fill: none; stroke: #e0ae2a; stroke-width: 2.5; opacity: 0.6; }}
.runway {{ fill: none; stroke: #313841; stroke-linecap: butt; }}
.airport-building {{ fill: #3a424c; stroke: #59626d; stroke-width: 3; }}
.edge {{ fill: none; stroke: #ffc65a; stroke-width: 11; stroke-linecap: round; stroke-dasharray: 0 61; }}
.centre {{ fill: none; stroke: #fff2cf; stroke-width: 6; stroke-linecap: round; stroke-dasharray: 0 15.2; opacity: 0.75; }}
.approach circle {{ fill: #fff3d2; }}
.approach circle.red {{ fill: #ff5a4a; }}
</style>
</defs>
<rect class="ground" x="{x0}" y="{y0}" width="{w}" height="{h}" />
<g>{''.join(f'<path class="road-minor" d="{d}" />' for d in layers['road-minor'])}</g>
<g>{''.join(f'<path class="road-major" d="{d}" />' for d in layers['road-major'])}</g>
<g>{''.join(f'<path class="road-motorway" d="{d}" />' for d in layers['road-motorway'])}</g>
<g>{''.join(f'<path class="building" d="{d}" />' for d in layers['building'])}</g>
<ellipse cx="-400" cy="-380" rx="1100" ry="850" fill="url(#pool)" />
<g>{''.join(f'<path class="apron" d="{d}" />' for d in layers['apron'])}</g>
<g>{''.join(f'<path class="taxiway" d="{d}" />' for d in layers['taxiway'])}</g>
<g>{''.join(runway_paths)}</g>
<g>{''.join(f'<path class="taxi-line" d="{d}" />' for d in layers['taxi-line'])}</g>
<g>{''.join(f'<path class="airport-building" d="{d}" />' for d in layers['airport-building'])}</g>
<g filter="url(#glow)">
<g>{''.join(f'<path class="edge" d="{d}" />' for d in lights_edge)}</g>
<g>{''.join(f'<path class="centre" d="{d}" />' for d in lights_centre)}</g>
<g class="approach">{''.join(approach)}</g>
</g>
</svg>
"""
OUT.parent.mkdir(parents=True, exist_ok=True)
OUT.write_text(svg, encoding="utf-8")
print(f"wrote {OUT} ({len(svg) // 1024} KB): {len(runways)} runway ways, {sum(len(v) for v in layers.values())} features, {len(approach)} approach lights")
