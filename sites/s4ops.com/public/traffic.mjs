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
