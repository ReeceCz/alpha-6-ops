# Alpha 6 OPS — Web and Accounts Roadmap

Status as of 18 September 2026. Owner: Reece (web, accounts, hosting). Dan owns the desktop flight side.

This document is the pick-up point for the web end of Alpha 6 OPS. It records where the work stands, what
"finished" looks like, and every phase between the two, in enough detail that a session can start from any
heading without re-deriving context. Update it at the end of each working session; it lives at
`docs/web-roadmap.md` on the `accounts` branch of the fork `ReeceCz/alpha-6-ops`.

---

## Part 1 — Where things stand today

### 1.1 The live stack

| Piece | Where | Notes |
| --- | --- | --- |
| Portal + API | https://alpha6ops.onrender.com | Render free web service `alpha6ops` (`srv-dampdocri2ms73bb6100`), Docker, region Ohio, auto-deploys from `accounts` |
| Database | Neon project `cool-flower-07048911`, branch `production` | PostgreSQL; six migrations applied; key ring for cookies stored in `data_protection_key` |
| Identity | Auth0 tenant `dev-2m805d64kx5233sh.us.auth0.com` | Portal app (Regular Web) and Desktop app (Native); API audience `https://api.alpha6ops.dev` |
| Source | `ReeceCz/alpha-6-ops`, branch `accounts` | 19 commits ahead of Dan's `main`; PR withheld until the in-person demo |
| Local dev | `https://localhost:7246`, PostgreSQL 17 on `127.0.0.1:55439` | Start with `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Alpha6Ops.Server --no-build --urls https://localhost:7246` |

Verified on 18 September: hosted `/healthz` answers `200 ok`, and Reece signed in through the hosted portal end to
end (Auth0 redirect, callback, account page).

### 1.2 What the web end already does

**Front door (anonymous)**
- Landing page (`/`) with the globe backdrop, readout strip and photo cards; explains the product and sends
  visitors to sign-up. Never offers sign-up while the server is unconfigured.
- `/Levels` — the three airline levels (no prices shown by design; pricing is undecided), mentions Link.
- `/Download` — installer page driven by `Release__*` settings; refuses to advertise a download that does not exist.
- `/Support`, `/SignedOut`, `/AuthError`, `/Error`.

**Account (signed in)**
- `/Account` — personal dashboard: profile summary, airlines, plan, activity.
- `/Account/Profile` — display name, callsign, home base, time zone, preferred workspace ("Open at sign-in":
  personal, portal, last used), profile picture (PNG/JPEG/WebP, validated by magic bytes and dimensions, ≤ 1 MB).
- `/Account/Plan` — personal plan selection (placeholder for billing).
- `/Account/Logbook` — flight log with CSV import (synonym-based column matcher, per-batch undo), summary, export.
- `/Account/Create` — create a virtual airline (slug, name, callsign, level).
- `/Account/Join` — accept an invitation token.

**Airline console (per airline, role-gated)**
- `/Account/Airline/{id}` — overview, level, activity log, logo upload, member roles, ownership transfer, invitations.
- `/Account/Airline/{id}/Schedule` — routes (flight number, origin, destination, departure UTC, block time, days,
  aircraft type), CSV import.
- `/Account/Airline/{id}/Fleet` — aircraft (registration, type, name, status).
- Access levels: `Read` (any member), `Operations` (dispatcher+), `Branding` (admin), `Admin` (admin with a
  step-up MFA within 5 minutes).

**API (`/api/v1`, bearer token from Auth0)** — everything the pages do, plus `/me/bootstrap` for the desktop app,
`/media/{kind}/{ownerId}` for avatars and logos with ETag/304 caching, and `/api/v1/release` for the desktop updater.

**Security posture** — CSP `script-src 'none'` (the whole portal runs without JavaScript), form-action limited to
the site and Auth0, host filtering, rate limiting, request body limits per endpoint, 503 gate when unconfigured,
API refuses to start outside Development unless fully configured, no secrets in the repo, secrets in Render
environment only.

**Desktop side of accounts (in `src/Alpha6Ops.Desktop`, to be extracted)** — sign in inside the app (password
realm grant), MFA enrolment and challenge in-app, sign-up and password reset in-app, browser sign-in fallback
with a branded callback page that links to the web console, remembered email/password (DPAPI), profile picture,
airline logos, logbook window, step-up dialog.

### 1.3 Test coverage (all green at the close of 18 September)

