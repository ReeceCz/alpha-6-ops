# s4ops.com landing site — design

Status: draft for Reece's review, 2026-10-02. Branch `merge/dan-7.26b`.

## Purpose

`s4ops.com` becomes the front door for S4 OPS. It tells the story, carries the interactive hero, and sends people to the
account portal (`app.s4ops.com`) or the download. The first audience is a small group of invited test pilots, plus anyone
who looks S4 OPS up. Success: within a few seconds a visitor reads "serious, beautiful aviation software", and finds
either the download or how to get an invitation.

Decided with Reece:

- `s4ops.com` is the front door. The portal stays script-free and becomes a short sign-in and account entry (separate follow-up).
- The hero is a simulated live-traffic map in the "lens" style (v4 mockup in `.superpowers/brainstorm/`), in the app's charcoal tokens.
- Accounts are invite-only. "Powered by RYNEL" stays off until the name is cleared.
- Hosting stays free: Cloudflare static assets on the `s4ops.com` zone. Installers are served from R2.

## What the page contains

Top to bottom. All copy follows the fact-checked claims already in the portal (`Pages/Home/HomeContent.cs`).

1. **Hero:** full viewport height (minimum 640px), with the navigation overlaid on top. The navigation has the S4 tile,
   "SIERRA 4", Levels, Download, Help and Sign in. The headline, lead, two actions ("Create your invited account",
   "Download for Windows"), the invite-only note, the stats strip, the legend and the cursor readout match the v4
   mockup. The note and the legend no longer crowd each other.
2. **Flight phases:** OUT, OFF, ON, IN on the S4601 sample flight, as on the portal today. The block-in wording is honest
   (automatic on the Fenix A320 family, one click on other aircraft).
3. **How it works:** the four steps (install and sign in, plan the flight, connect and fly, block in and file).
4. **What it tracks:** live tracking, cruise and descent, predicted gate arrival, recovery, logbook, dispatch.
5. **Pilots and airlines:** the two audiences, with roles, and no shared-dispatch claim.
6. **Get started:** requirements in short, the unsigned-installer note, and a link to `app.s4ops.com/Download`. That page
   stays the single source of truth for the version, download link and SHA-256.
7. **Footer:** S4 OPS by Sierra 4, Privacy and Terms (linked to the portal), and "Not affiliated with Microsoft or Asobo Studio".

Links: Sign in goes to `https://app.s4ops.com/auth/login`. Create account goes to `https://app.s4ops.com/auth/signup`.
Download goes to `https://app.s4ops.com/Download`. The request-invitation link points at the portal home
(`https://app.s4ops.com/`), which carries the invitation request to the contact address.

**Theme:** dark only (charcoal). Tokens come from `SierraDesignTokens.xaml`: background `#090C0F`, surfaces
`#101418`/`#151A1F`/`#20272D`, divider `#293138`, text `#F2F4F5`/`#BBC2C8`/`#8E99A2`, yellow `#F4CF22` for brand
and selection, information `#86BDE8` for flown tracks. Typography uses Bahnschrift SemiCondensed where installed.
Otherwise it falls back to self-hosted Barlow Semi Condensed (headings and numbers), Barlow (body) and IBM Plex Mono
(eyebrows and readouts), copied from the portal's `wwwroot/fonts` together with their OFL licences.

## The hero simulation

- **Map:** orthographic projection centred near Lake Michigan, so the graticule curves like the app's globe. Water is
  near-black, land charcoal, and coastlines a faint neutral line.
- **Lake outlines:** generated once from Natural Earth 1:50m lakes (public domain) by a small script that writes a
  compact committed JSON with a `provenance.json`, the same practice as the desktop's map assets. Not hand-drawn.
- **Traffic:** about 74 simulated flights; about 46 on small screens.
  - O'Hare is the hub, with arrivals through four corner posts and departures climbing out. Midway, Milwaukee and
    regional airports are weighted below it, and about 14% are high-altitude overflights.
  - Altitude and ground speed follow the phase. Motion runs 14× real time, so it's calm.
  - About one flight in five is Sierra 4 (yellow); the rest are fictional operators in grey.
- **Lens:** a radius of about a third of the hero's shorter side. Inside it, airport tags and up to eight flight tags
  appear, nearest first and never overlapping. No tags are drawn in the headline column.
- **Hover:** the nearest aircraft within 22px shows its flown track (cyan), its planned route (yellow, dashed), origin
  and destination tags, and the flight card.
- **Honesty:** "Simulated data" is always shown in the legend. Callsigns and operators are fictional, apart from S4.
- **Behaviour:**
  - Without a hover-capable pointer (touch), the lens drifts slowly on its own and there is no flight card.
  - With `prefers-reduced-motion`, it renders a single still frame.
  - The animation pauses when the hero is off-screen or the tab is hidden.
  - Device pixel ratio is capped at 2. If the canvas fails, the page falls back to a static CSS background.

## Structure and hosting

```
sites/s4ops.com/
  public/            index.html, styles.css, hero.js (UI wiring), traffic.mjs (pure simulation + projection),
                     data/great-lakes.json, data/provenance.json, fonts/, favicon + og image, _headers
  tools/build_lakes.py   regenerates data/great-lakes.json from Natural Earth (run by hand, not at deploy)
  tests/traffic.test.mjs node --test, no dependencies
  wrangler.jsonc     Workers static assets, no Worker code; assets directory public/
  README.md          dev, test and deploy steps
```

- **No framework and no build step.** The repo gets no new npm dependencies. `npx wrangler dev` is used locally, and
  deploy is a step Reece runs (`npx wrangler deploy`) once he has the go-ahead. Brand images are copied from the portal's
  `wwwroot/images/brand` (compact lockups only).
- **`_headers`:** a strict CSP (`default-src 'self'`; `script-src 'self'`; no inline script; `connect-src 'self'`),
  plus `X-Content-Type-Options`, `Referrer-Policy` and `Permissions-Policy`. Long cache lifetimes for fonts and data.

## Testing

- `node --test sites/s4ops.com/tests` covers:
  - the projection round-trip;
  - flights staying on their path;
  - O'Hare arrivals passing a corner post;
  - altitude and speed being monotonic by phase;
  - tag placement never overlapping and never entering the headline column.
- **Playwright at 1440×900 and 390×844:**
  - no console errors and no horizontal overflow;
  - the hover card appears over an aircraft;
  - reduced motion renders a still frame;
  - links point at the portal;
  - the hero headline keeps at least 4.5:1 contrast over the scrim.
- Served locally with `npx wrangler dev`, so `_headers` and the CSP are exercised before deploy.

## Out of scope (follow-ups)

- Simplifying the portal home into a short script-free entrance that links to `s4ops.com`.
- DNS, R2 bucket and Cloudflare dashboard work. Reece does this with written steps.
- A light theme, and real traffic data. Neither is planned.
