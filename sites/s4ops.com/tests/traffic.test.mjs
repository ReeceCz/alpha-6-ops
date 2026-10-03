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

test('narrow screens exclude tags above the copy block instead of a column', () => {
  const box = (x, y) => ({ x, y, w: 60, h: 30 });
  const c = [{ id: 'overCopy', box: box(100, 300), distance: 1 }, { id: 'belowCopy', box: box(100, 700), distance: 2 }];
  assert.deepEqual(placeTags(c, [], { maxTags: 8, minX: -Infinity, minY: 650 }).map(t => t.id), ['belowCopy']);
  assert.deepEqual(placeTags(c, [], { maxTags: 8, minX: 0 }).map(t => t.id), ['overCopy', 'belowCopy'], 'minY is optional');
});