| Suite | Runs with | Covers |
| --- | --- | --- |
| `tests/Alpha6Ops.Accounts.Tests` | `dotnet run --project … --no-build` | Service rules, migrations vs. snapshot parity, roles, invitations, logbook, media (160 checks) |
| `tests/Alpha6Ops.Server.Tests` | same, needs `ALPHA6_SERVER_TEST_DATABASE` | Pages, API, CSP, host filtering, hosted-production start-up with database key ring |
| `tests/Alpha6Ops.Desktop.Identity.Tests` | same | Desktop session, MFA, callback page (102 checks) |
| Desktop `--smoke-test` | `Alpha6Ops.Desktop.exe --smoke-test <dir>` | Window flows with a fake HTTP handler (70 + 444 checks) |
| `tests/Alpha6Ops.Tests` | same | Dan's core engine |
| `auth0/` action test | node | Post-login actions (security challenge, account claims) |

Never `dotnet test`; there is no test framework in this repository.

### 1.4 Known limitations to keep in mind

- No email of any kind. Invitations are tokens the issuer hands over manually. Auth0 sends verification and
  password-reset mail through its own test sender, which is rate-limited and not for real users.
- No public airline pages, no directory, no way to discover or apply to an airline without an invitation.
- No domain. Everything is on `onrender.com`; Auth0 branding still shows the tenant name on the login page.
- Free tier: the Render instance sleeps after 15 minutes idle (first request afterwards takes ~50 s); Neon
  free tier keeps a short point-in-time window and has compute limits. Fine for testing, not for launch.
- The desktop installer still points at `localhost:7246`; nobody but Reece can use in-app sign-in until 0.17.0 ships.
- The Neon password and the Render sync-hook key were pasted into a chat during setup and must be rotated.
- Npgsql logs a harmless `libgssapi_krb5.so.2` line on every start in the container.
- Hosted database is empty (separate from the local dev database), so the first sign-in creates a fresh account.

---

## Part 2 — The destination

### 2.1 What "finished" means for the web end

Alpha 6 OPS on the web is the account and airline layer under the desktop app. A pilot can find out what Alpha 6
is, sign up in a minute, download the app, join an airline (or found one), and see their career grow. An airline
operator gets a console that runs a virtual airline: crew, roles, schedule, fleet, branding, recruitment,
integrations and, later, billing. The site is fast, script-free where it can be, accessible, and never leaks
anything it should not.

Concretely, the finished web end has:

1. A marketing front end that explains the product with real screenshots and short videos.
2. Account management: profile, security, notification preferences, data export, account deletion.
3. Airline console: everything today plus recruitment (invitations by email, join links, applications), public page
   and branding, integrations (Discord), statistics, and a billing tab.
4. Public pages: airline directory, airline public pages, pilot public profiles (opt-in).
5. Pilot career: statistics, ranks, milestones, badges, with the desktop app filing flights automatically.
6. Community: Discord webhooks and a bot with role sync.
7. Reliability: custom domain, email deliverability, backups, monitoring, paid tiers when needed.
8. A clean seam with Dan's desktop project: `Alpha6Ops.Desktop.Account` as its own library.

### 2.2 Guiding rules (unchanged)

- Every feature must fight for its existence: name the user, the moment, and what they can do afterwards that
  they could not before. If that sentence is hard to write, the feature waits.
- Dan owns flight-sim domain decisions (ranks, phases, what counts as a landing). Web work that needs one of those
  decisions records the question in Part 5 rather than inventing an answer.
- No new NuGet or npm dependency without asking. Prefer `HttpClient` against a REST API over an SDK package.
- Tests run with `dotnet run`. Every phase ends with all suites green, the dev database migrated, the local
  server restarted and probed, a commit on `accounts`, and a push to the fork.
- Commit only on `accounts`; never push to Dan's `origin`.

---

## Part 3 — Housekeeping (do first; about half a day in total)

These are small, and several of them are safety items that should happen before anyone but Reece signs up.

### H1. Rotate the secrets that went through chat

1. Neon → project `cool-flower-07048911` → Roles → reset the password for the application role.
2. Build the new `.NET` connection string (`Host=…;Port=5432;Username=…;Password=…;Database=…;SSL Mode=Require;
   Channel Binding=Require`) and paste it into Render → `alpha6ops` → Environment → `ConnectionStrings__Accounts`.
   Render redeploys automatically; confirm `/healthz` still answers `ok`.
