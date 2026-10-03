// Draws the simulated traffic map behind the hero and wires the lens, tags and flight card.
import { AIRPORTS, createProjection, makeFlight, flightState, advance, placeTags } from './traffic.mjs';

const hero = document.getElementById('hero'), canvas = document.getElementById('heroCanvas');
const ctx = canvas?.getContext?.('2d');
if (!hero || !ctx) { hero?.classList.add('no-canvas'); }
else start();

function start() {
  const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const canHover = matchMedia('(hover: hover) and (pointer: fine)').matches;
  const copy = hero.querySelector('.copy'), note = hero.querySelector('.note'), $ = id => document.getElementById(id), card = $('flightCard');
  const rng = Math.random;
  let W = 0, H = 0, K = 1, FONT = '', proj = null, lakes = [], mx = null, my = null, last = performance.now(), visible = true, running = false;
  const flights = [];

  fetch('/data/great-lakes.json').then(r => r.ok ? r.json() : { lakes: [] }).then(d => { lakes = d.lakes ?? []; if (!running) draw(0); }).catch(() => {});

  function size() {
    const r = hero.getBoundingClientRect(), d = Math.min(2, devicePixelRatio || 1);
    // K follows the page's fluid base size, so labels and icons grow with the rest of the page on large screens.
    K = (parseFloat(getComputedStyle(document.documentElement).fontSize) || 16) / 16;
    FONT = `600 ${11 * K}px "Bahnschrift SemiCondensed", "Barlow Semi Condensed", "Segoe UI", sans-serif`;
    W = r.width; H = r.height; canvas.width = Math.round(W * d); canvas.height = Math.round(H * d); ctx.setTransform(d, 0, 0, d, 0, 0);
    const narrow = W < 900;
    proj = createProjection({ lon0: -87.6, lat0: 42.6, scale: Math.max(W, H * 1.6) * (narrow ? 3.2 : 4.1), cx: W * (narrow ? 0.5 : 0.66), cy: H * (narrow ? 0.62 : 0.5) });
    const target = narrow ? 46 : 74;
    while (flights.length < target) flights.push(makeFlight(rng, { anywhere: true }));
    flights.length = target;
  }
  size();
  // Setting canvas.width clears the bitmap; redraw before paint so a live resize never shows a blank hero.
  new ResizeObserver(() => { size(); draw(performance.now()); }).observe(hero);

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
  function measure(x, y, lines) { ctx.font = FONT; const w = Math.max(...lines.map(l => ctx.measureText(l).width)) + 10 * K, h = (lines.length * 13 + 5) * K; return { x, y: y - h + 4 * K, w, h }; }
  function tag(box, lines, colors, alpha) {
    ctx.font = FONT; ctx.globalAlpha = alpha; ctx.fillStyle = 'rgba(16,20,24,.88)'; ctx.fillRect(box.x, box.y, box.w, box.h);
    ctx.strokeStyle = '#293138'; ctx.lineWidth = 1; ctx.strokeRect(box.x + .5, box.y + .5, box.w - 1, box.h - 1);
    lines.forEach((l, i) => { ctx.fillStyle = colors[i]; ctx.fillText(l, box.x + 5 * K, box.y + (12 + i * 13) * K); }); ctx.globalAlpha = 1;
  }
  const altText = a => a >= 18000 ? 'FL' + Math.round(a / 100) : Math.round(a / 100) * 100 + ' FT';
  const hhmm = d => String(d.getUTCHours()).padStart(2, '0') + ':' + String(d.getUTCMinutes()).padStart(2, '0') + 'Z';

  function draw(now) {
    const dt = (now - last) / 1000; last = now;
    const t = now / 1000, idle = mx === null, narrow = W < 900, hr = hero.getBoundingClientRect();
    // No tags over the copy: left of its right edge on wide screens, above its last line on narrow ones.
    const copyRight = narrow ? -Infinity : copy.getBoundingClientRect().right - hr.left;
    const copyBottom = narrow ? note.getBoundingClientRect().bottom - hr.top + 8 * K : -Infinity;
    const inCopy = b => b.x < copyRight || b.y < copyBottom;
    const freeMid = narrow ? (Math.min(copyBottom, H) + H) / 2 / H : 0.52;
    const cx = idle ? W * (narrow ? 0.5 + 0.18 * Math.cos(t * 0.17) : 0.7 + 0.12 * Math.cos(t * 0.17)) : mx;
    const cy = idle ? H * (narrow ? freeMid + 0.06 * Math.sin(t * 0.23) : 0.52 + 0.16 * Math.sin(t * 0.23)) : my;
    const R = Math.min(W, H) * 0.34, near = (x, y) => Math.max(0, 1 - Math.hypot(x - cx, y - cy) / R);

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
      ctx.beginPath(); ctx.arc(x, y, (hub ? 4 : 3) * K, 0, Math.PI * 2); ctx.fill(); ctx.stroke();
      const b = measure(x + 7 * K, y - 4 * K, [code]);
      if ((k > 0.08 || pinned) && !inCopy(b)) { blocked.push(b); tag(b, [code], ['#f4cf22'], Math.max(pinned ? 0.7 : 0, k)); }
    }

    // Traffic
    let hover = null, best = 22 * K, s4Air = 0, ordArr = 0, ordDep = 0; const candidates = [];
    for (let i = 0; i < flights.length; i++) {
      let f = flights[i];
      if (!reduce && dt > 0 && advance(f, dt)) { f = flights[i] = makeFlight(rng, { anywhere: false }); }
      const s = flightState(f), [x, y] = P(s.lon, s.lat); f.x = x; f.y = y; f.s = s;
      if (f.s4 && s.alt > 0) s4Air++; if (f.to === 'KORD') ordArr++; if (f.from === 'KORD') ordDep++;
      f.trail ??= []; f.trailT = (f.trailT ?? 0) + Math.max(0, Math.min(0.1, dt));
      if (f.trailT > 0.5) { f.trailT = 0; f.trail.push([s.lon, s.lat]); if (f.trail.length > 40) f.trail.shift(); }
      const vis = 0.26 + 0.74 * near(x, y), col = f.s4 ? '244,207,34' : '160,172,182';
      for (let j = 1; j < f.trail.length; j++) { const [ax, ay] = P(...f.trail[j - 1]), [bx, by] = P(...f.trail[j]);
        ctx.strokeStyle = `rgba(${col},${vis * 0.28 * j / f.trail.length})`; ctx.lineWidth = 1.2 * K; ctx.beginPath(); ctx.moveTo(ax, ay); ctx.lineTo(bx, by); ctx.stroke(); }
      icon(x, y, s.heading, (f.s4 ? 1.1 : 0.9) * K, `rgba(${col},${Math.min(1, vis + (f.s4 ? 0.2 : 0))})`);
      if (canHover && mx !== null && x >= copyRight && y >= copyBottom) { const d = Math.hypot(x - mx, y - my); if (d < best) { best = d; hover = f; } }
      const k = near(x, y);
      if (k > 0.5) { const lines = [f.cs, `${altText(s.alt)} · ${s.gs}`]; candidates.push({ id: i, f, k, lines, box: measure(x + 10 * K, y - 2 * K, lines), distance: Math.hypot(x - cx, y - cy) }); }
    }
    const shown = placeTags(candidates.filter(c => c.f !== hover), blocked, { maxTags: 8, minX: copyRight, minY: copyBottom });
    for (const c of shown) tag(c.box, c.lines, [c.f.s4 ? '#f4cf22' : '#f2f4f5', '#8e99a2'], Math.min(1, (c.k - 0.5) * 3));
    if (hero.dataset.tags !== String(shown.length)) hero.dataset.tags = shown.length; // observable for layout checks

    if (hover) { // flown in cyan, remaining plan in yellow: the app's convention
      const s = hover.s; ctx.lineWidth = 2 * K; ctx.strokeStyle = '#86bde8'; ring(hover.trail.concat([[s.lon, s.lat]])); ctx.stroke();
      ctx.strokeStyle = '#f4cf22'; ctx.setLineDash([6, 5]); ctx.beginPath(); ctx.moveTo(hover.x, hover.y);
      for (let j = s.segment + 1; j < hover.path.length; j++) { const [x, y] = P(...hover.path[j]); ctx.lineTo(x, y); } ctx.stroke(); ctx.setLineDash([]);
      ctx.lineWidth = 1.5 * K; ctx.beginPath(); ctx.arc(hover.x, hover.y, 12 * K, 0, Math.PI * 2); ctx.stroke();
      if (!hover.over) { for (const [code, color] of [[hover.from, '#6fd59a'], [hover.to, '#f4cf22']]) { const a = AIRPORTS[code], [x, y] = P(a.lon, a.lat); tag(measure(x + 7 * K, y - 4 * K, [code]), [code], [color], 1); } }
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
