# Product direction: competing with vAMSYS, AeroNexus and smartCARS

Written 17 September 2026 on the `accounts` branch. A working document for Reece and Dan: what the
competition sells, where Alpha 6 OPS already stands, which ideas are worth building, how the web should
look and behave, and how to capture data properly. Every idea below is a proposal; Dan owns the
flight-sim calls, so nothing here is committed until he has looked at it.

## 1. What we already have

- **Desktop (WPF, MSFS 2024, Windows):** telemetry replay now, SimConnect live in testing; phase
  engine (pushback, taxi, takeoff, climb, cruise, descent, approach, landing, block-in) derived from the
  sim, not self-reported; aircraft rotations; personal logbook in a local SQLite flight-history database
  (`flight` + `flight_event`); dispatch with SimBrief import; weather briefings; flight tracking; a debrief;
  fleet database and aircraft catalogue; Simple / Advanced / OCC presentation modes; offline session.
- **Accounts:** Auth0 sign-in (email, passkey, Microsoft, Google), real step-up MFA for administration,
  one account with many airline memberships, roles (pilot, dispatcher, administrator, owner), invitation
  codes shown once, ownership transfer, audit log, synced pilot profile, personal and airline levels.
- **Web portal:** landing, levels, account, profile, level, airline administration (roster, invites,
  roles, activity log), download.

That is already a credible ACARS-plus-accounts core. What we do not have yet is everything a virtual
airline *runs on* day to day: schedules, bookings, PIREP review, scoring, ranks, awards, events, a live
map, statistics, and a pilot-facing website per airline.

## 2. The competition, read honestly

### vAMSYS (vamsys.co.uk) — the incumbent to beat

Web platform plus the Pegasus ACARS client (Windows/macOS/Linux; MSFS, X-Plane, P3D, FSX).

- Flight planning: one-click OFP, map-based booking with route filters and weather layers, VATSIM/IVAO
  prefile buttons, fleet-specific SimBrief profiles, cargo and passenger loads.
- Live tracking: fleet map, Navigraph airport layouts, automatic add-on recognition, phase detection,
  pilot privacy controls.
- Flight analytics: altitude/speed profiles, glide-slope and stabilised-approach checks, cross-track
  error, graduated scoring (landing, fuel, weight, takeoff), AutoReject for hard landings, overspeed,
  stalls, fuel violations, crashes; bounce detection; planned-vs-actual fuel; block/flight/taxi/pause time.
- Gamification: community goals, team challenges, custom ranks, badges, events, multi-leg tours, roster
  schedules, focus airports, leaderboards, stacked points.
- Administration: PIREP review with charts, per-registration fleet, routes with validity dates, pilot
  filtering and transfers, holiday workflows, scoring groups per fleet, alerts and NOTAMs, claims for
  unbooked flights, CSV/Excel/JSON import/export, PFPX.
- Integrations: Discord bot with rank roles, webhooks, OAuth2 API with scopes, Navigraph charts,
  white-label branding, drag-and-drop dashboard widgets, email templates.

Its weakness is the one we can exploit: it is a **website with a thin tracker**. Everything a pilot or
dispatcher does happens in a browser tab; the desktop app exists to stream positions.

### AeroNexus (aeronexus.app) — is this "VA Nexus"?

I could not find a product called "VA Nexus"; the closest match is **AeroNexus**, an MSFS 2024 / X-Plane
platform: "silent" zero-config ACARS with offline SQLite buffering, an 85k-airport database (heliports,
seaplane bases, oil rigs), helicopter physics and Vortex Ring State monitoring, a living economy (dynamic
fuel prices, hub demand), fleet wear, P&L dashboard, insurance marketplace, pilot applications, and an
enterprise add-on with a branded subdomain, manager portal, public API and alliances. Pricing: free solo
pilot, VA Startup $4.99/mo (5 pilots), Enterprise $14.99/mo (500 pilots), $199 lifetime.
**Please confirm the URL you mean** — if it is a different product I will re-read it.

Its angle is *economy and management sim*; it is not ops-realism.

### smartCARS (TFDi Design)