3. Render → Blueprint → regenerate the sync hook (or delete the blueprint link if not used).
4. Delete `work/neon-connection-string.txt`, `work/keyring-certificate.txt`, `work/certificate-base64-only.txt`
   from the local machine.
5. Optional: regenerate the key-ring certificate with `scripts/new-keyring-certificate.ps1` and replace the two
   `DataProtection__*` values. Doing so signs everyone out once; harmless today.

### H2. Keep the free instance awake

- GitHub → `ReeceCz/alpha-6-ops` → Settings → Secrets and variables → Actions → Variables → `ALPHA6_HEALTH_URL` =
  `https://alpha6ops.onrender.com/healthz`.
- The workflow `.github/workflows/keep-warm.yml` already exists (every 10 minutes). Check the Actions tab shows a
  green run; the first one may need the workflow enabled.

### H3. Point the desktop app at the hosted server and ship 0.17.0

- Add a hosted variant of `alpha6-identity.json` (`ApiBaseUrl` and `PortalUrl` = `https://alpha6ops.onrender.com`).
- `packaging/build-desktop.ps1`: copy the hosted config into the publish output; bump `Version` to `0.17.0` in
  `src/Alpha6Ops.Desktop/Alpha6Ops.Desktop.csproj`.
- Build the installer, create a GitHub Release on the fork (`v0.17.0`), attach the installer and zip, record the
  SHA-256.
- Set `Release__DownloadUrl`, `Release__Version`, `Release__Sha256` in Render so `/Download` and the desktop
  Updates dialog work.
- Test on a clean Windows profile: install, sign in in-app, sign in in the browser, open the portal from the app.
- Send the release link to Dan.

### H4. Render and Neon notifications and limits

- Render → service → Settings → Notifications: enable deploy-failed emails.
- Neon → project → Settings: note the free-tier compute hours and storage; set a calendar reminder to check usage
  monthly.
- Add the Render "deploy failed" and the keep-warm workflow to a small "operations" note in `docs/hosting.md`.

### H5. Log noise

- Npgsql probes for Kerberos on every connection and logs `libgssapi_krb5.so.2: cannot open shared object file`
  when the library is absent. The clean fix is one `apt-get install -y libgssapi-krb5-2` line in the runtime stage
  of the Dockerfile. It is small and removes an error-level line that will confuse everyone reading logs later.

### H6. Repository hygiene

- Decide what to do with the Neon CLI artifacts (`neon.ts`, `package.json`, `package-lock.json`, `skills-lock.json`,
  `.claude/skills/*`). Recommendation: commit `neon.ts`, `package.json`, `package-lock.json` and `skills-lock.json`
  under a short note in `docs/hosting.md`; add `.claude/settings.local.json` to `.gitignore`.
- Add `docs/web-roadmap.md` (this file) to the repo and mention it in `CLAUDE.md` under docs.
- Enable CI: a GitHub Actions workflow that builds `Alpha6Ops.Core`, `Accounts`, `Server` and runs the accounts
  and server suites against a service-container PostgreSQL on every push to `accounts`. (WPF projects stay
  local-only; the workflow must not try to build them.)

### H7. Auth0 tidy-up

- Universal Login: upload the Alpha 6 logo and set the colours so the login page does not show the tenant name.
- Applications: confirm the Desktop app has only the loopback callback and the Portal app has exactly the four
  URLs (two localhost, two Render). Remove anything else.
- Tenant settings: set the friendly name "Alpha 6 OPS" and support email once one exists (see Phase A).
- Write down the whole Auth0 configuration in `docs/identity-foundation.md` (apps, grants, actions, MFA factors,
  connections) so the tenant can be rebuilt if it ever needs to move.

---

## Part 4 — Delivery phases

Each phase lists purpose, design, work items, tests, and acceptance. Estimates are working sessions, not
calendar time.

### Phase A — Email foundation (1 session)

**Purpose.** Nothing multi-user works without email: invitations, application decisions, security notices, and
Auth0's own verification and password-reset mail. This phase gives the server one reliable way to send, and users
control over what they receive.

**Decisions to make first**
- Provider: **Brevo** (300/day free, verified single sender allowed without a domain, plain HTTPS API). Resend
  and Postmark need a verified domain, which does not exist yet. Switching later is one class.
- Sender address: a mailbox Reece controls (for example `alpha6ops.mail@gmail.com` or an Outlook alias), verified
  in Brevo. Replace with `no-reply@<domain>` once a domain exists.
- Reply-to: the support mailbox (can be the same address for now).

