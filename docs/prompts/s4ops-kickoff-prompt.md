# S4 OPS — Rebrand, Cloudflare Production & Client Portal (Claude Code kickoff prompt)

> Start Claude Code in **plan mode** and send:
> `Read @docs/prompts/s4ops-kickoff-prompt.md and execute Phase 0 and Phase 1 only. Stop at the Phase 1 checkpoint.`
> After each phase: review, commit, `/clear`, then say `Continue with the next phase in @docs/prompts/s4ops-kickoff-prompt.md — read docs/s4-migration/PROGRESS.md first.`
>
> Corrected 2026-09-22 against the actual `accounts` branch. The original draft assumed the public README
> (Cloudflare already live, no auth, React dashboard as the web front end); none of that matched the repo.

---

## 0. Role and working agreement

You are the lead engineer on **S4 OPS**, an MSFS 2024 virtual-airline operations platform (tagline: *"You fly
the airplane. We'll run the airline."*). It was built as **Alpha 6 OPS**. The name is being dropped because it
conflicts with real-world brands; the company is now **Sierra 4** and the product **S4 OPS**. The platform is
moving from free-tier testing hosting (Render + Neon) to a production setup on Cloudflare at `s4ops.com`.

How I want you to work:

1. **Explore before you edit.** If the repo and this prompt disagree, tell me and ask which one wins.
2. **Plan, then checkpoint.** Every phase ends at a **CHECKPOINT**: stop, summarize what changed and what you
   verified, and wait for me.
3. **Verify, don't claim.** Done means the §6 commands pass and you showed their output. UI work includes
   Playwright screenshots at 1440px and 390px.
4. **Keep a handoff log** in `docs/s4-migration/PROGRESS.md`: phase, dated decisions, open questions, next steps.
   Update it before every checkpoint. `docs/web-roadmap.md` stays the long-form web roadmap; keep both consistent.
5. **Small, reviewable commits** on the `accounts` branch (or a branch cut from it), one logical change each,
   conventional messages. Never push, force-push, rewrite history, or deploy without my explicit go-ahead.
6. **Ask when a decision is mine**: brand direction, identity provider, hosting architecture, anything that costs
   money, anything irreversible, anything touching Dan's (Alphatango2) `main` branch or GitHub settings.
7. Keep code comments minimal. Explanations go in commit messages and docs.

## 1. What already exists (verified 2026-09-22 — re-check anything you rely on)

Solution `Alpha6Ops.slnx`, .NET 10, no external NuGet dependencies without approval.

| Project | Role |
|---|---|
| `src/Alpha6Ops.Core` | Domain records, phase detection, rotation/delay engine, contracts |
| `src/Alpha6Ops.Desktop` | WPF pilot app: SimConnect tracking, SimBrief import, local SQLite journals, tray, account UI (`MainWindow.Account.cs`, `Account*`) — **the pilot client** |
| `src/Alpha6Ops.Server` | **The hosted product today.** ASP.NET Core Razor portal + `/api/v1`. Landing, Levels, Download, Support, Account (profile, plan, logbook with CSV import), airline console (schedule, fleet, members, invitations). Runs without JavaScript (CSP `script-src 'none'`). Dockerfile + `entrypoint.sh` migrations. |
| `src/Alpha6Ops.Identity`, `src/Alpha6Ops.Accounts` | Identity plumbing and the PostgreSQL account store (migrations, data-protection key ring in `data_protection_key`) |
| `src/Alpha6Ops.Api` | Old local read-only demo API; refuses non-Development. Not the hosted surface. |
| `src/Alpha6Ops.Pilot`, `src/Alpha6Ops.FlightLab` | Console replay host, flight lab |
| `apps/ops-web` | React dashboard — **retired**. Do not extend; decide in Phase 1 whether to delete or archive. |
| `tests/Alpha6Ops.Tests` | Dependency-free executable. `dotnet run`, never `dotnet test`. |
| `packaging` | Installer / ZIP packaging (coordinate before changing) |

**Identity today:** Auth0 tenant `dev-2m805d64kx5233sh.us.auth0.com`; Portal (Regular Web) and Desktop (Native)
apps; API audience `https://api.alpha6ops.dev`. Roles per airline with access levels Read / Operations / Branding
/ Admin (Admin requires step-up MFA within 5 minutes). Rate limiting, host filtering, body limits, CSP already in.

**Hosting today (interim):** Render free web service `alpha6ops` (Docker, `render.yaml`) at
`alpha6ops.onrender.com`, Neon PostgreSQL, `.github/workflows/keep-warm.yml`. See `docs/hosting.md`.

**Cloudflare:** set up on 2026-09-22 for `s4ops.com`. No Cloudflare config exists in the repo yet. Find out from
me (and the dashboard steps I run) what exists: zone/DNS, any Pages/Workers projects, R2/D1/KV, Turnstile.

Billing: Stripe + Link designed only (`docs/billing-roadmap.md`), not built.

## 2. Decisions already made

| Item | Value |
|---|---|
| Company / brand | **Sierra 4** |
| Product name | **S4 OPS** |
| Domain | `s4ops.com` (propose app/API subdomains, e.g. `app.` / `api.`) |
| Code identifier | `Alpha6Ops` → `S4Ops` (namespaces, projects, assemblies, solution) |
| Tenant id | `alpha6` → `s4` |
| Tagline | keep "You fly the airplane. We'll run the airline." |
| Pilot client | WPF desktop app |
| Web front end | the client-facing site + login (today's Razor portal); `apps/ops-web` retired |
| Production hosting | Cloudflare; Render + Neon are to be replaced |
| Colors / logo | [open — propose in Phase 3] |
| Server runtime on Cloudflare | [open — decide in Phase 4] |
| Identity provider | [open — Auth0 is in place; confirm or replace in Phase 4] |

## 3. Tooling

- **superpowers**: brainstorming → writing-plans → executing-plans per phase; TDD for domain and auth logic;
  systematic-debugging; verification-before-completion before each checkpoint.
- **claude-code-setup**: Phase 0 automation recommender.
- **claude-md-management**: keep `CLAUDE.md` accurate (it exists — update it, don't recreate it).
- **frontend-design**: all visual work in Phase 3.
- **playwright** (MCP): drive the portal and take screenshots.
- **security-guidance** + **claude-security**: full security pass over identity/hosting changes before their checkpoint.
- **code-review** / **pr-review-toolkit**: review each phase's diff, including silent-failure and test-coverage agents.
- **Subagents** (Explore / Plan) for wide read-only sweeps.
- **render** and **Neon** MCP/skills: read-only use while migrating off them (current config, schema, data export).
  Never change or delete Render/Neon resources without asking — they serve the live site until cutover.
- Not relevant: finance, data, pdf-viewer.

If a **Cloudflare MCP server** isn't connected, say so in Phase 0 and give the `claude mcp add` command. Check
Cloudflare behavior against current official docs, not memory.

## 4. Phases

### Phase 0 — Set up Claude Code for this repo
1. Run the claude-code-setup recommender; summarize. Install nothing yet.
2. Update `CLAUDE.md` (keep it under ~150 lines): add the Server/Identity/Accounts projects, the S4 OPS naming,
   ops-web retired, Cloudflare target with Render/Neon marked interim, never deploy/push without asking,
   pointer to PROGRESS.md.
3. Propose `.claude/settings.json`: allow build/test/lint and read-only git; ask before `wrangler deploy`,
   `git push`, deleting files, and any Render/Neon write.
4. Propose 3–4 project commands in `.claude/commands/`: `/verify` (§6), `/rebrand-audit` (leftover Alpha 6
   strings), `/ui-check` (run the portal, Playwright screenshots), `/handoff` (update PROGRESS.md).
5. **CHECKPOINT.**

### Phase 1 — Discovery & plan (no code changes)
1. **Cloudflare map:** record what exists for `s4ops.com` today and what each current piece (portal, `/api/v1`,
   database, media uploads, installer downloads, key ring) would need on Cloudflare.
2. **Hosting options** for `Alpha6Ops.Server` (a .NET app cannot run in a plain Worker). Compare at least:
   - **A.** Cloudflare Containers running the existing Dockerfile.
   - **B.** A production .NET host (VM / paid container platform) behind Cloudflare (proxy or Tunnel).
   - **C.** Rewriting the portal/API to Workers (TypeScript).
   And the database: D1 vs Postgres reached through Hyperdrive (Neon paid, or another host) vs Postgres next to
   the app host. Cover cost at 10 / 100 / 1,000 users, lock-in, migration effort, operational burden, and what
   happens to the six existing account-store migrations and live data. **Recommend one; stop for my decision.**
3. **Alpha 6 inventory** via a subagent, grouped: (a) user-facing text, (b) code identifiers/projects,
   (c) file/folder names, (d) packaging/installer metadata (product name, AppUserModelID, install dir, upgrade
   codes, exe name), (e) runtime data locations (AppData/SQLite, registry, logs, tray IDs,
   `alpha6-identity.json`), (f) docs/samples/tenant ids, (g) hosting names (Render service, Auth0 app names and
   audience `api.alpha6ops.dev`, cookie names, env var prefixes like `ALPHA6_`), (h) GitHub repo names
   (Dan's `origin` and the `ReeceCz` fork).
4. For (e), design a first-launch migration so existing desktop installs keep journals and database.
5. **Rename vs Dan's `main`:** a solution-wide rename on `accounts` will conflict with every file Dan touches on
   `main`. Propose the sequencing (e.g. merge `main` into `accounts` first, rename once, coordinate a freeze
   window with Dan) and write it down.
6. Write `docs/s4-migration/rebrand-plan.md`: ordered steps, risk per step, what stays unchanged on purpose
   (git history, historical release notes, `A601`-style sample flight numbers unless I say otherwise).
7. List everything I or Dan must do by hand: GitHub repo rename, Cloudflare dashboard, DNS, Auth0 app/API
   changes, installer signing.
8. **CHECKPOINT.**

### Phase 2 — Execute the rebrand
1. `git mv` projects and folders to `S4Ops.*`; update references, `Directory.Build.props`, packaging scripts.
2. User-facing text in the WPF app, portal, installer, docs, README. Old release notes stay, labeled Alpha 6 history.
3. Implement and test the desktop data-location migration.
4. Tenant id and config names. Anything that is an external identifier (Auth0 audience, cookie names, env vars on
   the live host) changes only per the Phase 1 plan, so the live site keeps working until cutover.
5. Verify: §6 passes, `/rebrand-audit` finds zero unintended hits, the WPF app builds, packaging produces an
   installer named for S4 OPS.
6. **CHECKPOINT** (one commit per category, reviewed with code-review).

### Phase 3 — Client-facing site redesign (the portal)
1. Use **frontend-design**. Propose 2–3 visual directions for Sierra 4 / S4 OPS (palette, type, density, a sample
   landing page and account page). Consider whether the black/yellow heritage carries forward. Render them for
   comparison, then **stop for my pick**.
2. Build a small design system inside the portal: CSS tokens with light/dark themes, type scale, spacing, status
   colors (on-time / delayed / cancelled / diverted, color plus icon/text), core components (shell, nav, table,
   badge, card, empty/loading/error states, forms). Carry the same tokens into the WPF app's resource
   dictionaries where practical so both clients read as one brand.
3. Rework the public pages (landing, Levels, Download, Support) and the signed-in pages on top of it.
4. Keep the no-JavaScript / strict CSP posture unless I approve a change. Responsive, keyboard navigable,
   WCAG AA contrast.
5. Verify: Playwright screenshots of every page at 1440px and 390px, both themes; build and tests pass.
6. **CHECKPOINT.**

### Phase 4 — Production identity & hosting on Cloudflare
Start with **brainstorming, not code**. Write `docs/s4-migration/production-design.md` and **stop for my decision**.

Identity: Auth0 is already integrated end to end (portal, API bearer tokens, desktop Native app, step-up MFA).
Compare **keeping Auth0** (production tenant, custom domain `auth.s4ops.com`, plan cost) against alternatives
only where there's a concrete reason (cost at 10 / 100 / 1,000 users, lock-in, Cloudflare fit). Also cover
Cloudflare Access for staff/admin surfaces and Turnstile on sign-up.

Hosting: implement the Phase 1 decision.

Constraints any design must meet (mark which are already met today):
1. Roles enforced on the server per airline, separate from any presentation mode.
2. Desktop sign-in via device flow or PKCE loopback; no secrets in the installer; tokens stored with DPAPI.
3. Web flows: sign-up (Turnstile), email verification, login/logout including logout everywhere, password reset,
   session expiry/refresh, MFA.
4. Protections: rate limiting/lockout (Worker logic or Cloudflare rules), CSRF, strict CORS, CSP/HSTS/
   frame-ancestors, no secrets in the repo (`wrangler secret`, `.dev.vars` gitignored).
5. Audit log of auth events and role changes.
6. Data migration from Neon with a tested rollback, and a cutover plan with the Render site kept alive until
   Cloudflare is verified.

After I decide: implement test-first (session/token logic, failure paths: wrong password, expired token,
replayed reset link, role escalation, cross-airline access), then claude-security, `/security-review`, and the
silent-failure agent, then Playwright E2E sign-up → verify → login → role-gated airline admin page → logout.
**CHECKPOINT** with a written threat-model summary.

### Phase 5 — Deploy path & roadmap
1. Staging-first on Cloudflare: preview deployments per branch, production deploy gated on my approval,
   rollback steps in `docs/deploy.md`. Retire Render/Neon only after I confirm.
2. README for S4 OPS: architecture diagram, what's live, deliberate limits.
3. Next roadmap slice in PROGRESS.md as proposed issues: desktop-to-server flight upload, live multi-aircraft
   OCC board, crew/pax/IRROPS modules, Stripe billing.
4. **CHECKPOINT.**

## 5. Guardrails
- Never commit secrets, `.dev.vars`, tokens, database files, or `work/*.txt`. Check `.gitignore` covers them.
- Don't delete or rewrite historical release notes, `outputs/`, or anything outside the repo.
- Don't change DNS, Cloudflare/Auth0/Render/Neon dashboards, or GitHub settings yourself. Give me the steps.
- Don't add paid services or NuGet/npm dependencies without asking.
- Don't touch Dan's `main` branch.
- If a phase grows beyond one focused session, split it, record the split in PROGRESS.md, checkpoint early.

## 6. Verification commands (confirm, then record in CLAUDE.md)

```powershell
dotnet build Alpha6Ops.slnx                               # S4Ops.slnx after Phase 2
dotnet run --project tests/Alpha6Ops.Tests --no-build     # tests/S4Ops.Tests after Phase 2
$env:ASPNETCORE_ENVIRONMENT='Development'; dotnet run --project src/Alpha6Ops.Server --no-build --urls https://localhost:7246
npx wrangler dev                                          # once Cloudflare config exists
```

## 7. Definition of done
- Zero unintended "Alpha 6" references; existing desktop users keep their data after upgrading.
- `s4ops.com` serves the redesigned site from Cloudflare; accounts are behind role-enforced authentication; the
  desktop app signs in against production.
- Render and Neon retired (or kept deliberately, documented).
- All §6 checks pass in CI; Playwright E2E covers the auth flows.
- `CLAUDE.md`, README, `docs/deploy.md`, `production-design.md` and PROGRESS.md are accurate.
