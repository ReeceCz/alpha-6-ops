# S4 OPS (formerly Alpha 6 OPS)

MSFS 2024 virtual-airline operations platform by **Sierra 4**. Tagline: "You fly the airplane. We'll run the
airline." Two clients: the **WPF desktop pilot app** (SimConnect tracking, SimBrief import, local journals) and
the **hosted account portal** (`Alpha6Ops.Server`: Razor pages + `/api/v1`, Auth0 sign-in, PostgreSQL).

**Rename in progress (decided 2026-09-22):** "Alpha 6" conflicts with real-world brands. The product is now
**S4 OPS**, the code identifier becomes `S4Ops`, and the demo tenant becomes `s4`. Until Phase 2 of
`docs/s4-migration/rebrand-plan.md` lands, the code still says `Alpha6Ops`. New text should say S4 OPS. Don't
rename persisted identifiers (data paths, cookies, installer keys, Auth0 audience) outside that plan.
Handoff log: `docs/s4-migration/PROGRESS.md`. Read it first when resuming.

Windows-only. WPF and SimConnect do not build under WSL or Linux.

## Commands

Run from the repository root.

- Build desktop/domain: `dotnet build Alpha6Ops.slnx`
- Build server/accounts: `dotnet build Alpha6Ops.Cloud.slnx`
- Tests are executables (see below). Suites:
  - `dotnet run --project tests/Alpha6Ops.Tests --no-build` (core engine)
  - `dotnet run --project tests/Alpha6Ops.Desktop.Identity.Tests --no-build`
  - `dotnet run --project tests/Alpha6Ops.Accounts.Tests --no-build` (DB part needs `ALPHA6_TEST_DATABASE`)
  - `dotnet run --project tests/Alpha6Ops.Server.Tests --no-build` (**fails** without `ALPHA6_SERVER_TEST_DATABASE`)
- One-shot identity suites plus desktop launch: `powershell -File scripts/test-identity-local.ps1`
- Portal locally: `$env:ASPNETCORE_ENVIRONMENT='Development'; dotnet run --project src/Alpha6Ops.Server --no-build --urls https://localhost:7246`
- Console pilot: `dotnet run --project src/Alpha6Ops.Pilot --no-build -- samples/delayed-flight.jsonl`

Local test database: PostgreSQL 17 cluster in `work/identity-postgres` on `127.0.0.1:55439`, user
`alpha6_test`, DBs `alpha6_identity_test` / `alpha6_server_test`. Start it with `pg_ctl` from **Bash** with
output redirected to a file (`... -w start >file 2>&1 </dev/null`). In PowerShell, both `& pg_ctl` and
`Start-Process -Wait` hang, because the postgres child keeps the pipe open. Stop it afterwards.

Baseline 2026-09-22 (`accounts`): both solutions build with 0 warnings. Core 85, desktop identity 102,
accounts 160 (with DB) and server 94 (with DB) checks, all passing.

## Critical: tests are not `dotnet test`

**`dotnet test` does not work in this repository and never will.** Each test project is a console executable
that exits nonzero on failure. Run it with `dotnet run --project <path> --no-build`.

## Toolchain

- .NET 10 SDK (`global.json`, `rollForward: latestFeature`). `TreatWarningsAsErrors` is on.
- NuGet: domain/replay/desktop projects are dependency-free. Identity/Accounts/Server use OIDC, EF Core +
  Npgsql, and `Microsoft.AspNetCore.Mvc.Testing` in tests. **Ask before adding any package.**
- Node is used only by `src/Alpha6Ops.Server/auth0` action tests. `apps/ops-web` uses pnpm 11.19.0 with
  `--frozen-lockfile`; do not regenerate its lockfile.
- The root `package.json` / `neon.ts` / `skills-lock.json` are untracked Neon tooling leftovers, not part of the build.

## Layout

