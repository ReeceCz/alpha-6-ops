# Portal Simplification and Charcoal Restyle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the portal's home into a short script-free entrance, remove the duplicated marketing, and restyle every remaining portal page onto the charcoal Sierra tokens shared with `s4ops.com` and the desktop app.

**Architecture:** Razor Pages in `src/Alpha6Ops.Server`.
- `/` gets a page model that redirects signed-in users and renders a small entrance for everyone else.
- The restyle is done in `wwwroot/css/site.css`: the design tokens are repointed, the light theme is removed, sizes move to a fluid `rem` scale, and the header and footer in `_Layout.cshtml` are rebuilt to match `s4ops.com`.
- Nothing functional changes.

**Tech Stack:** ASP.NET Core Razor Pages (.NET 10), CSS, the executable server test suite (`tests/Alpha6Ops.Server.Tests`), Playwright MCP for visual checks.

**Spec:** `docs/superpowers/specs/2026-10-03-portal-simplify-design.md`

## Global Constraints

- No JavaScript and no inline `style=` anywhere in the portal (`script-src 'none'`). Never weaken the server's security headers or host, tenant and environment checks.
- Tokens:

  | Token | Value |
  |---|---|
  | `--surface` | `#090c0f` |
  | `--surface-raised` | `#101418` |
  | `--surface-sunken` | `#151a1f` |
  | `--line` | `#293138` |
  | `--line-strong` | `#3a434b` |
  | `--text` | `#f2f4f5` |
  | `--text-2` | `#bbc2c8` |
  | `--muted` | `#8e99a2` |
  | `--brand`, `--primary` | `#f4cf22` |
  | `--action` (links) | `#86bde8` |

- Dark only: no `prefers-color-scheme: light` rules remain.
- Display font `"Bahnschrift SemiCondensed", "Barlow Semi Condensed"`. Fluid base: `html { font-size: clamp(100%, calc(0.3125vw + 12px), 137.5%) }`.
- No "RYNEL". Status colour always comes with an icon and words, as now.
- Tests are executables: `dotnet run --project tests/Alpha6Ops.Server.Tests --no-build` with `ALPHA6_SERVER_TEST_DATABASE` set (local PostgreSQL on 55439, started from Bash). The accounts suite likewise.
- Commit messages are conventional and end with the session's attribution lines.

## Review Focus

- A signed-in user on `/` with a stale or expired cookie: the redirect goes to `/Account`, where the normal sign-in challenge applies. There is no loop.
- Account pages with long airline names, callsigns or e-mails at 390px: they still wrap (the earlier long-ID fix), with no sideways scroll.
- Status badges (on time, delayed, warning) on the darker surfaces: still legible at 4.5:1, and still carrying an icon and words.
- The airline console sidebar (`_ConsoleLayout`) at 1100 to 1280px: no overlap between the sidebar and the tables after the rem conversion.
- Forms (profile, schedule, fleet, delete confirmation): inputs and buttons keep visible focus outlines on charcoal.

---

### Task 1: Home becomes an entrance; marketing removed

**Files:**
- Create: `src/Alpha6Ops.Server/Pages/Index.cshtml.cs`
- Modify: `src/Alpha6Ops.Server/Pages/Index.cshtml`, `src/Alpha6Ops.Server/wwwroot/css/site.css` (append `.entrance` rules), `tests/Alpha6Ops.Server.Tests/Program.cs`
- Delete: `src/Alpha6Ops.Server/Pages/Home/` (all files), `src/Alpha6Ops.Server/wwwroot/css/home.css`, `src/Alpha6Ops.Server/wwwroot/images/hero/`,
  `wwwroot/images/{flight-deck.png,globe.jpg,pilot.jpg,routes.jpg,runway.jpg}`, `tools/hero/`

**Interfaces:**
- Produces: the `IndexModel` page model; CSS classes `.entrance`, `.entrance-card`, `.entrance-actions`, `.entrance-note`.

- [ ] **Step 1: Rewrite the unconfigured landing check and add the new checks (failing)**

In `tests/Alpha6Ops.Server.Tests/Program.cs`, replace the existing landing check that begins
`Check(landingHtml.Contains("How it works") && landingHtml.Contains("Every phase recorded automatically")` and the following
`Check(!landingHtml.Contains("<script") ...` with:

