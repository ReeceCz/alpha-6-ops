# s4ops.com Landing Site Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A static `s4ops.com` landing site whose hero is a simulated, cursor-reactive live-traffic map in the app's charcoal theme.

**Architecture:** Plain static files served by Cloudflare static assets.
- `traffic.mjs` is a pure, dependency-free module covering projection, flight model and label placement. Node tests cover it.
- `hero.js` draws the canvas and wires the DOM using `traffic.mjs`.
- `index.html` and `styles.css` hold the page.
- The Great Lakes outlines come from a committed JSON generated once from Natural Earth.

**Tech Stack:** HTML, CSS, ES modules, Canvas 2D, `node --test` (Node 22+), Python 3 (one-off data script), Wrangler (`npx`, not a repo dependency), Playwright MCP for browser checks.

**Spec:** `docs/superpowers/specs/2026-10-02-s4ops-landing-design.md`

## Global Constraints

- No new npm or NuGet dependencies, no framework and no build step. Wrangler only through `npx`.
- Theme: dark only. Tokens: `#090C0F` bg, `#101418` / `#151A1F` / `#20272D` surfaces, `#293138` divider, `#F2F4F5` / `#BBC2C8` / `#8E99A2` text, `#F4CF22` yellow, `#86BDE8` information/flown.
- Fonts: `"Bahnschrift SemiCondensed"` first, then self-hosted Barlow Semi Condensed / Barlow / IBM Plex Mono (copied with OFL licences).
- No "RYNEL" anywhere. Accounts are invite-only. "Simulated data" is always visible in the hero legend.
- Links:
  - Sign in: `https://app.s4ops.com/auth/login`
  - Create account: `https://app.s4ops.com/auth/signup`
  - Download: `https://app.s4ops.com/Download`
  - Privacy: `https://app.s4ops.com/Privacy`
  - Terms: `https://app.s4ops.com/Terms`
- CSP allows only `'self'` for scripts, styles, fonts, images and connections. No inline `<script>` or `style=` attributes.
- Copy claims must match the portal's fact-checked `src/Alpha6Ops.Server/Pages/Home/HomeContent.cs`.
- Never run `wrangler deploy`. Deploying is Reece's step.

## Review Focus

- A window resized while the hero is animating: the canvas, projection and lens radius follow the new size with no blank band or stretched drawing.
- A short or very wide viewport (1280×640, 2560×1080): the headline, actions and stats strip never overlap one another.
- A touch-only device (no hover): the hero animates with the drifting lens, and the flight card never appears or sticks.
- `data/great-lakes.json` failing to load: the hero still renders the graticule, airports and traffic, without throwing.
- A tab left hidden for minutes, then shown: the aircraft continue smoothly with no jump. `advance` clamps each step to 0.1 s.

---

## File Structure

```
sites/s4ops.com/
  public/
    index.html            page markup (no inline script/style)
    styles.css            all page styles
    hero.js               canvas rendering + DOM wiring (imports traffic.mjs)
    traffic.mjs           pure simulation: projection, airports, flights, tag placement
    data/great-lakes.json generated lake outlines
    data/provenance.json  source + licence of the outlines
    fonts/                woff2 + OFL licences (copied from the portal)
    images/s4-tile.svg, images/s4-tile-512.png (copied from the portal)
    _headers              CSP + security + cache headers
  tools/build_lakes.py    regenerates data/great-lakes.json
  tests/traffic.test.mjs  node --test
  wrangler.jsonc          static assets config
  README.md               dev, test, deploy
```

---

### Task 1: Simulation core (`traffic.mjs`)

**Files:**
- Create: `sites/s4ops.com/public/traffic.mjs`
- Test: `sites/s4ops.com/tests/traffic.test.mjs`

**Interfaces:**
- Produces:
  - `AIRPORTS: Record<string,{lon,lat,weight,name}>`, `CORNERS: Record<'NW'|'NE'|'SE'|'SW',[lon,lat]>` and `TIME_SCALE = 14`.
  - `mulberry32(seed) -> () => number`, a seeded RNG for tests.
  - `createProjection({lon0,lat0,scale,cx,cy}) -> {project(lon,lat)->[x,y], unproject(x,y)->[lon,lat]}`.
  - `distanceNm([lon,lat],[lon,lat]) -> number`.
  - `makeFlight(rng, {anywhere:boolean}) -> Flight`, where `Flight = {from,to,path,segs,total,over,s4,cs,type,reg,cruise,done}`.
  - `flightState(f) -> {lon,lat,alt,gs,phase,toGo,heading,segment}`.
  - `advance(f, dtSeconds) -> boolean` (true once arrived; `dt` is clamped to 0.1 s).
  - `placeTags(candidates, blocked, {maxTags,minX}) -> candidate[]`, where `candidate = {id, box:{x,y,w,h}, distance}` and `blocked` is a box[].

- [ ] **Step 1: Write the failing tests**

```js
// sites/s4ops.com/tests/traffic.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { AIRPORTS, CORNERS, mulberry32, createProjection, distanceNm, makeFlight, flightState, advance, placeTags } from '../public/traffic.mjs';

const proj = createProjection({ lon0: -87.6, lat0: 42.6, scale: 6000, cx: 800, cy: 400 });

test('projection centre maps to the canvas centre and round-trips', () => {
  const [x, y] = proj.project(-87.6, 42.6);
  assert.ok(Math.abs(x - 800) < 1e-9 && Math.abs(y - 400) < 1e-9);
  for (const [lon, lat] of [[-87.9, 41.98], [-93.2, 44.9], [-83.3, 42.2]]) {
    const [lo, la] = proj.unproject(...proj.project(lon, lat));
    assert.ok(Math.abs(lo - lon) < 1e-6 && Math.abs(la - lat) < 1e-6, `${lon},${lat}`);
  }
  const [nx, ny] = proj.project(-87.6, 43.6); assert.ok(ny < 400 && Math.abs(nx - 800) < 1e-6, 'north is up');
});

test('distance is about 60 nm per degree of latitude', () => {
  assert.ok(Math.abs(distanceNm([-87, 42], [-87, 43]) - 60) < 0.01);
});

test('flights connect two different airports and their segments add up', () => {
  const rng = mulberry32(7);
  for (let i = 0; i < 400; i++) {
    const f = makeFlight(rng, { anywhere: false });
    assert.equal(f.done, 0);
    assert.ok(Math.abs(f.segs.reduce((a, b) => a + b, 0) - f.total) < 1e-9);
    if (f.over) continue;
    assert.notEqual(f.from, f.to);
    assert.deepEqual(f.path[0], [AIRPORTS[f.from].lon, AIRPORTS[f.from].lat]);
    assert.deepEqual(f.path.at(-1), [AIRPORTS[f.to].lon, AIRPORTS[f.to].lat]);
  }
});

test("O'Hare arrivals and departures pass a corner post", () => {
  const rng = mulberry32(11), corners = Object.values(CORNERS).map(c => c.join());
  let checked = 0;
  for (let i = 0; i < 600; i++) {
    const f = makeFlight(rng, { anywhere: false });
    if (f.over || (f.to !== 'KORD' && f.from !== 'KORD')) continue;
    assert.ok(f.path.some(p => corners.includes(p.join())), `${f.from}-${f.to}`); checked++;
  }
  assert.ok(checked > 20);
});

test('altitude and speed follow the phase', () => {
  const rng = mulberry32(3);
  for (let i = 0; i < 200; i++) {
    const f = makeFlight(rng, { anywhere: false });
    for (let k = 0; k <= 50; k++) {
      f.done = f.total * k / 50; const s = flightState(f);
      assert.ok(s.alt >= 0 && s.alt <= f.cruise * 100, 'altitude within 0..cruise');
      assert.ok(s.gs >= 150 && s.gs <= 470, 'ground speed plausible');
      if (f.over) assert.equal(s.phase, 'CRUISE');
    }
    if (!f.over) { f.done = 0; assert.equal(flightState(f).alt, 0); f.done = f.total; assert.equal(flightState(f).alt, 0); }
  }
});

test('advance moves forward, clamps long frames, and reports arrival', () => {
  const f = makeFlight(mulberry32(5), { anywhere: false });
  const before = f.done; advance(f, 0.05); const step = f.done - before; assert.ok(step > 0);
  const f2 = { ...f, done: before }; advance(f2, 600); assert.ok(f2.done - before <= step * 2 + 1e-9, 'a 10-minute gap moves no further than 0.1 s');
  f.done = f.total - 1e-6; assert.equal(advance(f, 0.1), true);
});

test('tags are nearest-first, never overlap, respect the limit and the copy column', () => {
  const box = (x, y) => ({ x, y, w: 60, h: 30 });
  const c = [
    { id: 'far', box: box(700, 100), distance: 300 },
    { id: 'near', box: box(500, 100), distance: 10 },
    { id: 'overlapsNear', box: box(530, 110), distance: 20 },
    { id: 'inCopy', box: box(100, 300), distance: 5 },
    { id: 'blocked', box: box(900, 500), distance: 30 },
  ];
  const out = placeTags(c, [box(905, 505)], { maxTags: 8, minX: 300 }).map(t => t.id);
  assert.deepEqual(out, ['near', 'far']);
  const many = Array.from({ length: 20 }, (_, i) => ({ id: i, box: box(400 + i * 70, 100), distance: i }));
  assert.equal(placeTags(many, [], { maxTags: 8, minX: 0 }).length, 8);
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `node --test sites/s4ops.com/tests`
Expected: FAIL (`Cannot find module .../public/traffic.mjs`).

- [ ] **Step 3: Implement `traffic.mjs`**

```js
// sites/s4ops.com/public/traffic.mjs
// Simulated traffic for the landing hero. Pure functions, no DOM: tested with node --test.
// Nothing here is real flight data. Airports and corner posts are approximate public coordinates.
export const D = Math.PI / 180;
export const TIME_SCALE = 14; // simulated seconds per real second: calm, readable motion

