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
| 2026-10-13 | Everything stays local (no push, deploy, release) until Dan's final approval | Reece |
| 2026-10-13 | Testers launch on the existing Auth0 dev tenant; revisit before opening widely | Reece |
| 2026-10-13 | Emails on from day one via Resend; public contact `support@s4ops.com` (Cloudflare Email Routing) | Reece |
| 2026-10-13 | Rotate the Neon password and Render deploy hook now, before Dan's approval. **Done 13 Oct:** `neondb_owner` reset, Render redeployed 18:56 UTC, sign-in verified, hook regenerated | Reece |

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

**2026-10-19 session (`feat/launch-prep`, local commits, not pushed).** Suites: core 194, desktop identity 107,
accounts 292, server 286, all green; both solutions 0 warnings.
- [x] Staff-only **preview builds** (97c0007): `/Staff/Builds` lists a private R2 bucket and redirects to 5-minute
      SigV4 links (no new package). `scripts/upload-preview-build.ps1` builds through `publish-release.ps1`'s dry run
      and uploads with wrangler. A local 0.23.0 build passed the hosted-config check. **Reece:** create bucket
      `s4ops-builds` + a read-only R2 token, set `PreviewBuilds__AccountId/Bucket/AccessKeyId/SecretAccessKey` on Render,
      push, then upload with `-SimConnectDll` pointing at `work\msfs2024-sdk-1.7.3\SimConnect SDK\lib\SimConnect.dll`.
- [x] Security review of everything since the 7.26B merge (feedback uploads, staff area, request form, mailer,
      invitations, crew/settings split, deletion purge): no confirmed issues.
- [x] Phase 5 journey test (4d9e444): dispatcher, pilot, non-member and pending-deletion member through the console
      pages. Phone-width screenshots in `work/ui-check/20261019-110624/`: no overflow, no scripts, no console errors.
      Snapshot mode trips the shared-IP rate limit in two rate checks; normal runs pass.
- [x] For Dan's 7.27 UI (4c8e80d, 5ed45fb): `ListRoutesAsync`/`ListFleetAsync` on the desktop session and
      `AirlineSchedule.Between` for dated legs; `7.27-account-data.md` maps his mockup panels. A separate
      airline-context endpoint was dropped as a duplicate of existing member reads.
- Decision: no desktop UI work until Dan pushes 7.27.

**2026-10-13 session (`feat/launch-prep`, worktree `C:\Dev\_scratch\s4-merge`, local commits only, not pushed).**
Reece: everything stays local until Dan's final approval. Plan: `PLAN-to-invites.md`. Suites: core 194, desktop identity
102, accounts 292, server 237, landing 15, all green; both solutions 0 warnings.
- [x] `publish-release.ps1` builds with `alpha6-identity.hosted.json` (gitignored, created locally) and refuses a payload
      that doesn't point at `https://app.s4ops.com/`. Local dry-run installer built; localhost payload rejected.
- [x] Airline console: Overview (counts + setup steps, any member), Crew and Settings split, invitations accepted from
      Join without the code, invitation emails carry a link only and go only to addresses that may sign in.
- [x] CI workflow (`.github/workflows/ci.yml`) written, **never run** (no Docker/WSL here); keep-warm fails on a bad `/healthz`.
- [x] Security review of the launch-prep server diff: no findings at confidence 8+; the one low item (admin-controlled
      invitation emails) is fixed in 7257668.
- [x] Draft for Dan: `flight-upload-contract-draft.md` (history row ID as the upload identity).
- [x] Correction: the plan's "duplicate flight on retry" was wrong; `409 flight_exists` already prevents it.
- [x] Rotation done (Neon `neondb_owner` password, Render deploy hook). Auth0 checked: password + passkeys + Google,
      no passwordless, default email templates/provider and login page (brand them in the launch Auth0 step).
- [x] Full local test pass (34 portal pages + landing, phone and desktop, links, form handlers). Fixed: logbook
      block time showed raw code; "Shared dispatch board" and "Weather briefings" claims removed; "Microsoft" sign-in
      wording removed (Google only); level names consistent; staff report buttons.
- [x] Error pages: a reference to quote and links out; 404 / expired-form / forbidden pages instead of blank errors.
      Locally, a stale database names `--migrate-accounts` (run it after pulling new migrations).
- [x] Signed-in but uninvited pilots ask for an invitation in one click from the account page; signing in again
      while it waits says so. Privacy updated (13 October) to match the site.