```csharp
    Check(landingHtml.Contains("Your S4 OPS account") && landingHtml.Contains("https://s4ops.com/") && !landingHtml.Contains("/auth/signup")
        && !landingHtml.Contains("How it works") && !landingHtml.Contains("Demonstration data") && !landingHtml.Contains("home.css"),
        "Unconfigured entrance points to s4ops.com, carries no marketing and never offers sign-up");
    Check(!landingHtml.Contains("<script") && !Regex.IsMatch(landingHtml, @"\sstyle="""), "Entrance is script-free with no inline styles the CSP would block");
```

Directly after the existing line `var accountPage = await browser.GetAsync("/Account");`, add:

```csharp
var signedInHome = await browser.GetAsync("/");
Check(signedInHome.StatusCode == HttpStatusCode.Redirect && signedInHome.Headers.Location?.OriginalString == "/Account", "Signed-in home goes straight to the account");
var entrance = await anonymous.GetStringAsync("/");
Check(entrance.Contains("/auth/login") && entrance.Contains("/auth/signup") && entrance.Contains("https://s4ops.com/") && entrance.Contains("invite-only")
    && !entrance.Contains("Every phase recorded") && !entrance.Contains("home.css"), "Signed-out home is a short entrance: sign in, invited sign-up, s4ops.com, no marketing");
foreach (var gone in new[] { "/css/home.css", "/images/pilot.jpg", "/images/hero/kmke-plan.svg" })
    Check((await anonymous.GetAsync(gone)).StatusCode == HttpStatusCode.NotFound, $"Retired asset {gone} is gone");
```

- [ ] **Step 2: Build and run, and confirm the new checks fail**

Run: `dotnet build Alpha6Ops.Cloud.slnx -nologo -v q`, then the server suite with the DB variable set.
Expected: FAIL lines for "Unconfigured entrance…", "Signed-in home goes straight…", "Signed-out home is a short entrance…" and the three "Retired asset…" checks. Every other check passes.

- [ ] **Step 3: Write the page model**

```csharp
// src/Alpha6Ops.Server/Pages/Index.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Alpha6Ops.Server.Pages;

// The portal's front page is an entrance: signed-in people go straight to their account; the product story lives on s4ops.com.
public sealed class IndexModel : PageModel
{
    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true ? Redirect("/Account") : Page();
}
```

- [ ] **Step 4: Rewrite `Index.cshtml`**

```cshtml
@page
@model Alpha6Ops.Server.Pages.IndexModel
@inject ServerSettings Settings
@{ ViewData["Title"] = "Your S4 OPS account"; ViewData["BodyClass"] = "entrance-page"; }
@* An entrance, not a brochure: the product story is on s4ops.com. Signed-in visitors never see this (IndexModel redirects). *@
<section class="entrance" aria-labelledby="entrance-title">
    <div class="entrance-card">
        <img src="/images/brand/s4-tile.svg" alt="" width="56" height="56" />
        <p class="eyebrow">S4 OPS</p>
        <h1 id="entrance-title">Your S4 OPS account</h1>
        <p class="lead">Sign in to manage your pilot profile, your logbook and the airlines you fly for.</p>
        @if (Settings.IsConfigured) {
            <div class="entrance-actions">
                <a class="button" href="/auth/login">Sign in</a>
                <a class="button secondary" href="/auth/signup">Create your invited account</a>
            </div>
            <p class="entrance-note">S4 OPS is invite-only while we test with a small group of pilots. Create your account with the email address you were invited with, or <a href="mailto:@Legal.Contact?subject=S4%20OPS%20invitation">ask us for an invitation</a>.</p>
        } else {
            <p class="notice" role="status">Account registration is not available on this installation yet.</p>
        }
        <p class="entrance-more">What is S4 OPS? <a href="https://s4ops.com/">See s4ops.com</a>.</p>
    </div>
</section>
```

- [ ] **Step 5: Add the entrance styles** (append to `site.css`)