**Design**
- `Alpha6Ops.Accounts`:
  - `IEmailSender` with `SendAsync(EmailMessage)`; `EmailMessage(To, Subject, TextBody, HtmlBody, Category, Tags)`.
  - `BrevoEmailSender` using `HttpClient` (`POST https://api.brevo.com/v3/smtp/email`, header `api-key`).
    No SDK package.
  - `NullEmailSender` for Development when no key is configured (logs the message at Information level).
  - `email_outbox` table: `Id, UserId?, ToAddress, Subject, Category, TextBody, HtmlBody, Status (queued, sent,
    failed), Attempts, LastError, CreatedAt, SentAt, ProviderMessageId`. Bodies are stored so a failed send can be
    retried and every message is auditable. Purge bodies after 30 days (keep the row).
  - `EmailOutboxDispatcher` hosted service: polls every 15 s, sends queued rows, exponential back-off up to 5
    attempts, marks failed. One instance today; use `FOR UPDATE SKIP LOCKED` so replicas are safe later.
  - `EmailTemplates`: plain text plus minimal HTML (single column, no external images, no scripts, brand
    colours inline). Templates: `AirlineInvitation`, `InvitationReminder`, `ApplicationReceived` (to airline
    staff), `ApplicationApproved`, `ApplicationDeclined`, `RoleChanged`, `RemovedFromAirline`, `Welcome`,
    `SecurityNotice` (new device / MFA enrolled / password changed — sourced from Auth0 log stream later).
  - `NotificationPreferences` on the profile: `AirlineMail`, `CareerMail`, `ProductMail` (each on/off);
    `SecurityMail` cannot be turned off. Every non-security message carries a signed one-click unsubscribe link
    (`/mail/unsubscribe?u=…&c=…&sig=…`) that works without signing in, and `List-Unsubscribe` headers.
  - Rate limiting per recipient (max 20 messages/day per address) as a safety valve against a bug or abuse.
- Configuration: `Email__Provider` (`brevo` or `none`), `Email__ApiKey`, `Email__FromAddress`, `Email__FromName`,
  `Email__ReplyTo`. Added to `render.yaml` (`sync: false` for the key) and `docs/hosting.md`.
- Auth0: Branding → Email Provider → Brevo (SMTP or API key) so verification, password-reset and MFA enrolment
  mail come from the same sender. Customise the templates with the Alpha 6 name and the support address.
- Portal: `/Account/Profile` gains a "Email" section with the three switches and the last five messages sent
  to the user (subject, date, status) so support questions ("did it send?") answer themselves.

**Tests**
- Accounts suite: outbox enqueue, dispatcher retry and back-off with a fake `HttpMessageHandler`, unsubscribe
  signature validation, preferences respected, security mail cannot be suppressed, per-recipient limit.
- Server suite: profile preference form round-trip; unsubscribe link works anonymously; API returns message history.
- A manual send to Reece's own address from the hosted environment.

**Acceptance.** An invitation created on the hosted console reaches the invitee's inbox within a minute, the link
in it works, and the invitee can turn airline mail off and on from their profile.

### Phase B — Joining virtual airlines (1–1.5 sessions)

**Purpose.** Three ways in, chosen per airline: invitation by email, a shareable join link, and an application
from the public page. Staff manage all three from one Crew page.

**Design**
- **Invitations by email** (finish the existing feature): sending uses Phase A; console shows pending, sent,
  accepted, expired; resend and revoke; invitee lands on `/Account/Join?token=…` which works whether they already
  have an account, need to sign in, or need to sign up (return URL preserved through Auth0).
- **Join links**: `airline_join_link(Id, AirlineId, Code (8 chars, unambiguous alphabet), Label, MaxUses?, Uses,
  ExpiresAt?, Mode (direct | application), Roles[], CreatedBy, CreatedAt, RevokedAt?)`. Public URL
  `/join/{code}` shows the airline card and a Join button; `direct` creates the membership, `application` creates
  an application. Console: create, copy, revoke, see uses. Rate-limit lookups.
- **Applications**: `airline_application(Id, AirlineId, UserId, Source (public_page | join_link), Message (500),
  Status (pending | approved | declined | withdrawn), DecidedBy?, DecidedAt?, DecisionNote, CreatedAt)`.
  Pilot side: apply from the public page or a join link in application mode, see status on `/Account`, withdraw.
  Staff side: `/Account/Airline/{id}/Applications` queue (Operations access), approve with roles, decline with an
  optional note; both send mail. One pending application per airline per user; declined users can reapply after
  7 days (configurable later).