A pilot ACARS client sold to airlines that run phpVMS or similar, not a full platform: flight tracking
and PIREP filing, landing rate, live map, flight bidding from the web, chat, flight playback, and a web
pilot centre in the newer version. It is the thing many small VAs bolt onto a self-hosted site.

### Where nobody is strong: operations

All three are **pilot-centric**. None of them gives an airline an actual **operations control centre**:
rotations, turnarounds, delays propagating down a line of flying, dispatch releases, crew on duty right
now, gate and stand usage. Alpha 6 OPS already has the phase engine, rotations and an OCC mode. That is
the identity to build on: *the desktop where an airline is actually run*, with the website as the shop
window and the admin console.

## 3. Ideas, ranked by whether they earn their place

Each line says what it is and why it exists. "Now" means it closes a gap a first visitor will notice;
"Next" makes us better than the incumbent; "Later" is differentiation once the basics hold.

### Pilot experience

| When | Idea | Why |
| --- | --- | --- |
| Now | **Automatic PIREP** at block-in: times, fuel, landing rate, route flown, phases, score, filed to the airline without typing | Table stakes; every competitor has it |
| Now | **Live map** (WebView2 + Leaflet, already decided) showing your flight and your airline's fleet | The first thing anyone screenshots |
| Now | **Landing analysis**: vertical speed at touchdown, g-load, bounce detection, centreline and touchdown-zone distance, flare time | The single most shared stat in the hobby |
| Next | **Flight replay & debrief** with profile charts (altitude, speed, VS, cross-track), stabilised-approach gate, planned-vs-actual fuel | vAMSYS has it; we already have the timeline and debrief foundations |
| Next | **Bidding / booking** from the airline's schedule, with SimBrief OFP generated and the plan loaded straight into Dispatch | Removes the web tab entirely |
| Next | **Career**: hours, ranks, type ratings by aircraft family, badges, tours and events | Retention; VAs live on this |
| Next | **Personal statistics**: aircraft flown, favourite routes, landing trend, on-time performance, night/IFR hours | Uses the data capture below |
| Later | **Checkride / training mode**: a dispatcher or instructor watches a live flight and signs off | Nobody has it natively |
| Later | **Network awareness**: VATSIM/IVAO online status, prefile, controller in range | Cheap wins later |

### Airline operations (our edge)

| When | Idea | Why |
| --- | --- | --- |
| Now | **Schedules & routes**: flight numbers, days of week, validity, fleet restrictions, hubs; CSV import | Without a schedule there is nothing to bid on |
| Now | **Fleet by registration**: type, livery, home base, status, hours, next check | Rotations need real tails |
| Now | **PIREP review queue** with auto-accept rules and reasons (hard landing, overspeed, fuel) | Admins ask for this first |
| Next | **OCC desk**: today's line of flying per tail, delays and knock-on, crew on duty, dispatch release, gate/stand board — built on the rotation engine | The feature nobody else can copy quickly |
| Next | **Dispatcher role with teeth**: releases, fuel policy, MEL notes, reroutes pushed to the pilot's Dispatch tab | We already have the role and the workspace |
| Next | **Scoring policies** per fleet (stabilised approach, fuel, weight, taxi speed) and AutoReject | Matches vAMSYS; runs off our phase events |
| Next | **Airline website**: `{slug}.alpha6ops.com` or `/va/{slug}` with public roster, fleet, live map, recruitment form, brand colours and logo | Every VA needs a front page; today they build their own |
| Later | **Events & tours** (multi-leg, time-boxed), focus airports, community goals, leaderboards | Gamification layer |
| Later | **Economy (optional module)**: ticket revenue, fuel cost, maintenance, P&L — off by default | AeroNexus' angle; only if Dan wants it |
| Later | **Alliances / codeshares** between airlines | Multi-airline accounts make this natural |

### Integrations

| When | Idea | Why |
| --- | --- | --- |
| Now | SimBrief (done), **Navigraph charts** in Dispatch (auth via their OAuth) | Expected by anyone flying airliners |
| Next | **Discord**: webhook posts (departed / landed / PIREP accepted), rank-to-role sync, sign-in with Discord | VAs live in Discord |
| Next | **Webhooks + read-only API** with scoped tokens per airline | Lets power users build their own bots |
| Later | VATSIM / IVAO / Hoppie ACARS, streaming overlays (OBS) | Nice-to-have |