| Path | Owns |
|---|---|
| `src/Alpha6Ops.Core` | Domain records, phase detection, rotation engine, contracts |
| `src/Alpha6Ops.Desktop` | WPF pilot app, tray, local SQLite, desktop account UI (`MainWindow.Account.cs`, `Account*`) |
| `src/Alpha6Ops.FlightLab` | Flight Lab test harness (named pipe to the desktop) |
| `src/Alpha6Ops.Pilot` | Console replay host |
| `src/Alpha6Ops.Server` | Hosted portal + `/api/v1`, Dockerfile, `entrypoint.sh`, `auth0/` post-login actions |
| `src/Alpha6Ops.Identity` / `src/Alpha6Ops.Accounts` | Identity plumbing; PostgreSQL account store and migrations |
| `src/Alpha6Ops.Api` | Old local read-only demo API, Development-only, tenant `alpha6` |
| `apps/ops-web` | React dashboard: **retired**, no new work |
| `tests/*` | Executable suites (see Commands) |
| `packaging` | Custom desktop installer (`PreviewSetup.cs`) and `build-desktop.ps1` |
| `docs` | `web-roadmap.md` (web/accounts pick-up point), `hosting.md`, `identity-foundation.md`, `billing-roadmap.md`, `s4-migration/` |

## Hosting

- **Today (interim):** Render free web service `alpha6ops` (`render.yaml`, Docker) + Neon PostgreSQL + Auth0
  dev tenant. Live at `alpha6ops.onrender.com`. `.github/workflows/keep-warm.yml` pings it.
- **Target:** Cloudflare, zone `s4ops.com` (Cloudflare nameservers; no records yet as of 2026-09-22). The
  runtime and database choice is recorded in `docs/s4-migration/PROGRESS.md`. No `wrangler` config exists yet.
- Render and Neon keep serving until cutover. Never change or delete them without asking.

## Machine-specific configuration

- `simconnect-sdk-path.txt` (gitignored) points at the MSFS SDK. `work/` holds local SDKs, test databases and
  run artifacts. Neither is portable.
- `src/Alpha6Ops.Desktop/alpha6-identity.json` (gitignored) holds the desktop Auth0 client config.

## Not in source control

`work/`, `**/bin/`, `**/obj/`, `**/node_modules/`, `**/dist/`, `.env*`, `.neon`, `*.user`, `*.log`, `*.dmp`,
`data/`, `simconnect-sdk-path.txt`, `alpha6-identity.json`, and installer/zip artifacts under `outputs/`. The
README links to `outputs/Alpha6OPS-Setup-*.exe`, but that file doesn't exist in a clone.

## Gotchas

- `FlightSession` applies one replay to the first leg only. Each request creates a fresh session.
- Block-in detection defaults to parking brake + engines off + near-zero groundspeed. It's configurable per
  aircraft via `AircraftGroundProfiles.ForFamily`, but every family resolves to the default. Don't invent numbers.
- Simple / Advanced / OCC are presentation modes, not authorization. Real authorization is per-airline access
  levels on the server (Read / Operations / Branding / Admin; Admin requires step-up MFA).
- The demo API refuses non-Development and exposes only tenant `alpha6`. Deliberate; never expose it.
- Sample dates are fixed to 2 September 2026, UTC. `--simconnect` on the console pilot exits 2 by design.

## Collaboration and boundaries

- `main` is Dan's (GitHub `Alphatango2`, remote `origin`, push disabled). Reece works on `accounts` in the
  fork (`fork` = `ReeceCz/alpha-6-ops`). Never commit to or push `main`.
- **Never push, deploy (`wrangler deploy`, Render), change DNS, or edit Cloudflare/Auth0/Render/Neon/GitHub
  settings without an explicit go-ahead.** Give Reece the steps instead.
- `packaging` and the SimConnect adapter need coordination with Dan before changes.
- Don't commit anything under `outputs/`. Don't weaken the server's environment, host or tenant checks.
- Conventional commit messages (`feat:`, `fix:`, `chore(rebrand):`, `docs:`), one logical change each.