- **Airline recruitment settings** on the console: `Recruiting` (on/off), `AcceptsApplications`, `JoinMode`
  default, `ApplicationPrompt` (text shown to applicants, e.g. "tell us your home base").
- **Crew page improvements**: search, filter by role, last active, flights this month (from the logbook), remove
  member with confirmation and email, leave airline (member-initiated; owners must transfer first).
- **Audit**: every join, approval, decline, removal and link creation goes to the airline activity log (exists).

**Tests**
- Accounts suite: code generation uniqueness, expiry and max uses, mode behaviour, duplicate application rule,
  approval assigns roles, decline note stored, owner cannot leave, removal revokes access immediately.
- Server suite: `/join/{code}` anonymous render, join with and without an account, application queue page
  authorisation (Read cannot decide; Operations can), CSP intact on new pages.
- Desktop: Join page reachable from the app's Airlines dialog (link opens the browser; the app already refreshes
  bootstrap on focus).

**Acceptance.** Dan creates an airline on his PC, posts a join link in Discord, Reece joins from a fresh account,
and appears on Dan's Crew page within seconds; an application from the public page reaches the queue and its
decision email arrives.

### Phase C — Public pages, branding and the directory (1 session)

**Purpose.** Let an airline present itself and let pilots discover airlines. This is also the first thing a
prospective operator judges Alpha 6 by.

**Design**
- `virtual_airline` gains `Description (1000)`, `Website`, `DiscordInvite`, `AccentColor (#rrggbb)`, `IsPublic`,
  `Recruiting`, `HomeBase (ICAO)`, `Region`, `Founded`. `AccountRules.Branding` validates (https URLs,
  Discord invite host, colour format, description length, no HTML).
- Console tab **Branding and public page** (`/Account/Airline/{id}/Branding`, `Branding` access): fields above,
  logo (exists), live preview link, "Publish" toggle.
- Public page `/va/{slug}` (anonymous, rate-limited, cached 60 s): logo and accent colour as a CSS variable on
  the page root only, name, callsign, home base, description, fleet by type with counts, top routes, crew count,
  founded date, recruiting call to action (application form if enabled; otherwise "invitation only"), links.
  Server test: private airline is 404; every field HTML-encoded; page renders without scripts.
- Directory `/Airlines`: public airlines, filter by region and recruiting, sort by crew size or newest, search by
  name/callsign; paginated; each card links to the public page. A small "Featured" slot the platform admin can
  set later.
- Pilot public profile `/pilots/{callsign}` (opt-in on Profile): avatar, callsign, airlines, hours, rank (Phase H),
  recent flights (count only until the pilot opts in to details).
- SEO basics: `<title>` and description per page, Open Graph image (airline logo), `sitemap.xml` of public pages,
  `robots.txt` that disallows `/Account` and `/api`.

**Acceptance.** A visitor with no account opens `/Airlines`, finds Dan's airline, reads its page and applies;
the application shows up in the queue.

### Phase D — Trust, legal and account lifecycle (0.5 session)

**Purpose.** Required before inviting strangers, and a condition for taking the Google sign-in consent screen out
of testing mode.

- `/Privacy` and `/Terms` pages, plain language, dated, linked from the footer and the sign-up page. Cover what is
  stored (identity claims, profile, airline data, logbook, images, email log), where (Neon, Render, Auth0, Brevo),
  retention, and how to delete.
- Account deletion: `/Account/Profile` → Delete account → step-up MFA → confirmation page → soft-delete with a 14-day
  grace period (email with a cancel link), then a job hard-deletes user rows, media, outbox bodies, and calls the
  Auth0 Management API to delete the identity. Owners must transfer or close airlines first.
- Data export: `/Account/Profile` → Download my data → a ZIP with JSON (profile, memberships, logbook, activity)
  and the avatar. Generated on request, streamed, not stored.
- Verified email enforcement: server requires `email_verified` for airline creation and for accepting invitations;
  the UI explains and offers "resend verification" (Auth0 Management API).
- Cookie notice: not needed for strictly necessary cookies only, which is the current state. Keep it that way; add
  analytics only in the self-hosted, cookieless form described in Part 6.

### Phase E — Marketing front end (1–2 sessions)

**Purpose.** Today the landing page is a decent front door for someone who already knows what Alpha 6 is. The
marketing front end is for someone who does not: a pilot who saw a Discord post, an operator comparing platforms.