- [x] Schedule, fleet and logbook are compact cards on phones.
- Suites after this: accounts 292, server 257, all green.
- [x] **Website live (Reece's call, before Dan's sign-off).** `feat/launch-prep` pushed to the fork and `accounts`;
      Render deployed it with the six migrations. https://app.s4ops.com: Render custom domain, Cloudflare CNAME
      proxied, Full (strict), Always HTTPS; Auth0 Portal URLs added; Resend verified for s4ops.com; Email Routing
      `support@s4ops.com`; Render `Staff__Emails`, `Legal__Contact`, `Email__ResendApiKey` set. Cloudflare Email
      Address Obfuscation turned off (it broke the contact link under the portal's no-script CSP). First CI run green.
      Reece tested sign-in, staff MFA, tester invitation email and the request flow on the live site.
- [x] Staff security check: returns to the staff page; a stale check keeps the invitation, reply or release notes
      instead of losing them, and nothing is sent first.
- [x] `DAN-BRIEF.md` for the next meeting with Dan.
- [x] Landing redeployed (`s4ops-landing`, version be2ba9ef; deploy with `CLOUDFLARE_ACCOUNT_ID` set to Dan's
      account, which holds the zone): HSTS on, "Ask for an invitation" goes to `/Request`. GitHub variable
      `ALPHA6_HEALTH_URL` = `https://app.s4ops.com/healthz`. `check-live.ps1`: all checks pass.
- Still open: Auth0 email/login branding, an outside uptime alert, a backup restore drill. The installer waits for Dan.
- Local run: `pg_ctl ... -o "-h 127.0.0.1 -p 55439"` (the cluster's config has no port), then the portal in
  Development on https://localhost:7246 with real Auth0 sign-in and the `alpha6_server_dev` database.
- Main checkout `devlpha-6-ops` (`chore/s4-rebrand`) is 81 commits behind and has stale uncommitted edits; not touched.

**2026-10-04/05 session (branch `feat/launch-prep`, worktree `C:\Dev\_scratch\s4-merge`, not pushed).** Reece's manual
steps, in order, are in **`docs/s4-migration/LAUNCH-CHECKLIST.md`**. Suites: core 194, desktop identity 102, accounts
282, server 225, landing 15, all green; both solutions 0 warnings.
- [x] s4ops.com is live on Cloudflare (Worker `s4ops-landing`). `app.s4ops.com` has **no DNS record yet**, so every
      account link on it is dead until checklist step 3.
- [x] Landing: HSTS, phone menu, CSP allows Cloudflare Web Analytics, "Ask for an invitation" → `/Support#invitations`.
- [x] `Legal__Contact` / `Legal__Jurisdiction` settings (default Wisconsin). No placeholder or `mailto:[CONTACT EMAIL]`
      is shown while the contact is unset.
- [x] Emails stored lower-case (migration `202610040001_LowercaseEmails`).
- [x] CSP-blocked inline styles removed (the `.upper` class; the validation summary renders only on errors).
- [x] Levels/plan pages: unbuilt paid features marked "(coming soon)".
- [x] `scripts/publish-release.ps1` (GitHub Releases on the public fork) and `scripts/check-live.ps1`.
- [x] Report a problem: `/Account/Feedback`, staff inbox `/Staff/Feedback` (migration `202610040002_Feedback`). Spec
      and plan are in `docs/superpowers/`. Staff = `Staff__Emails` + verified email + recent MFA; others get 404.
- [x] Testers invited from `/Staff/Testers` (migration `202610040003_TesterInvites`), alongside
      `Accounts__InvitedEmails`. A new tester no longer needs a Render change.
- [x] Airline console tabs fit at phone width.
- [x] Staff overview `/Staff`: tester funnel, desktop versions in use (flags versions behind the release), open
      reports, pending requests.
- [x] Release notes (migration `202610050001_ReleaseNotes`): staff write them at `/Staff/Releases`. Public
      `/Releases`, notes on `/Download`, and "Update available" on the account page for older desktops.
- [x] Invitation requests (migration `202610050002_InviteRequests`): public `/Request`. The answer is the same for
      known and unknown emails. Honeypot plus 3 per address per hour, and optional Turnstile (`Turnstile__*`). Staff
      Invite or Decline on `/Staff/Testers`. Unanswered requests are deleted after 90 days by the hourly job.
- [x] Getting-started card on `/Account` and "Your flying" insights on `/Account/Logbook` (CSS-only 12-month chart).
- [x] Fixed: web visits stamped `LastSeenAt` as if the desktop had signed in. Only the API (desktop) bootstrap stamps it now.
- [x] Tester guide `/Guide` (public; the first-flight steps are a shared partial with Download). The sign-in copy no
      longer promises passkeys or social login (Auth0 notes say WebAuthn isn't on this plan).
- [x] Replies on problem reports (migration `202610050003_FeedbackReplies`): staff reply, pilots see "Sierra 4 team",
      and the account page shows an unread notice.
- [x] Email via Resend (`Email__ResendApiKey`; off and only logged without it): tester invited, reply to your
      report, new report to staff, new request to staff. Background outbox, links only. Privacy names Resend.
- [x] Fixed copy: the logbook claimed desktop flights arrive automatically. They don't yet (the desktop never calls
      `POST /me/flights`); this is the top item for Dan.
- Deferred minors: an unknown non-numeric feedback category binds to the default; the staff inbox shows the newest
  500 reports only; ZIP attachments are checked by signature only.
- [x] s4ops.com landing site built in `sites/s4ops.com` (simulated traffic-map hero, charcoal theme). Spec
      `docs/superpowers/specs/2026-10-02-s4ops-landing-design.md`.
- [x] Reece: deploy s4ops.com per `sites/s4ops.com/README.md`. Done; redeploy for the 2026-10-04 landing fixes.
- [x] Simplify the portal home to a short script-free entrance linking to s4ops.com.
- [x] Portal restyled to charcoal Sierra tokens (dark only, fluid scale); marketing removed (spec
      `docs/superpowers/specs/2026-10-03-portal-simplify-design.md`). Levels page copy still needs a fact-check (see session notes).

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
- [x] Known small issues (fixed 2026-10-04): emails are stored and shown upper-case (`AccountRules.Email` normalises); inline
      `style=` attributes on the profile form are blocked by the CSP.
- [ ] Phase 3 remaining: replace the desktop `alpha6-mark.png` / `alpha6.ico` from `s4-tile-512.png`; the static landing page for
      `s4ops.com` on Workers static assets (reuse `Index.cshtml` output + `site.css`); carry the tokens into the WPF
      resource dictionaries; delete the now-unused photos in `wwwroot/images/*.jpg|png` (kept for now).
- [ ] Push `chore/s4-rebrand` (Reece's go-ahead needed) so Render deploys the new look + Cloudflare fix.
- [ ] Phase 2: cut `chore/s4-rebrand` from `accounts`; do 2a (rename script), 5a, 6–8 now; steps 2–4 after Dan pushes.