export const AIRPORTS = {
  KORD: { lon: -87.904, lat: 41.979, weight: 10, name: "Chicago O'Hare" },
  KMDW: { lon: -87.752, lat: 41.786, weight: 3, name: 'Chicago Midway' },
  KMKE: { lon: -87.897, lat: 42.947, weight: 3, name: 'Milwaukee' },
  KMSP: { lon: -93.222, lat: 44.882, weight: 4, name: 'Minneapolis' },
  KDTW: { lon: -83.353, lat: 42.212, weight: 4, name: 'Detroit' },
  KMSN: { lon: -89.337, lat: 43.140, weight: 1, name: 'Madison' },
  KGRB: { lon: -88.129, lat: 44.485, weight: 1, name: 'Green Bay' },
  KGRR: { lon: -85.522, lat: 42.881, weight: 1, name: 'Grand Rapids' },
  KSTL: { lon: -90.370, lat: 38.749, weight: 2, name: 'St. Louis' },
  KIND: { lon: -86.294, lat: 39.717, weight: 2, name: 'Indianapolis' },
  KDSM: { lon: -93.663, lat: 41.534, weight: 1, name: 'Des Moines' },
  KCLE: { lon: -81.850, lat: 41.411, weight: 2, name: 'Cleveland' },
  KCID: { lon: -91.711, lat: 41.885, weight: 1, name: 'Cedar Rapids' },
  KRFD: { lon: -89.097, lat: 42.195, weight: 1, name: 'Rockford' },
};
// O'Hare arrival corner posts (approximate), so traffic funnels in the way it really does.
export const CORNERS = { NW: [-88.70, 42.40], NE: [-87.30, 42.45], SE: [-87.25, 41.55], SW: [-88.60, 41.55] };

const OPERATORS = ['S4', 'S4', 'NWA', 'GLX', 'SKY', 'MXR', 'ZNA', 'OPR', 'LKS', 'PRV']; // fictional apart from S4
const S4_TYPES = [['Airbus A320neo', 'A20N'], ['Airbus A321neo', 'A21N'], ['Boeing 737-800', 'B738'], ['Airbus A350-900', 'A359']];
const OTHER_TYPES = [['Boeing 737-800', 'B738'], ['Airbus A320', 'A320'], ['Embraer E175', 'E75L'], ['Boeing 787-9', 'B789'], ['Bombardier CRJ-900', 'CRJ9'], ['Airbus A220-300', 'BCS3']];

