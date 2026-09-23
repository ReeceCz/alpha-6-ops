# S4 OPS — Rebrand plan (Alpha 6 OPS → S4 OPS)

Status: **agreed direction, not started** (2026-09-22). Dan is OK with the changes; his unpushed Codex work gates steps 2–4. Source inventory: `git grep -i -E "alpha ?[-_]?6"`
on `accounts` found 797 lines in 219 tracked files, about 190 of them `namespace`/`using` lines.
`pnpm-lock.yaml` has none.

## Target names

| Old | New |
|---|---|
| Alpha 6 Designs (company / publisher) | Sierra 4 |
| Alpha 6 OPS (product) | S4 OPS |
| Alpha 6 Flight Lab | S4 Flight Lab |
| `Alpha6Ops.*` (projects, namespaces, assemblies) | `S4Ops.*` |
| `Alpha6Ops.slnx`, `Alpha6Ops.Cloud.slnx` | `S4Ops.slnx`, `S4Ops.Cloud.slnx` |
| `Alpha6OPS.exe`, `Alpha6FlightLab.exe` | `S4OPS.exe`, `S4FlightLab.exe` |
| `%LOCALAPPDATA%\Alpha6Designs\Alpha6OPS` (data) | `%LOCALAPPDATA%\Sierra4\S4OPS` |
| `%LOCALAPPDATA%\Alpha6Designs\Alpha6OPSPreview` (install) | `%LOCALAPPDATA%\Sierra4\S4OPS\App` → **proposal:** keep install and data separate, as today, with `%LOCALAPPDATA%\Sierra4\S4OPSApp` |
| Demo tenant `alpha6` | `s4` |
| `alpha6-identity.json` | `s4ops-identity.json` |
| `ALPHA6_*` env vars | `S4OPS_*` |
| "A6" wordmark | "S4" (the new logo is a Phase 3 decision) |

## Ordered steps

Each step is one commit on a branch cut from `accounts` (`chore/s4-rebrand`). `/verify` must be green after every
step.

| # | Step | Risk | Notes |
|---|---|---|---|
| 0 | **Get Dan's work into git.** Dan has unpublished work in the Codex app (its location is unknown as of 2026-09-22). He should push it to a branch on `origin` (e.g. `dan/wip`) before step 2. | **High** if skipped: his unpushed files would still use `Alpha6Ops` namespaces and old paths | Dan is OK with the changes. Steps 1 and 5–8 don't conflict much and can go ahead. Steps 2–4 (the mass rename) wait for his push, or for step 2a. |
| 2a | **Make the rename a script:** `scripts/rebrand/Rename-ToS4.ps1`, idempotent, doing `git mv` plus text replacement from a fixed mapping table, with a `-WhatIf` mode. It is run once on `accounts`, and **Dan runs the same script on his branch** before merging, so his work converts mechanically instead of by hand-resolving conflicts. | Medium | The script can be re-run after every merge from Dan until he has converted. |
| 1 | **Compatibility layer first, no renames yet** (desktop): a data-root resolver with migration (see §Desktop data), log-indexer glob accepting both `Alpha6OPS-*` and `S4OPS-*`, identity config read as `s4ops-identity.json` then `alpha6-identity.json`, env vars read as `S4OPS_*` then `ALPHA6_*`. Server: the User-Agent parser accepts both `Alpha6OPS/` and `S4OPS/`. | Medium: this is the only step that touches user data | Test-first. Smoke tests with temp directories (see test list). Ships before any name changes, so step 3 can't strand anyone. |
| 2 | **Projects, namespaces, solutions:** `git mv` 9 src + 4 test folders and csproj files, both `.slnx`, ProjectReferences, `x:Class`/`clr-namespace` in XAML, fully-qualified Razor `@model` types, `InternalsVisibleTo`, Dockerfile COPY paths, `entrypoint.sh` DLL name, and script paths. | Low (compile errors show every miss) but noisy (about 220 files) | A pure rename commit with no behavior change, so `git log --follow` keeps history. EF model snapshot strings (`"Alpha6Ops.Accounts.X"`) change with the namespace. They aren't stored in the database. The migration IDs have no brand in them. |
| 3 | **Executables + desktop identifiers:** `AssemblyName` → `S4OPS` / `S4FlightLab`, Company/Product, app manifest, single-instance mutex `Local\Sierra4.S4OPS.SingleInstance` (also check the old one, so an old and a new build never run together), Flight Lab pipe `S4OPS.FlightLab.v4` (ships together with the desktop), journal/crash prefix `S4OPS-`, User-Agent `S4OPS/`. | Medium | Relies on step 1. |
| 4 | **Installer** (`packaging/PreviewSetup.cs`, `build-desktop.ps1`), which needs Dan's sign-off because packaging is coordinated. New uninstall key `S4OPS`, new install dir, marker `.s4ops-install`, prefix `S4OPS-Desktop-`. **Upgrade path:** find the legacy key `Alpha6OPSPreview` and marker `.alpha6-preview`, stop `Alpha6OPS.exe`/`Alpha6FlightLab.exe`, install the new version, then remove the old install dir, the three old `.lnk` shortcuts and the old uninstall key. Never touch the data root. Delete the `Alpha6Designs` parent folder only if it's empty. | **High**: a mistake either leaves two entries in Installed Apps or deletes the wrong folder | The verified-ownership check stays. Test on a VM or spare Windows user profile with 0.16/0.17 installed. Artifact names become `S4OPS-Setup-x.y.z.exe`. |
| 5a | **Archive `apps/ops-web`:** `git mv apps/ops-web archive/ops-web` with a README saying it is retired and unmaintained. Not deleted (Reece, 2026-09-22). The tenant/title renames in step 6 skip it. | Low | |
| 5 | **User-facing text:** WPF windows/dialogs/tray, Flight Lab, Razor layout and pages, the desktop callback page, installer UI, (`archive/ops-web` stays untouched). | Low | The logo/wordmark asset swap waits for Phase 3. Text says "S4 OPS" until then. |
| 6 | **Demo tenant and samples:** `alpha6` → `s4` in the demo API, `Demo.Rotation()`, desktop demo, tests and ops-web routes. Registration `N600A6` → `N600S4`. Flight numbers `A601`–`A603` → `S4601`–`S4603`, `A6101`–`A6103` → `S4101`–`S4103` (Reece: "can be anything"). | Low | Demo only, not persisted. Fixtures under `samples/` don't embed the tenant (checked with the inventory grep). |
| 7 | **Scripts and dev config:** `scripts/*.ps1`, `ALPHA6_TEST_DATABASE`/`ALPHA6_SERVER_TEST_DATABASE` → `S4OPS_*` (read both during the change), keep-warm variable, `UserSecretsId` → `s4ops-server-development` (**Reece re-runs `dotnet user-secrets set` locally**). Local test DB names/user can stay: they're only on this PC. | Low | |
| 8 | **Docs + README:** rewrite the current docs for S4 OPS. The README release history stays word for word under a heading "Alpha 6 OPS history (pre-2026-09-22)". | Low | |
| 9 | **Hosted identifiers, done at the Cloudflare cutover, not before** (see below). | High if done early | The live Render site keeps working until then. |

