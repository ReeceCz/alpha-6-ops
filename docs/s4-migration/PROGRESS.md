# S4 OPS migration — progress log

Kickoff prompt: `docs/prompts/s4ops-kickoff-prompt.md`. Branch: `accounts` (fork `ReeceCz/alpha-6-ops`).
Read this file first when resuming after `/clear`.

## Current state — 2026-09-22

**Phase 0 and Phase 1 are done and committed on `accounts` (not pushed).** Direction is agreed. Phase 2 can
start with the steps that don't depend on Dan's unpushed work. The "Still open" items below are non-blocking.

### Verification baseline (2026-09-22, before any rename)

| Check | Result |
|---|---|
| `dotnet build Alpha6Ops.slnx` | OK, 0 warnings, 0 errors |
| `dotnet build Alpha6Ops.Cloud.slnx` | OK, 0 warnings, 0 errors |
| `tests/Alpha6Ops.Tests` | 85 passed, exit 0 |
| `tests/Alpha6Ops.Desktop.Identity.Tests` | 102 passed, exit 0 |
| `tests/Alpha6Ops.Accounts.Tests` (local PG 55439) | 160 passed incl. PostgreSQL integration, exit 0 |
| `tests/Alpha6Ops.Server.Tests` (local PG 55439) | 94 passed, 0 failures, exit 0 |

Without the DB env vars, the accounts suite skips its integration part (53 pure checks) and the server suite
**exits 1**. That's expected, and it's recorded in CLAUDE.md.

## Decisions