```css
/* Entrance (home): one centred card on charcoal */
.entrance { min-height: calc(100vh - 12rem); display: grid; place-items: center; padding: var(--space-8) var(--gutter); }
.entrance-card { width: min(100%, 36rem); text-align: left; }
.entrance-card img { display: block; margin-bottom: var(--space-5); }
.entrance-card h1 { font: 700 var(--size-display)/1 var(--display); margin: var(--space-2) 0 var(--space-4); }
.entrance-actions { display: flex; flex-wrap: wrap; gap: var(--space-3); margin-top: var(--space-6); }
.entrance-note, .entrance-more { margin-top: var(--space-5); color: var(--muted); font-size: var(--size-s); }
```

- [ ] **Step 6: Delete the retired files and prove nothing references them**

```bash
git rm -r -q src/Alpha6Ops.Server/Pages/Home src/Alpha6Ops.Server/wwwroot/css/home.css src/Alpha6Ops.Server/wwwroot/images/hero tools/hero \
  src/Alpha6Ops.Server/wwwroot/images/flight-deck.png src/Alpha6Ops.Server/wwwroot/images/globe.jpg src/Alpha6Ops.Server/wwwroot/images/pilot.jpg \
  src/Alpha6Ops.Server/wwwroot/images/routes.jpg src/Alpha6Ops.Server/wwwroot/images/runway.jpg
git grep -n -E "HomeContent|home\.css|images/hero|flight-deck|globe\.jpg|pilot\.jpg|routes\.jpg|runway\.jpg|build_kmke_plan" -- src tools tests
```

Expected: the grep prints nothing. Documentation history under `docs/` may still mention the files; that's fine.

- [ ] **Step 7: Build and run the server suite**

Expected: 0 warnings, and every check passes. The count is 113 minus 2 replaced plus 7 new, so 118.

- [ ] **Step 8: Commit**

```bash
git add -A src/Alpha6Ops.Server tests/Alpha6Ops.Server.Tests tools
git commit -m "feat(portal): home becomes a sign-in entrance; marketing moves to s4ops.com"
```

---

### Task 2: Charcoal tokens, dark only, fluid scale, header and footer

**Files:**
- Modify: `src/Alpha6Ops.Server/wwwroot/css/site.css`, `src/Alpha6Ops.Server/Pages/Shared/_Layout.cshtml`, `tests/Alpha6Ops.Server.Tests/Program.cs`

**Interfaces:**
- Consumes: Task 1's entrance page.
- Produces: header markup classes `.topbar`, `.brand`, `.site-nav`, `.site-menu` (kept names) and footer `.site-footer .footer-row`.

- [ ] **Step 1: Failing server checks for the shared chrome**

Add after the Privacy/Terms loop in the server tests:

```csharp
var chrome = await anonymous.GetStringAsync("/Levels");
Check(chrome.Contains("s4-tile.svg") && !chrome.Contains("s4-lockup") && !chrome.Contains("topbar-plus") && chrome.Contains("content=\"dark\""),
    "Shared header uses the S4 tile, no lockup or stray plus, and declares a dark-only colour scheme");
var css = await anonymous.GetStringAsync("/css/site.css");
Check(!css.Contains("prefers-color-scheme: light") && css.Contains("--surface: #090c0f") && css.Contains("Bahnschrift SemiCondensed") && css.Contains("calc(0.3125vw + 12px)"),
    "Portal styles are charcoal-only, use the Sierra display face and the fluid base size");
```

Build and run the suite. Expected: both checks FAIL; everything else passes.

- [ ] **Step 2: Repoint the tokens and remove the day theme**

In `site.css`, replace the whole `:root { color-scheme: dark; --surface: … --on-primary: #000000;` colour block with:

```css
  color-scheme: dark;
  --surface: #090c0f;
  --surface-raised: #101418;
  --surface-sunken: #151a1f;
  --line: #293138;
  --line-strong: #3a434b;
  --text: #f2f4f5;
  --text-2: #bbc2c8;
  --muted: #8e99a2;
  --action: #86bde8;
  --action-hover: #b8d8f2;
  --on-action: #090c0f;
  --ok: #6fd59a;
  --caution: #ecc66b;
  --warning: #f58d8d;
  --managed: #e08ee0;
  --symbol: #f4cf22;
  --ok-fill: #12281c;
  --caution-fill: #2b2412;
  --warning-fill: #2e1517;
  --action-fill: #132634;
  --brand: #f4cf22;
  --brand-ink: #f4cf22;
  --brand-fill: #252311;
  --primary: #f4cf22;
  --primary-hover: #ffe066;
  --primary-edge: #f4cf22;
  --on-primary: #090c0f;
```

