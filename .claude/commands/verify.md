---
description: Build both solutions and run every test executable, including the DB-backed suites
---

Run the full verification pass from the repository root and report a table of suite → PASS count → FAIL count →
exit code. Stop and show the failing output if anything fails. Never use `dotnet test`.

1. `dotnet build Alpha6Ops.slnx -nologo -v q` and `dotnet build Alpha6Ops.Cloud.slnx -nologo -v q`
   (after the rename: `S4Ops.slnx` / `S4Ops.Cloud.slnx`; use whichever exist).
2. Start the local test PostgreSQL from **Bash**, with output redirected so the shell doesn't hang:
   `"/c/Program Files/PostgreSQL/17/bin/pg_ctl.exe" -D "$(cygpath -w "$PWD/work/identity-postgres")" -l "<data>\server.log" -o "-h 127.0.0.1 -p 55439" -w start >"<data>\start.out" 2>&1 </dev/null`
   Skip this step if `pg_ctl status` says it is already running, and remember whether you started it.
3. With `ALPHA6_TEST_DATABASE='Host=127.0.0.1;Port=55439;Username=alpha6_test;Database=alpha6_identity_test'`
   and `ALPHA6_SERVER_TEST_DATABASE='Host=127.0.0.1;Port=55439;Username=alpha6_test;Database=alpha6_server_test'`
   exported, run each project under `tests/` whose csproj is an Exe:
   `dotnet run --project tests/<name> --no-build > "$TEMP/<name>.log" 2>&1`, then count `^PASS` / `^FAIL` lines.
4. If a `wrangler.jsonc`/`wrangler.toml` exists, also run `npx wrangler types` (or the project's configured
   typecheck) and report it. Do not run `wrangler deploy`.
5. Stop the database with `pg_ctl ... -m fast -w stop`, but only if you started it.

Compare against the baseline in CLAUDE.md and call out any drop in check counts.