## 4. Web UI and UX

Split the site into three products with one design system:

1. **Public site** (`/`, `/Levels`, `/Download`, `/Support`, later `/Airlines` directory and `/va/{slug}`
   airline pages). Job: explain, convince, sign up. Photography and the flight-strip language we already
   use; no dashboards.
2. **Pilot portal** (`/Account/...`). Job: the things a pilot does *away from the sim*: profile, levels,
   statistics, logbook on the web, career, joining airlines, downloads and updates.
3. **Airline console** (`/Account/Airline/{id}/...`). Job: run the airline from a browser when the desktop
   is not open: schedules, fleet, roster, PIREP queue, invitations, branding, integrations, activity, level.

Principles for "beautiful and works great":

- **One design system, tokens first.** We have the palette (navy `#0c141d`, panel `#121f2a`, gold
  `#e5c44a`), Segoe UI and Consolas. Add a spacing scale, elevation, and a component set (buttons, inputs,
  strips, tables, tabs, status chips, empty states, toasts) documented in one page (`/design` in
  Development only) so pages stop hand-rolling CSS.
- **Data-dense where it earns it.** Console pages get tables with sticky headers, keyboard navigation,
  filters that live in the URL, bulk actions, and CSV export. Public pages stay airy.
- **No JavaScript framework yet.** CSP is `script-src 'none'` and the pages are fast because of it. Add a
  small, self-hosted script only when a page truly needs interaction (live map, charts); keep the CSP
  nonce-based rather than opening it up.
- **Airline theming**: an airline picks an accent colour and a logo once; its console, public page and
  Discord posts use them. Keep the base dark theme so the product stays recognisable.
- **Empty states that teach**: a new airline sees "Add your first route" with a 30-second path, not an
  empty table.
- **Accessibility floor**: visible focus, labels on everything, 4.5:1 contrast, reduced-motion respected,
  keyboard-only pass on every console page. Already mostly true; keep it in the tests.
- **Performance budget**: first paint under 1 s on a mid laptop, pages under 150 KB without images.

Suggested order: design-system page → airline console skeleton (schedules, fleet, PIREP queue) →
pilot statistics → airline public page → live map on the web.

## 5. Data capture: track what matters, respectfully

Goal: understand what pilots fly, how they use the product, and where they get stuck — so features,
scoring and airline tools can be built on evidence. Keep it lawful and trustworthy: we are a hobby
platform people trust with their evenings.

### Principles

- **Purpose-bound and disclosed.** A privacy page lists every category below in plain language.
- **Pseudonymous by default.** Interaction events carry the account id, never email or name; the join
  happens only inside our database. Raw IPs are never stored (country from IP at most).
- **Consent where it is not needed to run the service.** Flight data and PIREPs are the product — no
  consent needed beyond signing in. *Product-improvement telemetry* (UI interaction, performance) is a
  profile switch, on by default during early access, with an explanation and a one-click off. Crash
  reports stay opt-in per crash.
- **Retention by category**: flight data forever (it is the logbook); raw interaction events 13 months
  then rolled up; performance and crash data 90 days.
- **Never capture**: keystrokes, free-text contents, window screenshots, anything from other apps, exact
  file paths, or hardware identifiers beyond a random install id.
- **Export and delete**: the profile gets "Download my data" (JSON) and "Delete my account", because
  users will ask and regulators expect it.

### What to capture

**Flight and aircraft (the product itself — richest and most valuable)**

- Per flight: aircraft title, ICAO type, livery/add-on identity, origin/destination/alternate, planned vs
  flown route, scheduled vs actual times, phases with timestamps, block/flight/taxi/pause time, fuel at
  each phase, payload, distance, cruise altitude, network (VATSIM/IVAO/offline), sim version, weather at
  departure/arrival, dispatcher/OFP id.
- Landing: touchdown VS, g, pitch/bank, airspeed, distance from threshold and centreline, bounces,
  flare duration, crosswind component, runway used.