**Information architecture**
- `/` — hero with one real screenshot, one sentence on the promise ("a persistent airline day"), primary action
  **Get the app**, secondary **See how it works**.
- `/how-it-works` — three steps with screenshots: install and sign in, join or found an airline, fly and watch
  the day change. Embed the demo video (Phase F).
- `/for-pilots` — what the desktop app gives a pilot (assignment, live timeline, debrief, logbook, career).
- `/for-airlines` — what the console gives an operator (crew, schedule, fleet, branding, recruitment, Discord,
  statistics); a "start an airline" action.
- `/levels` — exists; add a comparison table without prices and a "we will announce pricing before launch" line.
- `/download` — exists; add system requirements, MSFS 2024 note, SimConnect note, changelog link.
- `/about` — Alpha 6 Designs, who Dan and Reece are, why the product exists, contact.
- `/changelog` — generated from GitHub Releases (server fetches the release list, caches 10 minutes) so it is never
  stale.
- `/status` — simple: server time, database reachable, last deploy version; links to a future status page.
- Footer everywhere: Privacy, Terms, Support, Discord, GitHub.

**Design direction.** Keep the current shell (globe backdrop, readout, photo cards) and extend it. Use real
screenshots of the desktop app and console, not stock. One accent colour, one display face, generous whitespace,
no decorative animation. All pages stay script-free; embedded video is a poster image linking to YouTube (CSP
`script-src 'none'` stays), or a self-hosted MP4 in a `<video>` tag if the file is small.

**Content plan.** Write the copy in Markdown first (`docs/site-copy/`), review with Dan, then build the pages.
Screenshots: a checklist of the exact windows and states to capture, taken at 1600×1000, saved under
`src/Alpha6Ops.Server/wwwroot/img/screens/`, optimised to WebP.

**Acceptance.** Someone who has never heard of Alpha 6 can explain, after two minutes on the site, what it is,
who it is for, and how to start.

### Phase F — Demo videos (0.5 session of production time, plus recording)