The status values `--ok`, `--caution`, `--warning` and `--information` are the desktop's `S4.Success`, `S4.Warning`, `S4.Critical` and `S4.Information`.
Then delete the block from `/* Tokens: day display, for systems set to light mode */` through its closing `}` `}`. Set:

```css
  --display: "Bahnschrift SemiCondensed", "Barlow Semi Condensed", "Barlow", "Segoe UI", sans-serif;
  --container: max(84rem, 70%);
```

Change `html { background: var(--surface); }` to:

```css
/* Fluid base size: 16px to about 1280px wide, 20px at 2560, capped at 22px (same as s4ops.com). All rem sizes scale with it. */
html { background: var(--surface); font-size: clamp(100%, calc(0.3125vw + 12px), 137.5%); }
```

- [ ] **Step 3: Convert fixed sizes to `rem`**

Run this one-off conversion, which matches the one used for `sites/s4ops.com`. Values of 3px or less, media-query lines, and the `calc(0.3125vw + 12px)` base stay in px:

```bash
python - <<'PY'
import re
p = "src/Alpha6Ops.Server/wwwroot/css/site.css"
s = open(p, encoding="utf-8", newline="").read(); out = []
for line in s.split("\n"):
    if line.lstrip().startswith("@media") or "0.3125vw + 12px" in line: out.append(line); continue
    out.append(re.sub(r"(?<![\w.#-])(\d+(?:\.\d+)?)px",
        lambda m: m.group(0) if float(m.group(1)) <= 3 else (f"{float(m.group(1))/16:.4f}".rstrip("0").rstrip(".") + "rem"), line))
open(p, "w", encoding="utf-8", newline="").write("\n".join(out))
PY
```

- [ ] **Step 4: Rebuild the header and footer in `_Layout.cshtml`**

1. Change `<meta name="color-scheme" content="dark light" />` to `<meta name="color-scheme" content="dark" />`.
2. Replace the `<a class="logo-link" …>…</a>`, the `topbar-state` span and the `topbar-plus` span with:

```cshtml
                <a class="brand" href="/" aria-label="Sierra 4 S4 OPS home"><img src="/images/brand/s4-tile.svg" alt="" width="34" height="34" /><span>SIERRA 4<small>AIRLINE OPERATIONS</small></span></a>
```

3. Replace the whole `<footer class="site-footer">…</footer>` with:

```cshtml
        <footer class="site-footer">
            <div class="footer-row">
                <p><strong>S4 OPS</strong> by Sierra 4. Airline operations for Microsoft Flight Simulator 2024.</p>
                <nav aria-label="Footer"><a href="/Privacy">Privacy</a><a href="/Terms">Terms</a><a href="/Support">Help</a><a href="https://s4ops.com/">s4ops.com</a></nav>
                <p class="fine">@(release.Available ? "S4 OPS " + release.Version + " · " : "")Not affiliated with Microsoft or Asobo Studio.</p>
            </div>
        </footer>
```

4. In `site.css`, delete the rules for `.logo-link`, `.logo-link img`, `.topbar-state`, `.state-dot`, `.topbar-plus`, `.footer-inner`, `.footer-brand*`, `.footer-links*` and `.footer-strip*`. Remove `.topbar-state, .topbar-plus` from the 1100px media query. Add:

```css
.brand { display: flex; align-items: center; gap: 0.75rem; text-decoration: none; color: var(--text); font: 600 1.25rem/1 var(--display); letter-spacing: 0.06em; }
.brand small { display: block; margin-top: 0.25rem; font: 600 0.5625rem/1 var(--font); letter-spacing: 0.14em; color: var(--muted); }
.site-nav { margin-left: auto; }
.site-footer { border-top: 1px solid var(--line); margin-top: auto; }
.footer-row { max-width: var(--container); margin: 0 auto; padding: 2.5rem var(--gutter); display: grid; grid-template-columns: 1fr auto; gap: 0.5rem 2rem; align-items: center; color: var(--text-2); font-size: var(--size-s); }
.footer-row p { margin: 0; }
.footer-row nav { display: flex; gap: 1.375rem; }
.footer-row nav a { color: var(--text-2); }
.footer-row .fine { grid-column: 1 / -1; color: var(--muted); font-size: var(--size-xs); }
@media (max-width: 640px) { .footer-row { grid-template-columns: 1fr; } }
```