| Date | Decision | By |
|---|---|---|
| 2026-09-22 | Rename Alpha 6 OPS → **S4 OPS**, company **Sierra 4**, because of real-world brand conflicts | Reece + Dan |
| 2026-09-22 | Production hosting moves to **Cloudflare** (`s4ops.com`). Render + Neon free tiers were interim. | Reece |
| 2026-09-22 | `apps/ops-web` retired. The client-facing site + login is the portal in `Alpha6Ops.Server`. | Reece |
| 2026-09-22 | Hosted identifiers (cookies, data-protection app name, Auth0 audience/claims) change **at the Cloudflare cutover**, not during the code rename | Proposed |
| 2026-09-22 | Stay on free tiers: Cloudflare edge + static landing, Render Free app, Neon Free, Auth0 Free | Reece |
| 2026-09-22 | Archive `apps/ops-web` (don't delete); sample flight numbers → `S4xxx` | Reece |
| 2026-09-22 | Design direction C "Glass cockpit": B612, cockpit display colours, night default + day theme | Reece |
| 2026-09-22 | No users besides Reece: the desktop data migration and installer legacy-upgrade path are dropped, and names are renamed outright | Reece |
| 2026-09-22 | `app.s4ops.com` proxied by Cloudflare to Render Free; rate limits key on `CF-Connecting-IP` from Cloudflare ranges only (`Hosting__TrustCloudflare`) | Built (commit on `chore/s4-rebrand`) |
| 2026-10-01 | Logo = Dan's taxiway-sign concepts: yellow `S4` tile (his Option 6) for favicon/app icon, Option 3 lockup "SIERRA 4 / Airline Operations Platform \| Powered by RYNEL" for the header | Reece + Dan |
| 2026-10-01 | **RYNEL** is Dan's engine brand and appears on the product ("Powered by RYNEL"). Name not yet checked for conflicts | Reece |
| 2026-10-01 | Merge Dan's dashboard look into the glass cockpit: yellow brand/navigation accent and a wide display face (Michroma) for headings and logo; keep cockpit structure and unambiguous status colours (yellow must not mean both "brand" and "caution") | Reece |

## Answers received 2026-09-22 (Reece)

- **Cost:** stay free as long as possible. Direction: the free hybrid in `hosting-options.md` §4 (Cloudflare edge
  + static landing, app on Render Free, Neon Free, Auth0 Free). Cloudflare Containers + Neon Launch is the upgrade
  path once there are paying users.
- **Cloudflare account:** Dan registered `s4ops.com` on Cloudflare on 2026-09-22. That is the only thing in it,
  and it's his first Cloudflare use. Reece has his own sites elsewhere.
- **Dan:** OK with the changes. He has **unpushed work in the Codex app, location unknown**, which gates the
  mass rename (steps 2–4). Mitigation is step 2a: a re-runnable rename script he applies to his branch.
- **Sample flight numbers:** anything. Switching them to `S4xxx`.
- **`apps/ops-web`:** archive to `archive/ops-web`, don't delete.

## Still open

1. Is Reece OK with the free hybrid, including a one-minute cold start on `app.` if keep-warm misses, and
   Render's "not for production" caveat?
2. Who does the Cloudflare dashboard work: Dan (owner) or Reece added as a member? Adding Reece as a member
   avoids sharing credentials.
3. Dan: push his Codex work to a branch on `origin` (e.g. `dan/wip`) and say where it is.
4. Untracked Neon leftovers at the repo root (`package.json`, `package-lock.json` changes, `neon.ts`,
   `skills-lock.json`, `.claude/skills/neon*`, `.gitignore` additions): keep them uncommitted, or move them to
   `C:\Dev\_scratch`?

## Phase 0 output

- `CLAUDE.md` rewritten. It now covers: both solutions, all four test suites and how to start the local test DB,
  the baseline, the S4 naming, hosting (interim versus target), and collaboration rules. Two stale claims were
  removed: "no external NuGet dependencies" (the server uses EF/Npgsql/OIDC) and "tests = one project".
- `.claude/settings.json` (project, shareable):
  - **Allow:** build, test executables, read-only git, `wrangler dev/types/whoami`.
  - **Ask:** push, merge, rebase, reset, `git mv`, rm, every other `wrangler` command, Render deploy/env,
    Neon SQL.
  - **Deny:** reading `.env*`, `.dev.vars`, `*.pem`, `*.pfx`.
  - The existing `settings.local.json` still applies on top.
- `.claude/commands/`: `/verify`, `/rebrand-audit`, `/ui-check`, `/handoff`.
- **Hook:** the prompt asked for an `apps/ops-web` formatter hook, but that app is retired, so I didn't write
  one. The proposed alternative is below and hasn't been added.

### claude-code-setup recommender summary (nothing installed)

| Type | Recommendation | Why |
|---|---|---|
| MCP | **Cloudflare MCP servers** (not connected today) | For current docs, bindings, Containers and observability during Phases 4–5. Either `/plugin marketplace add cloudflare/skills` (bundles the servers and skills), or individually: `claude mcp add --transport http cloudflare-docs https://docs.mcp.cloudflare.com/mcp`, `... cloudflare-bindings https://bindings.mcp.cloudflare.com/mcp`, `... cloudflare-containers https://containers.mcp.cloudflare.com/mcp`, `... cloudflare-observability https://observability.mcp.cloudflare.com/mcp` |
| MCP | **Fix the GitHub plugin MCP** | It fails to connect ("Authorization header is badly formatted"), so its token needs re-entering. `gh` works in the meantime. |
| Hook | **PreToolUse guard on applied migrations**: block Edit/Write to `src/Alpha6Ops.Accounts/Migrations/2026*.cs` unless the file is new | The rename touches about 220 files, and applied migrations must stay as they are (see the rebrand plan's allowlist) |
| Hook | **Fix or disable the Render plugin's `validate-render-yaml-hook.sh`** | It breaks on the space in the user path and throws a "syntax error" on every Write/Edit. This is noise, not a real failure. |
| Skill | `create-migration` (user-only): add an EF migration, update the snapshot, and run the accounts parity checks against the local DB | Migrations are hand-shaped here (raw SQL, parity test), and Phase 4 may add more |
| Subagent | Already covered by installed plugins: `pr-review-toolkit` (code-reviewer, silent-failure-hunter, pr-test-analyzer) and `claude-security` | No custom agents are needed yet |
| Plugins | Consider disabling `finance` and `data` in this project | They add about 20 irrelevant skills to every session |

## Phase 1 output

- `docs/s4-migration/hosting-options.md`: the Cloudflare map (what exists, and what each piece needs), options
  A/B/C, database options, costs, and the recommendation.
- `docs/s4-migration/rebrand-plan.md`: target names, 10 ordered steps with their risks, the desktop
  data-location migration design, hosted identifiers changed only at cutover, the "stays unchanged" allowlist,
  and the manual steps for Reece and Dan.
- Inventory facts that shaped the plan:
  - The persisted identifiers are: the data root, the two SQLite names, the `Alpha6OPS-` file prefix and indexer
    glob, the installer uninstall key/marker/dir/exe names, `alpha6-identity.json`, the `Alpha6OPS/`
    User-Agent the server parses, cookies, `SetApplicationName`, the Auth0 audience and claim namespace, and the
    `alpha6_*` SQL functions.
  - The installer is custom (no MSI UpgradeCode). There is no AppUserModelID, URI scheme or DPAPI entropy.
  - The log catalog stores absolute paths, so the migration rewrites them.

## Dan's 7.26B merge — 2026-10-02

Branch `merge/dan-7.26b` (worktree `C:\Dev\_scratch\s4-merge`, off `chore/s4-rebrand`), merge commit `230ca85`.
Not pushed. Dan's handoff doc: `docs/SIERRA4-PROJECT-CONTINUITY.md`.

- Dan's flight tracking, recovery, storage (`%LOCALAPPDATA%\Sierra 4`), Map V2 and `SierraShell` are taken as-is.
- The account layer was rebuilt on his `FlightSessionCoordinator`. `App` keeps one coordinator per account workspace
  (`Data\accounts\<id>\{personal|airlines\<id>}`) and swaps it on an account change, which is refused mid-flight.
- Settings, preferences, the SimBrief cache and diagnostics live in the shared profile. The SimBrief username follows
  the account profile.
- Dropped: the old pilot-tier dashboard layout tweaks (their panels no longer exist). The tier is now a shell
  navigation filter (`PilotFeatureProfile.Allows(ShellSection)`).
- Verified: both solutions 0 warnings. Core 194, desktop identity 102, accounts 180, server 111. Desktop smoke all pass
  (dashboard 565, identity UI 65). Recovery 21, retention 8 (+1 new), storage 9, logbook 13, terminal 203,
  active restart 74. Map V2: mailbox 7, browser 7, in-app 208. Not run: `verify-migration-baseline.ps1` (needs
  Dan's nine 7.17 journals).
- Dan's `scripts/verify-*.ps1` fail under Windows PowerShell 5.1 (`*>` on native stderr with `Stop`). Run the
  underlying commands from Bash, or use pwsh 7.

**For Dan (protected areas, his sign-off):**
1. The coordinator takes an optional account identity and writes an `account_identity` history event when a row
   begins. The row identity is unchanged.
2. Retention also protects OFPs referenced by `Data\accounts\**\active-flight.json` (it only adds files to the keep set).
   New check: `ACCOUNT_WORKSPACE_ACTIVE_OFP_SURVIVES_QUOTA`.
3. Also: `verify-desktop.ps1` timeout went from 60s to 120s, because the full smoke run takes about 70s here.

## Next steps

- [x] Code for `app.s4ops.com` behind Cloudflare (server suite 99/99).
- [ ] Deploy `chore/s4-rebrand` (or merge to `accounts`) to Render, then follow `docs/hosting.md` "Custom domain through Cloudflare".
- [x] Phase 3 direction: **C Glass cockpit** (Reece, 2026-09-22). Canvas: https://claude.ai/artifact/Lc9NbwDTvtofwxAdMhUrMH
- [x] Portal design system + restyle of all 15 pages (commit 7f8b159). Verified with Playwright at 1440/390 px in both
      themes (screenshots in `work/ui-check/`, snapshot via `S4OPS_PORTAL_SNAPSHOT_DIR`). All suites green: 85/102/160/99.
- [x] Logo + favicons (commit 159230c): `tools/brand/build_logo.py` regenerates every asset from vendored OFL fonts.
      Broken snapshot images were a test-fixture fault, fixed in 09d057d (production uploads were fine).
- [x] Merged look (728590e): Michroma headings, brand yellow for navigation and markers only, cyan actions,
      night caution moved to orange-amber. Airline console uses a sidebar (`_ConsoleLayout`).
- [x] Landing (a9842b0): night-airfield hero and an OOOI (OUT/OFF/ON/IN) timeline; copy no longer claims cruise
      or approach detection.
- [x] Trust basics (6e7eb91), roadmap Phase D: /Privacy, /Terms, data export ZIP, self-service deletion with a
      14-day grace period and hourly purge. **Before launch:** fill `[CONTACT EMAIL]` and `[STATE]` in
      `src/Alpha6Ops.Server/Legal.cs`; create the Auth0 M2M app (delete:users) per docs/hosting.md, or remove
      sign-ins by hand. Suites: 85 / 102 / 180 / 110.
- [x] Full-width landing redesign and site shell (ec7b4f9): night-airfield hero (`Pages/Home/HeroScene.cs`), demo
      operations board, OOOI timeline, waypoint "how it works", pilots/airlines split, desktop flow, security, final
      call. Content lives in `Pages/Home/HomeContent.cs`, one partial per section, styles in `wwwroot/css/home.css`.
      Yellow is the primary action. Phone nav is a `<details>` menu. Long IDs/emails now wrap. Suites 85/102/180/111.
- [ ] Ask Dan: the "Diverted" status is shown on the site but the engine has no diversion detection yet.
- [ ] Known small issues: emails are stored and shown upper-case (`AccountRules.Email` normalises); inline
      `style=` attributes on the profile form are blocked by the CSP.
- [ ] Phase 3 remaining: replace the desktop `alpha6-mark.png` / `alpha6.ico` from `s4-tile-512.png`; the static landing page for
      `s4ops.com` on Workers static assets (reuse `Index.cshtml` output + `site.css`); carry the tokens into the WPF
      resource dictionaries; delete the now-unused photos in `wwwroot/images/*.jpg|png` (kept for now).
- [ ] Push `chore/s4-rebrand` (Reece's go-ahead needed) so Render deploys the new look + Cloudflare fix.
- [ ] Phase 2: cut `chore/s4-rebrand` from `accounts`; do 2a (rename script), 5a, 6–8 now; steps 2–4 after Dan pushes.