### Hosted identifiers: change only at cutover

| Identifier | Plan | Why |
|---|---|---|
| Cookies `__Host-Alpha6.Session` / `.Antiforgery` → `__Host-S4.*` | Change in the same release that moves to `app.s4ops.com` | Cookies are per host, so users sign in again at the new domain anyway. Doing both at once means only one forced sign-out. |
| `SetApplicationName("Alpha6Ops.Server")` | Change to `"S4Ops.Server"` at cutover, for the same reason | It only protects cookies/antiforgery tokens. Changing it invalidates them, which the domain move does anyway. |
| Auth0 API audience `https://api.alpha6ops.dev` | Create a new Auth0 API `https://api.s4ops.com` (an identifier can't be renamed). The server accepts **both** audiences for one release, and the desktop config switches to the new one. Remove the old one after desktops update. | Old desktops keep working during the rollout |
| Auth0 claim namespace `https://alpha6ops.com/claims/` | The Action emits both namespaces for one release, the server reads either, then the old one is dropped | The Action (deployed in Auth0) and the server must move together |
| Render service `alpha6ops` / `alpha6ops.onrender.com` | Unchanged until retired | Renaming would create a new URL for no benefit |
| Production DB `alpha6_accounts` | Keep. Rename it only if a dump/restore move happens anyway (Phase 4). | The name isn't visible to users |
| Postgres functions `alpha6_check_owner()`, `alpha6_preserve_founder()` | **Keep.** Applied migrations are immutable. A later migration may rename them. | Editing applied migrations breaks parity checks and existing databases |

## Desktop data-location migration (category e)

Everything user-owned sits under one root: `CrashReporter.RootDirectory`. That root holds the flight-history
DB, the log catalog DB, journals, crash reports, DPAPI session files, settings and the SimBrief cache.

**Design:**

1. Add `DataRoot.Resolve()`, called once at startup **after** the single-instance check and **before**
   `CrashReporter.Install`. `CrashReporter.RootDirectory` then returns the resolved root. The `--diagnostic` /
   smoke-test override stays first.
2. **Detection:**
   - `new = %LOCALAPPDATA%\Sierra4\S4OPS`, `old = %LOCALAPPDATA%\Alpha6Designs\Alpha6OPS`.
   - If `new` exists, use it and do nothing more.
   - If only `old` exists and no `Alpha6OPS.exe` process is running, **move** it with `Directory.Move`. That's
     atomic and instant on the same volume, and it keeps file timestamps and DPAPI blobs, which are
     CurrentUser-scoped with null entropy and so aren't tied to the path.
   - After the move, rename `Alpha6OPS-flights.sqlite` → `S4OPS-flights.sqlite` and `Alpha6OPS-logs.sqlite` →
     `S4OPS-logs.sqlite`, including any `-wal`/`-shm` siblings. Nothing is open at that point.
   - Journals and crash files keep their names, and the indexer accepts both prefixes.
   - **Verified:** the log catalog (`diagnostic_file.path`, primary key) stores **absolute** paths
     (`FileInfo.FullName`), and nothing ever deletes rows. So the migration rewrites the prefix in one
     transaction:
     `UPDATE diagnostic_file SET path = @new || substr(path, length(@old) + 1) WHERE path LIKE @old || '%'`.
   - The flight-history DB stores no file paths (checked). It only needs the file rename.
   - Write `migration.json` (`{from, to, at, version}`) in the new root.
3. **Failure handling:**
   - If the move throws (a file is locked or access is denied), use `old` in place for this session, log it,
     and retry on the next launch. **Never** fall back to an empty new root, because that looks like lost data.
   - If `old` belongs to a running `Alpha6OPS.exe`, the same fallback applies. The installer normally stops
     that process first.
4. **Why move rather than copy:** copying doubles disk use for journal-heavy users and leaves two diverging
   copies. Downgrading to an Alpha 6 build after upgrading isn't supported, and the release notes say so.
5. **Tests** (desktop smoke tests, temp directories, test-first):
   - Only the old root exists: it's migrated, the DB files are renamed, the flight history is readable, and the
     signed-in session survives.
   - Both roots exist: new wins and old is untouched.
   - Neither exists: a fresh root is created.
   - The old root is locked: the old root is used and no data is lost.
   - The migration runs twice: nothing changes the second time.
   - Journals with both prefixes are indexed.
   - An `alpha6-identity.json` fallback is read.

## Stays unchanged on purpose (`/rebrand-audit` allowlist)

- Git history, commit messages, tags.
- README release notes before 2026-09-22 (under "Alpha 6 OPS history").
- `outputs/*` historical artifacts and handoff notes.
- Applied migrations and their `alpha6_*` SQL function names. The EF snapshot follows whatever the migrations
  contain.
- Compatibility code that deliberately names the old identifiers: the legacy data root, the `Alpha6OPS-` prefix
  glob, the `alpha6-identity.json` fallback, `ALPHA6_*` env fallbacks, the `Alpha6OPS/` User-Agent, the legacy
  installer key/marker/shortcuts, and the old Auth0 audience/claim namespace during the transition window.
- `docs/s4-migration/*` and `docs/prompts/*`, which describe the old names on purpose.
- Render service name `alpha6ops` and the Neon project, until retired.
- Local-only test DB names/user (`alpha6_test`, `alpha6_identity_test`, `alpha6_server_test`).

## Manual steps for Reece or Dan

| Who | What | When |
|---|---|---|
| Dan | Confirm no unpushed `main` work; agree to a short freeze while the rename branch lands | Before step 2 |
| Dan | Sign off on the installer changes (packaging is coordinated) | Before step 4 |
| Dan | Rename GitHub repo `Alphatango2/alpha-6-ops` → e.g. `s4-ops`. GitHub redirects the old URL; update `origin` locally. | Any time after the PR merges |
| Reece | Rename fork `ReeceCz/alpha-6-ops`. Check the Render repo link still resolves. Rename Actions variable `ALPHA6_HEALTH_URL` (or retire keep-warm on cutover). | After Dan's rename |
| Reece | Rename local folder `C:\Users\Reece Czarnecki\dev\alpha-6-ops`. This breaks Claude Code's per-project memory path; copy the memory folder across. | Optional, last |
| Reece | Cloudflare: turn on Workers Paid for the Containers spike; create DNS records once the hosting decision is made; add Turnstile widgets, WAF and rate-limit rules | Phase 4/5 |
| Reece | Auth0: create the `https://api.s4ops.com` API, rename the app display names to S4 OPS, update the tenant friendly name (the TOTP issuer shown in authenticator apps) and branding/email templates, set up the custom domain `auth.s4ops.com`, add the new callback/logout/origin URLs | Phase 4 |
| Reece | Code signing: the installer is unsigned today. A signed S4 OPS installer needs a certificate; that's a cost decision. | Before public release |
| Reece | `dotnet user-secrets` re-entry for the new `UserSecretsId` | Step 7 |
| Reece or Dan | New logo/icon assets (`.ico`, header mark, Flight Lab mark) | Phase 3 |
| Reece | Email: `s4ops.com` has no MX records. Needed if Auth0 emails should come from `@s4ops.com` (custom SMTP/sender domain + SPF/DKIM). | Phase 4 |