export function mulberry32(seed) {
  let a = seed >>> 0;
  return () => { a = (a + 0x6D2B79F5) >>> 0; let t = a; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
}

// Orthographic projection, so the graticule curves gently like the app's globe.
export function createProjection({ lon0, lat0, scale, cx, cy }) {
  const l0 = lon0 * D, p0 = lat0 * D;
  return {
    project(lon, lat) {
      const l = lon * D - l0, p = lat * D;
      return [cx + scale * Math.cos(p) * Math.sin(l), cy - scale * (Math.cos(p0) * Math.sin(p) - Math.sin(p0) * Math.cos(p) * Math.cos(l))];
    },
    unproject(x, y) {
      const px = (x - cx) / scale, py = (cy - y) / scale, r = Math.hypot(px, py), c = Math.asin(Math.min(1, r));
      const lat = Math.asin(Math.cos(c) * Math.sin(p0) + (r ? py * Math.sin(c) * Math.cos(p0) / r : 0));
      const lon = l0 + Math.atan2(px * Math.sin(c), r * Math.cos(c) * Math.cos(p0) - py * Math.sin(c) * Math.sin(p0));
      return [lon / D, lat / D];
    },
  };
}

export function distanceNm(a, b) {
  const dLat = b[1] - a[1], dLon = (b[0] - a[0]) * Math.cos((a[1] + b[1]) / 2 * D);
  return Math.hypot(dLat, dLon) * 60;
}

const at = code => [AIRPORTS[code].lon, AIRPORTS[code].lat];
const choose = (rng, list) => list[Math.floor(rng() * list.length)];
function weightedAirport(rng) {
  const list = Object.entries(AIRPORTS), total = list.reduce((s, [, a]) => s + a.weight, 0);
  let r = rng() * total;
  for (const [code, a] of list) if ((r -= a.weight) < 0) return code;
  return 'KORD';
}
// The corner post on the side of O'Hare facing the other airport.
function cornerFacing(code) {
  const a = AIRPORTS[code];
  return CORNERS[(a.lat >= 42 ? 'N' : 'S') + (a.lon <= AIRPORTS.KORD.lon ? 'W' : 'E')];
}

export function makeFlight(rng, { anywhere }) {
  let from, to, path;
  const over = rng() < 0.14;
  if (over) { // high-level overflight crossing the region
    const y1 = 38.8 + rng() * 8, y2 = 38.8 + rng() * 8, east = rng() < 0.5;
    path = east ? [[-96, y1], [-79, y2]] : [[-79, y1], [-96, y2]]; from = east ? 'WEST' : 'EAST'; to = east ? 'EAST' : 'WEST';
  } else {
    from = weightedAirport(rng); do to = weightedAirport(rng); while (to === from);
    path = [at(from), at(to)];
    if (to === 'KORD') path.splice(1, 0, cornerFacing(from));
    if (from === 'KORD') path.splice(1, 0, cornerFacing(to));
  }
  const segs = []; let total = 0;
  for (let i = 1; i < path.length; i++) { const d = distanceNm(path[i - 1], path[i]); segs.push(d); total += d; }
  const op = choose(rng, OPERATORS), s4 = op === 'S4';
  const num = s4 ? 600 + Math.floor(rng() * 90) : 100 + Math.floor(rng() * 3800);
  const type = choose(rng, s4 ? S4_TYPES : OTHER_TYPES);
  const cruise = over ? 350 + Math.floor(rng() * 5) * 10 : Math.min(390, 160 + Math.round(total / 40) * 10 + Math.floor(rng() * 4) * 10);
  return { from, to, path, segs, total, over, s4, cs: op + num, type, reg: s4 ? `N${num}S4` : `N${100 + Math.floor(rng() * 899)}XY`,
    cruise, done: anywhere ? rng() * total * 0.92 : 0 };
}

export function flightState(f) {
  let d = Math.min(f.done, f.total), segment = 0;
  while (segment < f.segs.length - 1 && d > f.segs[segment]) { d -= f.segs[segment]; segment++; }
  const k = f.segs[segment] ? Math.min(1, d / f.segs[segment]) : 1, a = f.path[segment], b = f.path[segment + 1];
  const lon = a[0] + (b[0] - a[0]) * k, lat = a[1] + (b[1] - a[1]) * k;
  const toGo = Math.max(0, f.total - f.done), flown = Math.min(f.done, f.total);
  const alt = f.over ? f.cruise * 100 : Math.max(0, Math.min(f.cruise * 100, Math.min(flown, toGo) * 320)); // ~3:1 profile
  const gs = f.over ? 470 : Math.round(150 + Math.min(1, alt / 28000) * 310);
  const phase = f.over || alt >= f.cruise * 100 - 50 ? 'CRUISE' : flown < toGo ? 'CLIMB' : toGo < 18 ? 'APPROACH' : 'DESCENT';
  return { lon, lat, alt, gs, phase, toGo, segment, heading: Math.atan2(-(b[1] - a[1]), (b[0] - a[0]) * Math.cos(lat * D)) };
}

export function advance(f, dtSeconds) {
  const dt = Math.min(0.1, Math.max(0, dtSeconds)); // a hidden tab never makes aircraft jump
  f.done += flightState(f).gs * dt * TIME_SCALE / 3600;
  return f.done >= f.total;
}

const overlaps = (a, b) => a.x < b.x + b.w + 3 && b.x < a.x + a.w + 3 && a.y < b.y + b.h + 3 && b.y < a.y + a.h + 3;
// Radar-style declutter: nearest first, never overlapping, never in the headline column.
export function placeTags(candidates, blocked, { maxTags, minX }) {
  const placed = [...blocked], out = [];
  for (const c of [...candidates].sort((a, b) => a.distance - b.distance)) {
    if (out.length >= maxTags) break;
    if (c.box.x < minX || placed.some(p => overlaps(c.box, p))) continue;
    placed.push(c.box); out.push(c);
  }
  return out;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `node --test sites/s4ops.com/tests`
Expected: 7 tests pass, 0 fail.

- [ ] **Step 5: Commit**

```bash
git add sites/s4ops.com/public/traffic.mjs sites/s4ops.com/tests/traffic.test.mjs
git commit -m "feat(landing): simulated traffic model for the s4ops.com hero"
```

---

### Task 2: Great Lakes outlines from Natural Earth

**Files:**
- Create: `sites/s4ops.com/tools/build_lakes.py`
- Create (generated): `sites/s4ops.com/public/data/great-lakes.json`, `sites/s4ops.com/public/data/provenance.json`
- Modify: `sites/s4ops.com/tests/traffic.test.mjs` (append a data test)

**Interfaces:**
- Produces: `data/great-lakes.json` with the shape `{"lakes":[{"name":string,"rings":[[[lon,lat],...],...]}]}`, where each ring is closed and coordinates are rounded to 3 decimals.

- [ ] **Step 1: Write the failing data test** (append to `traffic.test.mjs`)

```js
import { readFileSync } from 'node:fs';
test('great-lakes.json holds the four lakes around the hero, inside the map window', () => {
  const data = JSON.parse(readFileSync(new URL('../public/data/great-lakes.json', import.meta.url)));
  const names = data.lakes.map(l => l.name);
  for (const n of ['Lake Superior', 'Lake Michigan', 'Lake Huron', 'Lake Erie']) assert.ok(names.includes(n), n);
  for (const lake of data.lakes) for (const ring of lake.rings) {
    assert.ok(ring.length >= 4);
    assert.deepEqual(ring[0], ring.at(-1), 'closed ring');
    for (const [lon, lat] of ring) assert.ok(lon > -98 && lon < -74 && lat > 36 && lat < 51);
  }
  const prov = JSON.parse(readFileSync(new URL('../public/data/provenance.json', import.meta.url)));
  assert.match(prov.licence, /public domain/i);
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `node --test sites/s4ops.com/tests`
Expected: FAIL (ENOENT `great-lakes.json`).

- [ ] **Step 3: Write the generator**

```python
# sites/s4ops.com/tools/build_lakes.py
"""Regenerate public/data/great-lakes.json from Natural Earth 1:50m lakes (public domain).
Run by hand when the outlines need changing; the output is committed. Not run at deploy."""
import json, pathlib, urllib.request, datetime

SOURCE = "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/v5.1.2/geojson/ne_50m_lakes.geojson"
BOX = (-97.5, 36.5, -74.5, 50.5)  # lon/lat window around the hero map
OUT = pathlib.Path(__file__).resolve().parent.parent / "public" / "data"

def inside(ring):
    return any(BOX[0] < x < BOX[2] and BOX[1] < y < BOX[3] for x, y in ring)

def main():
    with urllib.request.urlopen(SOURCE, timeout=60) as response:
        source = json.load(response)
    lakes = []
    for feature in source["features"]:
        geometry, name = feature["geometry"], feature["properties"].get("name") or ""
        polygons = geometry["coordinates"] if geometry["type"] == "MultiPolygon" else [geometry["coordinates"]]
        rings = []
        for polygon in polygons:
            outer = [[round(x, 3), round(y, 3)] for x, y in polygon[0]]
            if inside(outer) and len(outer) >= 4:
                if outer[0] != outer[-1]:
                    outer.append(outer[0])
                rings.append(outer)
        if rings:
            lakes.append({"name": name, "rings": rings})
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "great-lakes.json").write_text(json.dumps({"lakes": lakes}, separators=(",", ":")), encoding="utf-8")
    (OUT / "provenance.json").write_text(json.dumps({
        "file": "great-lakes.json", "source": "Natural Earth 1:50m lakes v5.1.2", "url": SOURCE,
        "licence": "Public domain (Natural Earth terms of use)", "window": BOX,
        "generated": datetime.date.today().isoformat(), "generator": "tools/build_lakes.py"}, indent=2) + "\n", encoding="utf-8")
    print(f"{len(lakes)} lakes:", ", ".join(l["name"] for l in lakes))

if __name__ == "__main__":
    main()
```

- [ ] **Step 4: Generate and run the tests**

Run: `python sites/s4ops.com/tools/build_lakes.py`. Expected: a line listing lakes, including Superior, Michigan, Huron and Erie.
Then run `node --test sites/s4ops.com/tests`. Expected: 8 tests pass. If a lake name differs in the source (for example a combined Michigan–Huron feature), adjust the test's expected names to the source's names and note it in the commit message. Do not hand-edit the JSON.
Check: `great-lakes.json` should be under 80 KB. If it's larger, round to 2 decimals in the script and regenerate.

- [ ] **Step 5: Commit**

```bash
git add sites/s4ops.com/tools/build_lakes.py sites/s4ops.com/public/data sites/s4ops.com/tests/traffic.test.mjs
git commit -m "feat(landing): Great Lakes outlines generated from Natural Earth"
```

---

### Task 3: Page, styles, assets, headers and hosting config

**Files:**
- Create: `sites/s4ops.com/public/index.html`, `sites/s4ops.com/public/styles.css`, `sites/s4ops.com/public/_headers`, `sites/s4ops.com/wrangler.jsonc`
- Copy: `src/Alpha6Ops.Server/wwwroot/fonts/{barlow-400,barlow-500,barlow-700,barlow-semi-condensed-600,barlow-semi-condensed-700,ibm-plex-mono-400,ibm-plex-mono-600}.woff2`, `barlow-OFL.txt` and `ibm-plex-mono-OFL.txt` → `public/fonts/`. Copy `wwwroot/images/brand/s4-tile.svg` and `s4-tile-512.png` → `public/images/`.

**Interfaces:**
- Produces, for Task 4: the element IDs `hero`, `heroCanvas`, `flightCard`, `kCs`, `kRoute`, `kType`, `kPhase`, `kAlt`, `kGs`, `kDist`, `kOut`, `kIn`, `kBar`, `kArrow`, `sTracked`, `sS4`, `sOrd`, `sUtc` and `readout`; the CSS classes `.card.on` and `.hero.no-canvas`; and `<div class="copy">`, whose right edge bounds the tag-free column.

- [ ] **Step 1: Copy the assets**

```bash
mkdir -p sites/s4ops.com/public/fonts sites/s4ops.com/public/images
for f in barlow-400 barlow-500 barlow-700 barlow-semi-condensed-600 barlow-semi-condensed-700 ibm-plex-mono-400 ibm-plex-mono-600; do cp src/Alpha6Ops.Server/wwwroot/fonts/$f.woff2 sites/s4ops.com/public/fonts/; done
cp src/Alpha6Ops.Server/wwwroot/fonts/*-OFL.txt sites/s4ops.com/public/fonts/
cp src/Alpha6Ops.Server/wwwroot/images/brand/s4-tile.svg src/Alpha6Ops.Server/wwwroot/images/brand/s4-tile-512.png sites/s4ops.com/public/images/
```

- [ ] **Step 2: Write `index.html`**

```html
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>S4 OPS · You fly the airplane. We'll run the airline.</title>
  <meta name="description" content="S4 OPS is a Windows desktop app for Microsoft Flight Simulator 2024 that times every flight from off block to block in, tracks it live, and keeps your pilot logbook." />
  <meta name="theme-color" content="#090C0F" />
  <meta property="og:title" content="S4 OPS · Airline operations for MSFS 2024" />
  <meta property="og:description" content="You fly the airplane. We'll run the airline." />
  <meta property="og:image" content="https://s4ops.com/images/s4-tile-512.png" />
  <link rel="icon" href="/images/s4-tile.svg" type="image/svg+xml" />
  <link rel="preload" href="/fonts/barlow-semi-condensed-700.woff2" as="font" type="font/woff2" crossorigin />
  <link rel="stylesheet" href="/styles.css" />
  <script type="module" src="/hero.js"></script>
</head>
<body>
  <a class="skip" href="#main">Skip to content</a>
  <header class="nav">
    <a class="brand" href="/" aria-label="Sierra 4 home"><img src="/images/s4-tile.svg" alt="" width="34" height="34" /><span>SIERRA 4<small>AIRLINE OPERATIONS</small></span></a>
    <nav aria-label="Main"><a href="#how">How it works</a><a href="#start">Download</a><a href="https://app.s4ops.com/Support">Help</a><a class="signin" href="https://app.s4ops.com/auth/login">Sign in</a></nav>
  </header>

  <main id="main">
    <section class="hero" id="hero" aria-labelledby="hero-title">
      <canvas id="heroCanvas" aria-hidden="true"></canvas>
      <div class="scrim" aria-hidden="true"></div>
      <div class="copy">
        <p class="eyebrow">Airline operations for MSFS 2024</p>
        <h1 id="hero-title">You fly the airplane.<br /><span>We'll run the airline.</span></h1>
        <p class="lead">S4 OPS times every flight from off block to block in, tracks it live on the map, and keeps your pilot logbook.</p>
        <div class="actions"><a class="btn primary" href="https://app.s4ops.com/auth/signup">Create your invited account <span aria-hidden="true">→</span></a><a class="btn secondary" href="https://app.s4ops.com/Download">Download for Windows</a></div>
        <p class="note">Invite-only while we test with a small group of pilots. <a href="https://app.s4ops.com/">Ask for an invitation</a> or <a href="https://app.s4ops.com/auth/login">sign in</a>.</p>
      </div>
      <dl class="stats" aria-hidden="true">
        <div><dt>Tracked</dt><dd id="sTracked">0</dd></div>
        <div class="y"><dt>Sierra 4 airborne</dt><dd id="sS4">0</dd></div>
        <div><dt>KORD arr / dep</dt><dd id="sOrd">0 / 0</dd></div>
        <div><dt>UTC</dt><dd id="sUtc">--:--Z</dd></div>
      </dl>
      <div class="card" id="flightCard" aria-hidden="true">
        <div class="card-head"><span class="cs" id="kCs"></span><span class="route" id="kRoute"></span></div>
        <div class="type" id="kType"></div>
        <dl>
          <div><dt>Current phase</dt><dd class="y" id="kPhase"></dd></div><div><dt>Altitude</dt><dd id="kAlt"></dd></div>
          <div><dt>Ground speed</dt><dd id="kGs"></dd></div><div><dt>Distance remaining</dt><dd id="kDist"></dd></div>
          <div><dt>Actual out</dt><dd id="kOut"></dd></div><div><dt>Est. in</dt><dd id="kIn"></dd></div>
        </dl>
        <div class="prog"><b id="kBar"></b><i id="kArrow"></i></div>
      </div>
      <p class="legend">Yellow: Sierra 4 fleet · Grey: other traffic · Cyan: flown · Simulated data</p>
      <p class="readout" id="readout" aria-hidden="true"></p>
    </section>

    <section class="band phases" aria-labelledby="phases-title">
      <div class="wrap">
        <p class="index"><span>01</span> Flight phases</p>
        <h2 id="phases-title">Every phase recorded from the simulator.</h2>
        <p class="sub">S4 OPS records the four times airlines report for every flight, then works out block and flight time from them. A paused or slewed sim never triggers a phase change.</p>
        <ol class="oooi" aria-label="Sample flight S4601, Milwaukee to Chicago O'Hare">
          <li><b>OUT</b><time>1802Z</time><h3>Off block</h3><p>Parking brake released and the aircraft moving on the ground.</p></li>
          <li><b>OFF</b><time>1814Z</time><h3>Takeoff</h3><p>The wheels leave the runway.</p></li>
          <li><b>ON</b><time>1901Z</time><h3>Landing</h3><p>The wheels are back on the ground.</p></li>
          <li><b>IN</b><time>1909Z</time><h3>Block in</h3><p>Parked at the gate with the brake set. Fenix A320-family aircraft confirm it from the engine shutdown; on other aircraft you confirm it with one click.</p></li>
        </ol>
        <p class="fine">Sample flight S4601, KMKE to KORD: block time 1 h 07 min, flight time 47 min.</p>
      </div>
    </section>

    <section class="band how" id="how" aria-labelledby="how-title">
      <div class="wrap">
        <p class="index"><span>02</span> How it works</p>
        <h2 id="how-title">Fly normally. S4 OPS keeps the record.</h2>
        <ol class="steps">
          <li><span>WPT 01</span><h3>Install and sign in</h3><p>Install S4 OPS for Windows and sign in once with your account. It remembers you.</p></li>
          <li><span>WPT 02</span><h3>Plan the flight</h3><p>Import your SimBrief plan in Dispatch, or enter the flight yourself, then accept the flight.</p></li>
          <li><span>WPT 03</span><h3>Connect and fly</h3><p>Connect to Microsoft Flight Simulator 2024 at the gate and fly the way you always do. OUT, OFF and ON are recorded as they happen.</p></li>
          <li><span>WPT 04</span><h3>Block in and file</h3><p>Confirm block in at the gate, review the flight and submit your PIREP. Block and flight time are worked out for you.</p></li>
        </ol>
      </div>
    </section>

    <section class="band tracks" aria-labelledby="tracks-title">
      <div class="wrap">
        <p class="index"><span>03</span> What it tracks</p>
        <h2 id="tracks-title">The flight you actually flew.</h2>
        <ul class="features">
          <li><b>TRK</b><h3>Live tracking</h3><p>Phase, route progress and a predicted gate arrival as you fly, with a live map of your route.</p></li>
          <li><b>CRZ</b><h3>Cruise and descent</h3><p>Your actual cruise level, any step climbs and the top of descent, taken from what the aircraft did rather than the flight plan.</p></li>
          <li><b>ETA</b><h3>Gate arrival</h3><p>On time within five minutes of the schedule, otherwise minutes early or late.</p></li>
          <li><b>REC</b><h3>Recovery</h3><p>If the sim or the app restarts mid-flight, S4 OPS picks the same flight back up instead of starting a new one.</p></li>
          <li><b>LOG</b><h3>Logbook</h3><p>Every tracked flight in the app's pilot logbook with its four times, route and aircraft.</p></li>
          <li><b>DSP</b><h3>Dispatch</h3><p>Import a SimBrief plan, review the release and accept it. Your SimBrief username follows your account.</p></li>
        </ul>
      </div>
    </section>

    <section class="band audiences" aria-labelledby="aud-title">
      <div class="wrap">
        <p class="index"><span>04</span> Pilots and airlines</p>
        <h2 id="aud-title" class="visually-hidden">For pilots and for airlines</h2>
        <div class="split">
          <article><p class="for">For pilots</p><h3>Your flight. Your logbook.</h3><p>Everything you need on the flight deck, in your own space, whether or not you fly for an airline. Bring an existing logbook into your account from a CSV file.</p></article>
          <article><p class="for">For airlines</p><h3>Run the airline together.</h3><p>Found an airline or join one with an invitation. Pilots, dispatchers and administrators work from the same schedule, fleet and crew list, and every administrative change asks for a fresh security check.</p></article>
        </div>
      </div>
    </section>

    <section class="band start" id="start" aria-labelledby="start-title">
      <div class="wrap start-grid">
        <div>
          <p class="index"><span>05</span> Get started</p>
          <h2 id="start-title">Ready for your next rotation?</h2>
          <p class="sub">S4 OPS runs on the PC with Microsoft Flight Simulator 2024: 64-bit Windows 10 (1809 or later) or Windows 11. This test release is not code-signed yet, so Windows will ask you to confirm before it runs; the download page shows the checksum to check first.</p>
          <div class="actions"><a class="btn primary" href="https://app.s4ops.com/Download">Download for Windows <span aria-hidden="true">→</span></a><a class="btn secondary" href="https://app.s4ops.com/auth/signup">Create your invited account</a></div>
        </div>
        <ul class="facts"><li><b>Windows desktop app</b>Installs for your Windows user, no administrator rights needed.</li><li><b>Works offline</b>Sign in once, then fly without a connection.</li><li><b>Your data</b>Download everything we hold, or delete your account, from your profile at any time.</li></ul>
      </div>
    </section>
  </main>

  <footer class="foot">
    <div class="wrap foot-grid">
      <p><b>S4 OPS</b> by Sierra 4. Airline operations for Microsoft Flight Simulator 2024.</p>
      <nav aria-label="Legal"><a href="https://app.s4ops.com/Privacy">Privacy</a><a href="https://app.s4ops.com/Terms">Terms</a><a href="https://app.s4ops.com/Support">Help</a></nav>
      <p class="fine">Not affiliated with Microsoft or Asobo Studio. Map traffic is simulated.</p>
    </div>
  </footer>
</body>
</html>
```

- [ ] **Step 3: Write `styles.css`**

```css
@font-face { font-family: "Barlow Semi Condensed"; src: url(/fonts/barlow-semi-condensed-600.woff2) format("woff2"); font-weight: 600; font-display: swap; }
@font-face { font-family: "Barlow Semi Condensed"; src: url(/fonts/barlow-semi-condensed-700.woff2) format("woff2"); font-weight: 700; font-display: swap; }
@font-face { font-family: Barlow; src: url(/fonts/barlow-400.woff2) format("woff2"); font-weight: 400; font-display: swap; }
@font-face { font-family: Barlow; src: url(/fonts/barlow-500.woff2) format("woff2"); font-weight: 500; font-display: swap; }
@font-face { font-family: Barlow; src: url(/fonts/barlow-700.woff2) format("woff2"); font-weight: 700; font-display: swap; }
@font-face { font-family: "IBM Plex Mono"; src: url(/fonts/ibm-plex-mono-400.woff2) format("woff2"); font-weight: 400; font-display: swap; }
@font-face { font-family: "IBM Plex Mono"; src: url(/fonts/ibm-plex-mono-600.woff2) format("woff2"); font-weight: 600; font-display: swap; }

:root {
  --bg: #090c0f; --s1: #101418; --s2: #151a1f; --s3: #20272d; --line: #293138; --line-2: #3a434b;
  --text: #f2f4f5; --text-2: #bbc2c8; --muted: #8e99a2; --yellow: #f4cf22; --info: #86bde8;
  --display: "Bahnschrift SemiCondensed", "Barlow Semi Condensed", "Segoe UI", system-ui, sans-serif;
  --body: Barlow, "Segoe UI", system-ui, sans-serif; --mono: "IBM Plex Mono", ui-monospace, Consolas, monospace;
  color-scheme: dark;
}
* { box-sizing: border-box; }
html { background: var(--bg); }
body { margin: 0; background: var(--bg); color: var(--text); font: 400 17px/1.55 var(--body); -webkit-font-smoothing: antialiased; }
a { color: inherit; }
h1, h2, h3 { font-family: var(--display); font-weight: 700; margin: 0; }
.visually-hidden { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
.skip { position: absolute; left: 12px; top: -60px; z-index: 20; background: var(--yellow); color: var(--bg); padding: 10px 14px; border-radius: 4px; font-weight: 700; }
.skip:focus { top: 12px; }
:focus-visible { outline: 2px solid var(--yellow); outline-offset: 3px; }

/* Navigation over the hero */
.nav { position: absolute; z-index: 10; left: 0; right: 0; top: 0; height: 72px; display: flex; align-items: center; padding: 0 clamp(16px, 3vw, 40px);
  border-bottom: 1px solid rgba(41, 49, 56, .7); background: rgba(9, 12, 15, .55); backdrop-filter: blur(6px); }
.brand { display: flex; align-items: center; gap: 12px; text-decoration: none; font: 600 20px/1 var(--display); letter-spacing: .06em; }
.brand small { display: block; margin-top: 4px; font: 600 9px/1 var(--body); letter-spacing: .14em; color: var(--muted); }
.nav nav { margin-left: auto; display: flex; gap: clamp(14px, 2vw, 26px); align-items: center; font-size: 15px; }
.nav nav a { color: var(--text-2); text-decoration: none; }
.nav nav a:hover { color: var(--text); }
.nav .signin { border: 1px solid var(--yellow); color: var(--yellow) !important; padding: 9px 16px; border-radius: 4px; font-weight: 600; }

/* Hero */
.hero { position: relative; min-height: max(640px, 100vh); overflow: hidden; background: var(--bg); }
.hero canvas { position: absolute; inset: 0; width: 100%; height: 100%; display: block; cursor: crosshair; }
.hero.no-canvas { background: radial-gradient(ellipse at 66% 50%, #171c21 0%, #11151a 60%, #0b0e11 100%); }
.scrim { position: absolute; inset: 0; pointer-events: none;
  background: linear-gradient(90deg, rgba(9,12,15,.94) 0%, rgba(9,12,15,.82) 30%, rgba(9,12,15,.25) 52%, rgba(9,12,15,0) 66%),
              linear-gradient(180deg, rgba(9,12,15,.85) 0%, rgba(9,12,15,0) 14%, rgba(9,12,15,0) 80%, var(--bg) 100%); }
.copy { position: relative; z-index: 2; max-width: 620px; padding: clamp(120px, 20vh, 190px) clamp(20px, 5vw, 64px) 160px; pointer-events: none; }
.copy a { pointer-events: auto; }
.eyebrow { margin: 0; display: flex; align-items: center; gap: 14px; font: 600 12px/1 var(--mono); letter-spacing: .2em; text-transform: uppercase; color: var(--yellow); }
.eyebrow::before { content: ""; width: 34px; height: 2px; background: var(--yellow); }
.hero h1 { margin-top: 22px; font-size: clamp(48px, 6.4vw, 92px); line-height: .92; letter-spacing: -.01em; }
.hero h1 span { color: var(--yellow); }
.lead { margin: 24px 0 0; max-width: 500px; font-size: clamp(17px, 1.4vw, 19px); color: var(--text-2); }
.actions { margin-top: 32px; display: flex; flex-wrap: wrap; gap: 12px; }
.btn { display: inline-block; padding: 15px 22px; border-radius: 4px; font-weight: 700; font-size: 16px; text-decoration: none; border: 1px solid var(--yellow); }
.btn.primary { background: var(--yellow); color: var(--bg); }
.btn.primary:hover { background: #ffe066; }
.btn.secondary { color: var(--text); border-color: var(--line-2); background: rgba(16, 20, 24, .7); }
.btn.secondary:hover { border-color: var(--text-2); }
.note { margin: 16px 0 0; font-size: 14px; color: var(--muted); }
.note a { color: var(--text-2); }
.stats { position: absolute; z-index: 2; right: 24px; bottom: 48px; margin: 0; display: flex; gap: 6px; pointer-events: none; }
.stats div { background: rgba(16, 20, 24, .86); border: 1px solid var(--line); border-radius: 4px; padding: 8px 12px 9px; min-width: 88px; }
.stats dt { font: 600 10px/1 var(--body); letter-spacing: .08em; color: var(--muted); text-transform: uppercase; }
.stats dd { margin: 6px 0 0; font: 600 22px/1 var(--display); }
.stats .y dd { color: var(--yellow); }
.card { position: absolute; z-index: 3; right: 24px; top: 96px; width: 300px; background: rgba(16, 20, 24, .95); border: 1px solid var(--line); border-left: 2px solid var(--yellow);
  border-radius: 4px; padding: 14px 16px 16px; color: var(--text-2); opacity: 0; transform: translateX(8px); transition: opacity .2s, transform .2s; pointer-events: none; }
.card.on { opacity: 1; transform: none; }
.card.other { border-left-color: var(--info); }
.card-head { display: flex; justify-content: space-between; align-items: baseline; gap: 12px; }
.card .cs { font: 600 26px/1 var(--display); color: var(--yellow); }
.card.other .cs { color: var(--text); }
.card .route { font: 600 16px/1 var(--display); color: var(--text); }
.card .type { margin-top: 6px; font-size: 12px; color: var(--muted); }
.card dl { margin: 14px 0 0; display: grid; grid-template-columns: 1fr 1fr; gap: 10px 14px; }
.card dt { font: 600 10px/1 var(--body); letter-spacing: .08em; color: var(--muted); text-transform: uppercase; }
.card dd { margin: 4px 0 0; font: 600 16px/1 var(--display); color: var(--text); }
.card dd.y { color: var(--yellow); }
.prog { position: relative; margin-top: 16px; height: 3px; background: var(--line-2); }
.prog b { position: absolute; left: 0; top: 0; bottom: 0; width: 0; background: var(--yellow); }
.prog i { position: absolute; top: -6px; left: 0; border-left: 10px solid var(--yellow); border-top: 7px solid transparent; border-bottom: 7px solid transparent; margin-left: -5px; }
.legend { position: absolute; z-index: 2; left: clamp(20px, 5vw, 64px); bottom: 16px; margin: 0; font: 600 10px/1 var(--body); letter-spacing: .08em; color: var(--muted); text-transform: uppercase; }
.readout { position: absolute; z-index: 2; right: 24px; bottom: 18px; margin: 0; font: 11px/1 var(--mono); color: var(--muted); }

/* Sections */
.wrap { max-width: 1240px; margin: 0 auto; padding: 0 clamp(20px, 5vw, 64px); }
.band { padding: clamp(72px, 10vw, 120px) 0; border-top: 1px solid var(--line); }
.band:nth-of-type(odd) { background: var(--s1); }
.index { margin: 0 0 14px; font: 600 12px/1 var(--mono); letter-spacing: .16em; text-transform: uppercase; color: var(--muted); }
.index span { color: var(--yellow); margin-right: 10px; }
.band h2 { font-size: clamp(34px, 4vw, 52px); line-height: 1; max-width: 18ch; }
.sub { margin: 18px 0 0; max-width: 62ch; color: var(--text-2); }
.fine { margin: 22px 0 0; font-size: 13px; color: var(--muted); }
.oooi, .steps, .features { list-style: none; margin: 48px 0 0; padding: 0; display: grid; gap: 1px; background: var(--line); border: 1px solid var(--line); }
.oooi { grid-template-columns: repeat(4, 1fr); }
.steps { grid-template-columns: repeat(4, 1fr); }
.features { grid-template-columns: repeat(3, 1fr); }
.oooi li, .steps li, .features li { background: var(--bg); padding: 26px 24px 28px; }
.oooi b { font: 700 30px/1 var(--display); color: var(--yellow); }
.oooi time { display: block; margin-top: 6px; font: 600 13px/1 var(--mono); color: var(--muted); }
.oooi h3, .steps h3, .features h3 { margin-top: 18px; font-size: 22px; }
.oooi p, .steps p, .features p { margin: 8px 0 0; color: var(--text-2); font-size: 15px; }
.steps span, .features b { font: 600 12px/1 var(--mono); letter-spacing: .14em; color: var(--yellow); }
.split { display: grid; grid-template-columns: 1fr 1fr; gap: 1px; background: var(--line); border: 1px solid var(--line); }
.split article { background: var(--bg); padding: clamp(28px, 4vw, 48px); }
.split .for { margin: 0; font: 600 12px/1 var(--mono); letter-spacing: .16em; text-transform: uppercase; color: var(--yellow); }
.split h3 { margin-top: 16px; font-size: clamp(28px, 3vw, 38px); }
.split p { color: var(--text-2); }
.start-grid { display: grid; grid-template-columns: 1.4fr 1fr; gap: 64px; align-items: start; }
.facts { list-style: none; margin: 0; padding: 0; border-left: 2px solid var(--yellow); }
.facts li { padding: 0 0 22px 22px; color: var(--text-2); font-size: 15px; }
.facts b { display: block; color: var(--text); font: 600 18px/1.2 var(--display); margin-bottom: 4px; }
.foot { border-top: 1px solid var(--line); padding: 40px 0; color: var(--text-2); font-size: 14px; }
.foot-grid { display: grid; grid-template-columns: 1fr auto; gap: 8px 32px; align-items: center; }
.foot p { margin: 0; }
.foot nav { display: flex; gap: 22px; }
.foot nav a { color: var(--text-2); }
.foot .fine { grid-column: 1 / -1; margin-top: 6px; }

/* Smaller screens */
@media (max-width: 1100px) { .stats { display: none; } .card { display: none; } .features { grid-template-columns: repeat(2, 1fr); } }
@media (max-width: 900px) {
  .scrim { background: linear-gradient(180deg, rgba(9,12,15,.6) 0%, rgba(9,12,15,.78) 45%, rgba(9,12,15,.9) 75%, var(--bg) 100%); }
  .oooi, .steps { grid-template-columns: repeat(2, 1fr); } .start-grid, .split { grid-template-columns: 1fr; } .start-grid { gap: 40px; }
}
@media (max-width: 640px) {
  .nav nav a:not(.signin) { display: none; }
  .oooi, .steps, .features { grid-template-columns: 1fr; }
  .readout { display: none; }
  .legend { right: 20px; line-height: 1.5; }
  .foot-grid { grid-template-columns: 1fr; }
}
@media (prefers-reduced-motion: reduce) { .card { transition: none; } }
```

- [ ] **Step 4: Write `_headers` and `wrangler.jsonc`**

```
/*
  Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'; object-src 'none'
  X-Content-Type-Options: nosniff
  Referrer-Policy: strict-origin-when-cross-origin
  Permissions-Policy: camera=(), microphone=(), geolocation=(), interest-cohort=()
  Cross-Origin-Opener-Policy: same-origin
/fonts/*
  Cache-Control: public, max-age=31536000, immutable
/data/*
  Cache-Control: public, max-age=86400
```

```jsonc
// sites/s4ops.com/wrangler.jsonc — static assets only, no Worker code. Deploy is Reece's step: npx wrangler deploy
{
  "name": "s4ops-landing",
  "compatibility_date": "2026-09-01",
  "assets": { "directory": "./public" }
}
```

- [ ] **Step 5: Serve and check the static page (hero canvas still blank)**

Run in the background: `cd sites/s4ops.com && npx wrangler dev --port 8787`. If wrangler can't be fetched, use `python -m http.server 8787 -d sites/s4ops.com/public` and note that the headers aren't exercised.
Then use the Playwright MCP at 1440×900 and 390×844 on `http://localhost:8787/`:

```js
async (page) => {
  const errs = []; page.on('console', m => m.type() === 'error' && errs.push(m.text()));
  const out = {};
  for (const [w, h] of [[1440, 900], [390, 844]]) {
    await page.setViewportSize({ width: w, height: h });
    const r = await page.goto('http://localhost:8787/', { waitUntil: 'networkidle' });
    out[w] = await page.evaluate(() => ({
      overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      inlineStyles: document.querySelectorAll('[style]').length,
      inlineScripts: [...document.scripts].filter(s => !s.src).length,
      offsiteLinks: [...document.querySelectorAll('a[href^="http"]')].map(a => new URL(a.href).host).filter(h => h !== 'app.s4ops.com'),
      rynel: /rynel/i.test(document.documentElement.outerHTML),
    }));
    out[w].csp = r.headers()['content-security-policy'] ?? null;
  }
  return { errs, out };
}
```

Expected:
- `overflow` 0, `inlineStyles` 0, `inlineScripts` 0, `offsiteLinks` [] and `rynel` false at both widths.
- `csp` is present under wrangler.
- Console errors are limited to a missing `hero.js` (Task 4 adds it).

- [ ] **Step 6: Commit**

```bash
git add sites/s4ops.com/public sites/s4ops.com/wrangler.jsonc
git commit -m "feat(landing): s4ops.com page, charcoal styles, assets and security headers"
```

---

### Task 4: Hero renderer (`hero.js`)

**Files:**
- Create: `sites/s4ops.com/public/hero.js`

**Interfaces:**
- Consumes: everything Task 1 produces; `data/great-lakes.json` (Task 2); the element IDs and classes from Task 3.

- [ ] **Step 1: Write `hero.js`**

```js
// sites/s4ops.com/public/hero.js
// Draws the simulated traffic map behind the hero and wires the lens, tags and flight card.
import { AIRPORTS, createProjection, makeFlight, flightState, advance, placeTags } from './traffic.mjs';

const hero = document.getElementById('hero'), canvas = document.getElementById('heroCanvas');
const ctx = canvas?.getContext?.('2d');
if (!hero || !ctx) { hero?.classList.add('no-canvas'); }
else start();

function start() {
  const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const canHover = matchMedia('(hover: hover) and (pointer: fine)').matches;
  const copy = hero.querySelector('.copy'), $ = id => document.getElementById(id), card = $('flightCard');
  const rng = Math.random;
  let W = 0, H = 0, proj = null, lakes = [], mx = null, my = null, last = performance.now(), visible = true, running = false;
  const flights = [];

  fetch('/data/great-lakes.json').then(r => r.ok ? r.json() : { lakes: [] }).then(d => { lakes = d.lakes ?? []; if (!running) draw(0); }).catch(() => {});

  function size() {
    const r = hero.getBoundingClientRect(), d = Math.min(2, devicePixelRatio || 1);
    W = r.width; H = r.height; canvas.width = Math.round(W * d); canvas.height = Math.round(H * d); ctx.setTransform(d, 0, 0, d, 0, 0);
    const narrow = W < 900;
    proj = createProjection({ lon0: -87.6, lat0: 42.6, scale: Math.max(W, H * 1.6) * (narrow ? 3.2 : 4.1), cx: W * (narrow ? 0.5 : 0.66), cy: H * (narrow ? 0.62 : 0.5) });
    const target = narrow ? 46 : 74;
    while (flights.length < target) flights.push(makeFlight(rng, { anywhere: true }));
    flights.length = target;
  }
  size();
  new ResizeObserver(() => { size(); if (!running) draw(0); }).observe(hero);

  if (canHover) {
    hero.addEventListener('pointermove', e => { const r = hero.getBoundingClientRect(); mx = e.clientX - r.left; my = e.clientY - r.top; });
    hero.addEventListener('pointerleave', () => { mx = my = null; });
  }

  const P = (lon, lat) => proj.project(lon, lat);
  function ring(points) { ctx.beginPath(); points.forEach(([lon, lat], i) => { const [x, y] = P(lon, lat); i ? ctx.lineTo(x, y) : ctx.moveTo(x, y); }); }
  function icon(x, y, ang, s, color) {
    ctx.save(); ctx.translate(x, y); ctx.rotate(ang); ctx.scale(s, s); ctx.fillStyle = color; ctx.beginPath();
    ctx.moveTo(7, 0); ctx.lineTo(1.5, -1.2); ctx.lineTo(-1, -6.5); ctx.lineTo(-2.6, -6.5); ctx.lineTo(-1.6, -1.2); ctx.lineTo(-5, -1.1); ctx.lineTo(-6.6, -3.2); ctx.lineTo(-7.6, -3.2); ctx.lineTo(-6.8, 0);
    ctx.lineTo(-7.6, 3.2); ctx.lineTo(-6.6, 3.2); ctx.lineTo(-5, 1.1); ctx.lineTo(-1.6, 1.2); ctx.lineTo(-2.6, 6.5); ctx.lineTo(-1, 6.5); ctx.lineTo(1.5, 1.2); ctx.closePath(); ctx.fill(); ctx.restore();
  }
  const FONT = '600 11px "Bahnschrift SemiCondensed", "Barlow Semi Condensed", "Segoe UI", sans-serif';
  function measure(x, y, lines) { ctx.font = FONT; const w = Math.max(...lines.map(l => ctx.measureText(l).width)) + 10, h = lines.length * 13 + 5; return { x, y: y - h + 4, w, h }; }
  function tag(box, lines, colors, alpha) {
    ctx.font = FONT; ctx.globalAlpha = alpha; ctx.fillStyle = 'rgba(16,20,24,.88)'; ctx.fillRect(box.x, box.y, box.w, box.h);
    ctx.strokeStyle = '#293138'; ctx.lineWidth = 1; ctx.strokeRect(box.x + .5, box.y + .5, box.w - 1, box.h - 1);
    lines.forEach((l, i) => { ctx.fillStyle = colors[i]; ctx.fillText(l, box.x + 5, box.y + 12 + i * 13); }); ctx.globalAlpha = 1;
  }
  const altText = a => a >= 18000 ? 'FL' + Math.round(a / 100) : Math.round(a / 100) * 100 + ' FT';
  const hhmm = d => String(d.getUTCHours()).padStart(2, '0') + ':' + String(d.getUTCMinutes()).padStart(2, '0') + 'Z';

  function draw(now) {
    const dt = (now - last) / 1000; last = now;
    const t = now / 1000, idle = mx === null;
    const cx = idle ? W * (W < 900 ? 0.5 + 0.18 * Math.cos(t * 0.17) : 0.7 + 0.12 * Math.cos(t * 0.17)) : mx;
    const cy = idle ? H * (W < 900 ? 0.68 + 0.1 * Math.sin(t * 0.23) : 0.52 + 0.16 * Math.sin(t * 0.23)) : my;
    const R = Math.min(W, H) * 0.34, near = (x, y) => Math.max(0, 1 - Math.hypot(x - cx, y - cy) / R);
    const copyRight = W < 900 ? W + 1 : copy.getBoundingClientRect().right - hero.getBoundingClientRect().left;

    // Charcoal land, near-black water, faint neutral coastlines.
    const bg = ctx.createRadialGradient(W * 0.66, H * 0.5, 0, W * 0.66, H * 0.5, Math.max(W, H) * 0.75);
    bg.addColorStop(0, '#171c21'); bg.addColorStop(0.6, '#11151a'); bg.addColorStop(1, '#0b0e11'); ctx.fillStyle = bg; ctx.fillRect(0, 0, W, H);
    for (const lake of lakes) for (const r of lake.rings) { ring(r); ctx.closePath(); ctx.fillStyle = '#07090b'; ctx.fill(); ctx.strokeStyle = 'rgba(187,194,200,.30)'; ctx.lineWidth = 1; ctx.stroke(); }
    ctx.lineWidth = 1; ctx.strokeStyle = 'rgba(242,244,245,.045)';
    for (let lon = -100; lon <= -74; lon += 2) { ctx.beginPath(); for (let lat = 34; lat <= 52; lat += 0.5) { const [x, y] = P(lon, lat); lat === 34 ? ctx.moveTo(x, y) : ctx.lineTo(x, y); } ctx.stroke(); }
    for (let lat = 34; lat <= 52; lat += 2) { ctx.beginPath(); for (let lon = -100; lon <= -74; lon += 0.5) { const [x, y] = P(lon, lat); lon === -100 ? ctx.moveTo(x, y) : ctx.lineTo(x, y); } ctx.stroke(); }

    const glow = ctx.createRadialGradient(cx, cy, 0, cx, cy, R); glow.addColorStop(0, 'rgba(244,207,34,.06)'); glow.addColorStop(1, 'rgba(244,207,34,0)');
    ctx.fillStyle = glow; ctx.fillRect(0, 0, W, H); ctx.strokeStyle = 'rgba(244,207,34,.12)'; ctx.beginPath(); ctx.arc(cx, cy, R, 0, Math.PI * 2); ctx.stroke();

    // Airports: dark dot with a light ring, like the app; boxed code inside the lens.
    const blocked = [];
    for (const [code, a] of Object.entries(AIRPORTS)) {
      const [x, y] = P(a.lon, a.lat), k = near(x, y), hub = a.weight >= 3, pinned = code === 'KORD' || code === 'KMKE';
      ctx.fillStyle = '#101418'; ctx.strokeStyle = hub ? 'rgba(242,244,245,.75)' : 'rgba(187,194,200,.45)'; ctx.lineWidth = 1.5;
      ctx.beginPath(); ctx.arc(x, y, hub ? 4 : 3, 0, Math.PI * 2); ctx.fill(); ctx.stroke();
      if ((k > 0.08 || pinned) && x + 7 > copyRight) { const b = measure(x + 7, y - 4, [code]); blocked.push(b); tag(b, [code], ['#f4cf22'], Math.max(pinned ? 0.7 : 0, k)); }
    }

    // Traffic
    let hover = null, best = 22, s4Air = 0, ordArr = 0, ordDep = 0; const candidates = [];
    for (let i = 0; i < flights.length; i++) {
      let f = flights[i];
      if (!reduce && dt > 0 && advance(f, dt)) { f = flights[i] = makeFlight(rng, { anywhere: false }); }
      const s = flightState(f), [x, y] = P(s.lon, s.lat); f.x = x; f.y = y; f.s = s;
      if (f.s4 && s.alt > 0) s4Air++; if (f.to === 'KORD') ordArr++; if (f.from === 'KORD') ordDep++;
      f.trail ??= []; f.trailT = (f.trailT ?? 0) + Math.max(0, Math.min(0.1, dt));
      if (f.trailT > 0.5) { f.trailT = 0; f.trail.push([s.lon, s.lat]); if (f.trail.length > 40) f.trail.shift(); }
      const vis = 0.26 + 0.74 * near(x, y), col = f.s4 ? '244,207,34' : '160,172,182';
      for (let j = 1; j < f.trail.length; j++) { const [ax, ay] = P(...f.trail[j - 1]), [bx, by] = P(...f.trail[j]);
        ctx.strokeStyle = `rgba(${col},${vis * 0.28 * j / f.trail.length})`; ctx.lineWidth = 1.2; ctx.beginPath(); ctx.moveTo(ax, ay); ctx.lineTo(bx, by); ctx.stroke(); }
      icon(x, y, s.heading, f.s4 ? 1.1 : 0.9, `rgba(${col},${Math.min(1, vis + (f.s4 ? 0.2 : 0))})`);
      if (canHover && mx !== null && x > copyRight) { const d = Math.hypot(x - mx, y - my); if (d < best) { best = d; hover = f; } }
      const k = near(x, y);
      if (k > 0.5) { const lines = [f.cs, `${altText(s.alt)} · ${s.gs}`]; candidates.push({ id: i, f, k, lines, box: measure(x + 10, y - 2, lines), distance: Math.hypot(x - cx, y - cy) }); }
    }
    for (const c of placeTags(candidates.filter(c => c.f !== hover), blocked, { maxTags: 8, minX: copyRight }))
      tag(c.box, c.lines, [c.f.s4 ? '#f4cf22' : '#f2f4f5', '#8e99a2'], Math.min(1, (c.k - 0.5) * 3));

    if (hover) { // flown in cyan, remaining plan in yellow: the app's convention
      const s = hover.s; ctx.lineWidth = 2; ctx.strokeStyle = '#86bde8'; ring(hover.trail.concat([[s.lon, s.lat]])); ctx.stroke();
      ctx.strokeStyle = '#f4cf22'; ctx.setLineDash([6, 5]); ctx.beginPath(); ctx.moveTo(hover.x, hover.y);
      for (let j = s.segment + 1; j < hover.path.length; j++) { const [x, y] = P(...hover.path[j]); ctx.lineTo(x, y); } ctx.stroke(); ctx.setLineDash([]);
      ctx.lineWidth = 1.5; ctx.beginPath(); ctx.arc(hover.x, hover.y, 12, 0, Math.PI * 2); ctx.stroke();
      if (!hover.over) { for (const [code, color] of [[hover.from, '#6fd59a'], [hover.to, '#f4cf22']]) { const a = AIRPORTS[code], [x, y] = P(a.lon, a.lat); tag(measure(x + 7, y - 4, [code]), [code], [color], 1); } }
    }
    showCard(hover);
    $('sTracked').textContent = flights.length; $('sS4').textContent = s4Air; $('sOrd').textContent = `${ordArr} / ${ordDep}`; $('sUtc').textContent = hhmm(new Date());
    if (mx !== null) { const [lo, la] = proj.unproject(mx, my); $('readout').textContent = `${Math.abs(la).toFixed(2)}°${la >= 0 ? 'N' : 'S'}  ${Math.abs(lo).toFixed(2)}°${lo >= 0 ? 'E' : 'W'}`; }
    else $('readout').textContent = '';
  }

  function showCard(f) {
    card.classList.toggle('on', !!f); if (!f) return; const s = f.s;
    card.classList.toggle('other', !f.s4);
    $('kCs').textContent = f.cs; $('kRoute').textContent = f.over ? 'OVERFLIGHT' : `${f.from} → ${f.to}`; $('kType').textContent = `${f.type[0]} · ${f.reg}`;
    $('kPhase').textContent = s.phase; $('kAlt').textContent = altText(s.alt); $('kGs').textContent = `${s.gs} KT`; $('kDist').textContent = `${Math.round(s.toGo)} NM`;
    const now = Date.now();
    $('kOut').textContent = f.over ? '—' : hhmm(new Date(now - (f.done / 380 * 60 + 12) * 60000));
    $('kIn').textContent = f.over ? '—' : hhmm(new Date(now + (s.toGo / Math.max(s.gs, 200) * 60 + 6) * 60000));
    const p = Math.round(f.done / f.total * 100); $('kBar').style.width = p + '%'; $('kArrow').style.left = p + '%';
  }

  function loop(now) { if (!visible || document.hidden) { running = false; return; } draw(now); requestAnimationFrame(loop); }
  function resume() { if (!running && visible && !document.hidden && !reduce) { running = true; last = performance.now(); requestAnimationFrame(loop); } }
  new IntersectionObserver(([e]) => { visible = e.isIntersecting; resume(); }).observe(hero);
  document.addEventListener('visibilitychange', resume);
  draw(last); // a still frame for reduced motion, and the first frame for everyone else
  resume();
}
```

- [ ] **Step 2: Unit tests still pass**

Run: `node --test sites/s4ops.com/tests`. Expected: 8 pass.

- [ ] **Step 3: Browser checks (Review Focus)**

With the server from Task 3 running, use the Playwright MCP:

```js
async (page) => {
  const errs = []; page.on('pageerror', e => errs.push(e.message)); page.on('console', m => m.type() === 'error' && errs.push(m.text()));
  const res = {};
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('http://localhost:8787/', { waitUntil: 'networkidle' }); await page.waitForTimeout(2500);
  const box = await page.locator('#hero').boundingBox(); let on = false;
  for (let gx = .55; gx < .85 && !on; gx += .015) for (let gy = .3; gy < .8 && !on; gy += .03) {
    await page.mouse.move(box.x + box.width * gx, box.y + box.height * gy); await page.waitForTimeout(15);
    on = await page.locator('#flightCard').evaluate(e => e.classList.contains('on')); }
  res.hoverCard = on;
  await page.screenshot({ path: 'work/ui-check/landing/hero-1440.png' });
  await page.setViewportSize({ width: 1100, height: 700 }); await page.waitForTimeout(400);
  res.resized = await page.evaluate(() => { const c = document.getElementById('heroCanvas'); return Math.abs(c.width / devicePixelRatio - c.clientWidth) <= 1 || c.width === Math.round(c.clientWidth * Math.min(2, devicePixelRatio)); });
  for (const [w, h] of [[1280, 640], [2560, 1080], [390, 844]]) {
    await page.setViewportSize({ width: w, height: h }); await page.goto('http://localhost:8787/', { waitUntil: 'networkidle' }); await page.waitForTimeout(800);
    res[`${w}x${h}`] = await page.evaluate(() => {
      const r = s => document.querySelector(s)?.getBoundingClientRect(), a = r('.actions'), st = r('.stats');
      const overlap = st && st.width > 0 && a && !(a.right < st.left || st.right < a.left || a.bottom < st.top || st.bottom < a.top);
      return { overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth, actionsOverlapStats: !!overlap };
    });
    await page.screenshot({ path: `work/ui-check/landing/hero-${w}x${h}.png` });
  }
  await page.emulateMedia({ reducedMotion: 'reduce' }); await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('http://localhost:8787/', { waitUntil: 'networkidle' });
  const a1 = await page.evaluate(() => document.getElementById('heroCanvas').toDataURL().length); await page.waitForTimeout(1000);
  const a2 = await page.evaluate(() => document.getElementById('heroCanvas').toDataURL().length);
  res.reducedMotionStill = a1 === a2;
  await page.emulateMedia({ reducedMotion: null });
  await page.route('**/data/great-lakes.json', r => r.fulfill({ status: 404, body: '' }));
  await page.goto('http://localhost:8787/', { waitUntil: 'networkidle' }); await page.waitForTimeout(800);
  res.lakesMissingStillDraws = await page.evaluate(() => document.getElementById('heroCanvas').toDataURL().length > 5000);
  await page.unroute('**/data/great-lakes.json');
  return { errs, res };
}
```

Then a touch check in a fresh context. Use `browser_run_code_unsafe`, creating `browser.newContext({ hasTouch: true, isMobile: true, viewport: { width: 390, height: 844 } })`, load the page, tap the hero, wait 1 s, and confirm `#flightCard` does not have the class `on`.

Expected:
- `errs` is empty.
- `hoverCard` is true and `resized` is true.
- Every viewport has `overflow` 0 and `actionsOverlapStats` false.
- `reducedMotionStill` is true and `lakesMissingStillDraws` is true.
- In the touch check, the card is never `on`.

Look at the screenshots for: a readable headline over the scrim, no tags in the headline column, and the legend and note not crowding.

- [ ] **Step 4: Commit**

```bash
git add sites/s4ops.com/public/hero.js
git commit -m "feat(landing): live traffic-map hero with lens, tags and flight card"
```

---

### Task 5: README and handoff

**Files:**
- Create: `sites/s4ops.com/README.md`
- Modify: `docs/s4-migration/PROGRESS.md` (Next steps)

- [ ] **Step 1: Write the README**

````markdown
# s4ops.com landing site

Static site, served by Cloudflare static assets. The hero map is **simulated**: no real flight data is used.

## Develop
```
cd sites/s4ops.com
npx wrangler dev --port 8787        # serves public/ with _headers applied
node --test tests                   # simulation tests (Node 22+)
```

## Map outlines
`public/data/great-lakes.json` is generated from Natural Earth 1:50m lakes (public domain):
`python tools/build_lakes.py`. Commit the output; do not hand-edit it.

## Deploy (Reece only)
1. Cloudflare dashboard: the `s4ops.com` zone must be active.
2. `cd sites/s4ops.com && npx wrangler login && npx wrangler deploy`
3. In Workers & Pages → s4ops-landing → Settings → Domains & Routes, add the custom domains `s4ops.com` and `www.s4ops.com`.
4. Check `https://s4ops.com/` loads and that the response has the `Content-Security-Policy` header.

All account links go to `https://app.s4ops.com`. The download version and checksum live only on the portal's /Download page.
````

- [ ] **Step 2: Add PROGRESS.md next-step lines**

Under "## Next steps" add:

```markdown
- [x] s4ops.com landing site built in `sites/s4ops.com` (simulated traffic-map hero, charcoal theme). Spec
      `docs/superpowers/specs/2026-10-02-s4ops-landing-design.md`.
- [ ] Reece: deploy s4ops.com per `sites/s4ops.com/README.md` (needs the Cloudflare go-ahead).
- [ ] Simplify the portal home to a short script-free entrance linking to s4ops.com.
```

- [ ] **Step 3: Final verification**

Run: `node --test sites/s4ops.com/tests`. Expected: 8 pass.
Re-run the Task 4 browser check once more on the final tree. Expected: as in Task 4.

- [ ] **Step 4: Commit**

```bash
git add sites/s4ops.com/README.md docs/s4-migration/PROGRESS.md
git commit -m "docs(landing): s4ops.com README and handoff"
```