- Three short videos (60–120 s each), silent with captions so they work muted in Discord:
  1. Pilot: install, sign in in-app, receive an assignment, fly (Flight Lab replay), see the debrief and logbook.
  2. Operator: create an airline, upload the logo, add a route and aircraft, create a join link, approve an
     application.
  3. The airline day: two pilots, one aircraft, the second leg moving because the first arrived late (this one
     depends on Dan's engine and is the flagship demo).
- Tooling: OBS for capture (free), captions burned in with a script; export 1080p MP4; upload to a YouTube
  channel for Alpha 6 Designs (unlisted until launch). Poster frames exported as WebP for the site.
- Keep a `docs/demo-scripts.md` with the shot list so videos can be re-recorded after UI changes.

### Phase G — Desktop extraction and the pull request to Dan (0.5–1 session)

Unchanged from the approved plan:
- New WPF class library `src/Alpha6Ops.Desktop.Account` (net10.0-windows) holding `AccountWindow`, the dialogs,
  `DesktopAccountSession`, `SystemLoginBrowser`, `IdentityConfiguration`, `OfflineSessionPolicy`, `PortalStyles`,
  the smoke test and the workspace assets. `Duende.IdentityModel.OidcClient` moves with it (moved, not added).
- Dan's `Alpha6Ops.Desktop` keeps `MainWindow.Account.cs` as the seam and merges `PortalStyles.xaml` from the
  library. A small `IAccountHost` interface replaces the two helpers the library needs from Desktop.
- After extraction, `git diff --stat main..accounts -- src/Alpha6Ops.Desktop` should show only the seam files.
- Then: rebase or merge `main` into `accounts`, run every suite, open the PR to Dan after the in-person demo with a
  description that lists what changes in his project and what does not.

### Phase H — Pilot statistics and career (1 session)

- Server: `LogbookStatistics` (hours and flights by aircraft type, by month for 12 months, top 10 routes,
  landing-rate average/best/last-10 trend, day/night split if present, longest flight, airports visited) and
  `PilotCareer` (rank from block hours on a default ladder, next rank, hours to next, milestones).
- Ladder and milestone names are placeholders until Dan confirms: Cadet 0 h, Second Officer 25 h, First Officer
  100 h, Senior First Officer 250 h, Captain 500 h, Senior Captain 1000 h; milestones for first flight,
  10/50/100/500 flights, 100/500/1000 h, 10 airports, 50 landings under −200 fpm.
- `pilot_milestone(UserId, Code, AchievedAt)`; awarded when the logbook changes (import, manual entry, desktop
  filing). Email `CareerMail` on rank change.
- Web: `/Account/Statistics` with stat tiles, CSS-only bars and a server-rendered inline SVG sparkline (no
  scripts); career strip on `/Account` and `/Account/Logbook`; rank on the public pilot profile.
- Desktop: rank and hours-to-next in the Logbook window (from `/me/career`).
- Per-airline statistics on the console: hours and flights per member this month, most-flown routes, fleet
  utilisation (flights per aircraft). These feed the public page counts.

### Phase I — Discord (1 session for webhooks; the bot is a later phase)

**Webhooks first**
- `airline_webhook(Id, AirlineId, Label, UrlProtected, Events bitmask, CreatedBy, CreatedAt, LastStatus,
  LastSentAt)`; URL stored with `IDataProtector`, never displayed again; only `discord.com/api/webhooks/` and
  `discordapp.com/api/webhooks/` accepted.
- Events: `member.joined`, `member.roles_changed`, `application.received`, `application.decided`,
  `airline.level_changed`, `flight.filed`, `logo_changed`, `rank.achieved`.
- Queued `DiscordDispatcher` hosted service (shares the outbox pattern from Phase A), one retry, embed with logo
  and accent colour. Console: Integrations section on the airline page with add, test message, remove, last status.

**Bot later (its own phase)**
- A small Discord application: `/link` to connect a Discord user to an Alpha 6 account (OAuth through the
  portal), role sync (airline roles → Discord roles), `/status` and `/roster` slash commands, and posting the
  airline's daily rotation. Hosting: a Render background worker (free tier) or a Neon Function. Requires a decision
  on where the bot token lives and who administers the Discord application.

### Phase J — Operations depth (needs Dan; sequence after the PR lands)

These are the features that make Alpha 6 different from a logbook site, and they depend on the desktop engine:
- **Automatic PIREP filing**: the desktop app posts a flight to `/me/flights` at block-in with an airline context
  (`AirlineId`, route id, aircraft id). Needs Dan's block-in hook. Server side: nullable `AirlineId` on
  `pilot_flight`, validation against the airline's schedule and fleet, duplicate suppression.
- **Dispatch**: assign a route and aircraft to a pilot for a date; the desktop app shows it as the next flight.
  `assignment(Id, AirlineId, RouteId, AircraftId, UserId, Date, Status)`; console page; API for the app.
- **Events and tours**: an airline schedules a group flight or a multi-leg tour with a badge on completion.
- **NOTAM / announcements**: staff post a notice shown on the app dashboard and on the public page.
- **Rotation on the web**: read-only view of an aircraft's day (legs, actuals, drift), fed by the engine.

### Phase K — Billing (design exists; build when Dan says so)

`docs/billing-roadmap.md` holds the design: Stripe Checkout and Customer Portal with Link as the default payment
method, subscription per airline level, webhooks updating `SubscriptionStatus`. Build order when triggered:
Stripe.net (approved to add later) → checkout session → webhook endpoint → console billing tab → grace and
downgrade rules → invoices in the portal. Prices remain unpublished until decided.

### Phase L — Platform maturity (ongoing; items become due as usage grows)

- **Custom domain** (~$10–15/year): pick the name with Dan; add to Render (free TLS), Auth0 custom domain
  (needs a paid Auth0 plan for custom domains — check current pricing; otherwise keep the tenant domain), Brevo
  domain verification (SPF, DKIM, DMARC) for deliverability, `Hosting__PublicHosts` for the host filter.
- **Backups**: nightly GitHub Actions job running `pg_dump` against Neon, encrypted with a repository secret,
  stored as a workflow artifact (90 days) and optionally copied to a free object-storage bucket. Test a restore
  once a quarter.
- **Monitoring**: keep-warm doubles as an uptime check; add a free uptime monitor (e.g. UptimeRobot) that alerts
  on Discord; Render metrics for memory and CPU; a weekly look at Auth0 logs for failed logins.
- **Off the free tiers**: Render Starter (no sleep) and a Neon paid tier when there are real users; the code
  already supports replicas (database key ring, `SKIP LOCKED` dispatchers).
- **CI/CD**: build and test on every push (see H6); deploy previews per pull request are available on Render if
  wanted.
- **Security**: dependency updates monthly (`dotnet list package --outdated`); a `/security-review` pass before
  launch; Auth0 attack protection settings (brute-force, breached-password detection) reviewed; content-security
  headers already strict.
- **Accessibility**: keyboard and screen-reader pass on every portal page; colour contrast check on the shell
  tokens; `prefers-reduced-motion` respected (no animation today, keep it so).
- **Performance**: response caching for public pages; image variants for avatars and logos (already limited to
  2048 px; add a 256 px thumbnail on upload).

---

## Part 5 — Decisions and open questions

### For Reece to decide (now or at the start of Phase A)
- Sending address and provider for email (recommendation: Brevo with a dedicated Gmail until a domain exists).
- Whether Auth0's emails should go through the same provider (recommendation: yes).
- Domain name candidates to discuss with Dan.
- Whether the Neon PR-branch workflow (a Neon branch per pull request) is worth enabling now (recommendation: not
  until CI exists).

### For Dan (not blocking; collect answers at the demo)
- Rank ladder names and thresholds; whether ranks are per airline or per pilot.
- Where the block-in hook lives in the phase engine so the app can file flights automatically.
- What a "landing" is for statistics (touchdown rate source, bounced landings).
- Microsoft sign-in (needs an Azure app registration) — wanted or not.
- Account linking (Google identity and email identity for the same person) — wanted or not.
- Discord: who owns the server, whether Alpha 6 should have its own application, which channel gets webhooks.
- Pricing, so the Levels page can eventually show it.

---

## Part 6 — Data capture and analytics (from `docs/product-direction.md`, condensed)

Track what helps a pilot or an operator, respectfully: no third-party trackers, no cookies beyond the session,
aggregate by default. Build in this order once Phase A exists:
1. Product events table (`product_event(UserId?, AirlineId?, Name, Properties jsonb, At)`) written by the server
   for key moments (signed up, created airline, joined, imported logbook, filed flight, opened console page).
2. Daily roll-ups (`daily_metric`) computed by a hosted service; a platform-admin page `/Admin/Metrics` (new
   `platform_admin` flag on the user) showing sign-ups, active pilots, airlines, flights filed, emails sent.
3. Desktop app health: version in use (already sent in bootstrap), crash counts (opt-in), SimConnect connect
   success rate (opt-in).
4. Later: per-airline dashboards for operators (their own crew only).

---

## Part 7 — Sequencing and estimates

| Order | Phase | Sessions | Depends on |
| --- | --- | --- | --- |
| 1 | Housekeeping H1–H7 | 0.5 | — |
| 2 | A — Email foundation | 1 | Brevo account, sender address |
| 3 | B — Joining airlines | 1–1.5 | A |
| 4 | C — Public pages and directory | 1 | B (applications) |
| 5 | D — Trust and legal | 0.5 | A (deletion emails) |
| 6 | G — Desktop extraction and PR | 0.5–1 | Demo to Dan |
| 7 | E — Marketing front end | 1–2 | Screenshots, copy review |
| 8 | F — Demo videos | 0.5 + recording | E, Dan's engine for video 3 |
| 9 | H — Statistics and career | 1 | Dan's ladder answer (placeholders OK) |
| 10 | I — Discord webhooks | 1 | A (dispatcher pattern) |
| 11 | J — Operations depth | 2+ | Dan's hooks, PR merged |
| 12 | K — Billing | 1–2 | Dan's go-ahead, pricing |
| 13 | L — Platform maturity | ongoing | usage |

Reordering is fine. The only hard dependencies are: email before joining, joining before public pages, extraction
before the PR, and Dan's hooks before operations depth.

---

## Part 8 — How to resume a session

1. `git status` on `accounts`; `git log --oneline -5`.
2. Build: `dotnet build Alpha6Ops.slnx` (Desktop needs the SimConnect SDK path file; the server projects build
   anywhere).
3. Start the local server (Development) and probe `https://localhost:7246/healthz`.
4. Run the suites (see 1.3). The server suite needs `ALPHA6_SERVER_TEST_DATABASE` pointing at
   `alpha6_server_test` on `127.0.0.1:55439`.
5. Check the hosted service: Render dashboard → `alpha6ops` → Events, or `https://alpha6ops.onrender.com/healthz`.
6. Open this file, find the next unchecked item, and start there.

Reference documents: `docs/hosting.md` (Render and Neon), `docs/identity-foundation.md` (Auth0 and in-app
sign-in), `docs/product-direction.md` (competition, ideas, UX, data capture), `docs/billing-roadmap.md`,
`docs/data-model.md`, `src/Alpha6Ops.Server/README.md` (configuration keys).