- Quality signals: overspeed, stall warning, gear/flap violations, taxi speed, pause count, slew, time
  acceleration, crash flag.
- Derived: score per policy, on-time performance, aircraft familiarity (hours per type), route history.

**App interaction (pseudonymous, aggregated for insight)**

- Sessions: app start/stop, version, OS build, monitor size, sim detected (2024/2020/none), time in each
  workspace (Home, Dispatch, Tracking, Fleet, History, Debrief), Simple/Advanced/OCC mode chosen.
- Feature use: SimBrief import (success/fail), weather briefing opened, debrief opened, OFP viewed,
  history filters used, settings changed (unit choices), account actions (join, create, invite, level).
- Friction: errors shown (code only), retries, cancelled sign-ins, offline sessions, time from install
  to first flight, drop-off points in first-run.
- Performance: startup time, replay throughput, SimConnect reconnects, memory at intervals, unhandled
  exceptions (hashed stack).

**Web interaction**

- Page views, referrer category, sign-up funnel (landing → sign-up → verified → first desktop sign-in →
  first flight), console features used, search terms inside our own pages (not free text elsewhere).
- Server-side only (no third-party analytics script — keeps the CSP and avoids consent banners for
  trackers).

**Account lifecycle**

- Already audited: sign-in method, MFA use, airline changes, plan changes. Add: verified-email latency,
  reactivation after 30/90 days, level changes with reason.

### How to build it

- **Desktop:** an `event` table in the existing local SQLite (`Alpha6OPS-flights.sqlite` already has
  `flight_event`; add `usage_event(id, category, name, occurred_utc, session_id, props_json, synced)`),
  written fire-and-forget, batched to the server every few minutes and at exit, retried offline. One
  `Telemetry.Track("dispatch.simbrief_import", new { ok = true, ms = 812 })` call site per event.
- **Server:** `POST /api/v1/events` (batch, ≤ 500 events, ≤ 64 KB, bearer auth, rate-limited) into
  `usage_event` partitioned by month in PostgreSQL; flight facts land in proper tables (`flight`,
  `flight_landing`, `flight_phase`) via the PIREP endpoint, not the event stream. Roll-ups nightly into
  `pilot_stats`, `aircraft_stats`, `route_stats`, `funnel_daily`. When volume grows, ship raw events to
  ClickHouse or Parquet in blob storage; the tables above are the contract, so nothing upstream changes.
- **Web:** a server-side middleware logs page views to the same `usage_event` table (path, status,
  duration, account id or anonymous cookie id, referrer host).
- **Analysis surface:** an internal `/admin/insights` page first (pilots active, flights per day,
  aircraft league table, funnel, feature adoption, error rate), Metabase or Grafana later. The same
  roll-ups feed the pilot's own statistics page and the airline console, so the data earns its place
  for users, not only for us.
- **Governance:** a one-page event catalogue (`docs/events.md`) listing every event name, its
  properties and its purpose; nothing gets tracked that is not in the catalogue.

### Examples of what this answers

Which aircraft types are flown most (and which add-ons); where pilots abandon first run; whether SimBrief
import failures cluster on a version; which airlines are active weekly; average landing VS by type (great
content for the community); how long from install to first flight; whether OCC mode is used at all.

## 6. Proposed sequence

1. Confirm "VA Nexus" and get Dan's top ten from the pilot side.
2. Automatic PIREP + landing analysis + live map on the desktop (Dan's side, using the phase engine).
3. Server: schedules, fleet, PIREP intake and review; airline console pages for them (Reece's side).
4. Event capture (desktop queue + `/api/v1/events` + web middleware) with the profile switch and the
   privacy page — early, so every later feature is measured from its first day.
5. Pilot statistics page and career basics; airline public page; Discord webhooks.
6. Design-system page and console polish; then billing when prices are decided.

## Sources consulted

- vAMSYS features: https://vamsys.co.uk/features and https://vamsys.co.uk/docs/pegasus/pegasus-acars
- AeroNexus: https://aeronexus.app/
- smartCARS: general knowledge of TFDi Design's product; verify current feature list before quoting it.
