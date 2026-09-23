---
description: Run the portal locally and capture Playwright screenshots at desktop and mobile widths
---

1. Build: `dotnet build Alpha6Ops.Cloud.slnx -nologo -v q` (or `S4Ops.Cloud.slnx` after the rename).
2. Start the local test PostgreSQL the same way `/verify` does, if the pages under test need an account store.
3. Start the portal in the background:
   `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Alpha6Ops.Server --no-build --urls https://localhost:7246`.
   Wait until `https://localhost:7246/healthz` answers, and report what it returns (`ok` or `unconfigured`).
4. With the Playwright MCP, visit each anonymous page: `/`, `/Levels`, `/Download`, `/Support`, `/SignedOut`.
   Also visit any paths given in $ARGUMENTS. Take full-page screenshots at 1440×900 and 390×844, in light and
   dark (`browser_emulate_media` colorScheme). Save them under `work/ui-check/<yyyyMMdd-HHmmss>/`.
5. Check the console for errors and confirm that no page loads a script, since CSP is `script-src 'none'`.
6. Stop the portal, and the database if you started it. Summarize anything visually broken or overflowing
   at 390px.