Wherever `.topbar-inner` used `max-width: 1440px` (now `90rem` after conversion), set it to `max-width: var(--container)`.

- [ ] **Step 5: Build and run the server suite**

Expected: 0 warnings, and every check passes. That's 120: Task 1's 118 plus the 2 chrome checks.

- [ ] **Step 6: Commit**

```bash
git add src/Alpha6Ops.Server tests/Alpha6Ops.Server.Tests
git commit -m "style(portal): charcoal Sierra tokens, dark only, fluid scale, s4ops.com header and footer"
```

---

### Task 3: Visual pass over every page

**Files:**
- Modify: `src/Alpha6Ops.Server/wwwroot/css/site.css`, only where the checks below find a defect.

- [ ] **Step 1: Capture the pages**

1. Set `S4OPS_PORTAL_SNAPSHOT_DIR=work/ui-check/portal-snapshots` and run the server suite. This writes the signed-in pages as HTML.
2. Start the portal: `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Alpha6Ops.Server --no-build --urls https://localhost:7246`.
3. Serve the snapshot folder alongside the portal's `wwwroot` (copy `wwwroot/css`, `fonts`, `images` and `favicon*` into the snapshot folder) with `python -m http.server 8788 -d work/ui-check/portal-snapshots`.

- [ ] **Step 2: Automated layout check (Playwright MCP)**

For each anonymous URL on `https://localhost:7246` (`/`, `/Levels`, `/Download`, `/Support`, `/Privacy`, `/Terms`, `/SignedOut`, `/AuthError`), and each snapshot page on `http://localhost:8788/<name>.html`, at 1440×900, 2560×1440 and 390×844, assert:

```js
() => ({
  overflowX: document.documentElement.scrollWidth > innerWidth,
  bg: getComputedStyle(document.body).backgroundColor,              // must be rgb(9, 12, 15)
  rootPx: parseFloat(getComputedStyle(document.documentElement).fontSize), // 16 at 1440, 20 at 2560
  scripts: document.scripts.length,                                  // 0 on the live portal
  inlineStyles: document.querySelectorAll('[style]').length,         // 0
  rynel: /rynel/i.test(document.documentElement.outerHTML),          // false
})
```

Expected:
- `overflowX` false and `bg` `rgb(9, 12, 15)` everywhere.
- `rootPx` 16 at 1440 and 20 at 2560.
- `scripts` 0 and `inlineStyles` 0 on the live portal pages.
- `rynel` false everywhere.

Also run with `emulateMedia({ colorScheme: 'light' })` once on `/Levels`: `bg` must still be `rgb(9, 12, 15)`.

- [ ] **Step 3: Look at the screenshots**

Check each page at the three sizes for overlap, unreadable status badges, lost focus outlines (Tab through `/Account/Profile`) and console sidebar collisions. Fix any defect in `site.css`. Each fix gets a measured before and after (the same evaluate script, or an element-overlap check) recorded in the commit message.

- [ ] **Step 4: Full suites, then commit**

Run: both solutions build, then the accounts suite and the server suite against the test database. Expected: all pass, with 0 warnings.

```bash
git add src/Alpha6Ops.Server
git commit -m "style(portal): visual fixes from the charcoal pass"
```

Skip the commit if nothing needed fixing, and record that in the ledger.

---

### Task 4: Handoff

- [ ] **Step 1:** In `docs/s4-migration/PROGRESS.md` "Next steps", tick "Simplify the portal home…" and add:
  `- [x] Portal restyled to charcoal Sierra tokens (dark only, fluid scale); marketing removed (spec docs/superpowers/specs/2026-10-03-portal-simplify-design.md).`
- [ ] **Step 2:** Commit: `docs: record portal simplification`.
