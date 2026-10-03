# Portal simplification and charcoal restyle — design

Status: draft for Reece's review, 2026-10-03. Branch `merge/dan-7.26b`. Follows
`2026-10-02-s4ops-landing-design.md`, which made `s4ops.com` the front door.

## Purpose

The account portal (`app.s4ops.com`, `src/Alpha6Ops.Server`) is now a legacy build in two ways:

- **Content:** its home page duplicates the marketing story that `s4ops.com` tells.
- **Look:** it wears the older glass-cockpit style, while `s4ops.com` and the desktop app share the charcoal Sierra tokens.

The portal becomes what it is for: the place you sign in and manage your account and airlines. It should look like the
same product as `s4ops.com` and the app.

Decided with Reece:
- Strip the duplicated marketing, and restyle every remaining page.
- No consolidation of pages or URLs.
- Dark charcoal only. The day theme is removed.

## Home (`/`)

- **Signed in:** a `302` redirect to `/Account`.
- **Signed out:** a single short entrance:
  - the S4 tile, "Your S4 OPS account", and one line saying what the portal is for;
  - "Sign in" (`/auth/login`) and "Create your invited account" (`/auth/signup`);
  - the invite-only note, with "Ask for an invitation" as a `mailto:` to `Legal.Contact`;
  - "What is S4 OPS? See s4ops.com" (`https://s4ops.com/`).
- **Account services unconfigured:** the existing behaviour stays. A notice explains that registration isn't available,
  and no sign-up link is shown.
- **Deleted:** `Pages/Home/*` (nine partials and `HomeContent.cs`), `wwwroot/css/home.css`, `wwwroot/images/hero/`,
  `flight-deck.png`, `globe.jpg`, `pilot.jpg`, `routes.jpg` and `runway.jpg`. Each is first checked to have no remaining
  reference.

## Restyle (all remaining pages)

- **Pages:** Account (home, Profile, Plan, Logbook, Create, Join, Airline, Schedule, Fleet, Delete), Levels, Download,
  Support, Privacy, Terms, AuthError, SignedOut, Error, and the two layouts (`_Layout`, `_ConsoleLayout`).
- **Tokens:** `site.css` tokens are repointed to the Sierra values used by `sites/s4ops.com`:

  | Token | Value |
  |---|---|
  | background | `#090C0F` |
  | surfaces | `#101418` / `#151A1F` / `#20272D` |
  | dividers | `#293138` / `#3A434B` |
  | text | `#F2F4F5` / `#BBC2C8` / `#8E99A2` |
  | primary and brand | `#F4CF22` |
  | information | `#86BDE8` |

  The status colours (ok, caution, warning, managed) keep their meanings, retuned for the charcoal surfaces with at least
  4.5:1 contrast. The `prefers-color-scheme: light` block is removed.
- **Type:** `"Bahnschrift SemiCondensed", "Barlow Semi Condensed"` for display text, Barlow for body text, IBM Plex Mono
  for codes. These are the portal's existing self-hosted fonts.
- **Scale:** the same fluid base as `s4ops.com`:
  `html { font-size: clamp(100%, calc(0.3125vw + 12px), 137.5%) }`, with sizes in `rem`, so the portal grows at 1440p and
  4K and is unchanged on laptops and phones.
- **Header:** the compact S4 tile and "SIERRA 4", nav links (Levels, Download, Help) and Sign in / Account, matching
  `s4ops.com`. The `_ConsoleLayout` sidebar keeps its structure with the new tokens.
- **Footer:** "S4 OPS by Sierra 4", Privacy · Terms · Help, the not-affiliated line, and the published desktop release
  version.
- **Unchanged rules:** the existing rules stay as they are:
  - no JavaScript, and no inline `style=` (CSP);
  - status always comes with an icon and words;
  - no "RYNEL";
  - the server's host, tenant and environment checks are untouched.

## Not changing

Every route, form, handler, API, the account/airline/logbook/deletion behaviour, invite-only admission, cookies and
security headers.

## Testing

- **New server tests, written failing first:**
  - signed-in `GET /` redirects to `/Account`;
  - the signed-out `/` has sign-in, the invited sign-up and `s4ops.com` links, plus the invite-only text, and none of
    the old marketing sections;
  - the unconfigured `/` still explains registration and offers no sign-up;
  - no anonymous page links `home.css` or a deleted image.
- **Existing server test:** the one that asserts the old landing text ("How it works", "Every phase recorded…",
  "Demonstration data") is rewritten to the new entrance. Its unconfigured and script-free checks are kept.
- **Full suites against the test database:** accounts and server. Both solutions build with 0 warnings.
- **Screenshots** from the portal snapshot mode (`S4OPS_PORTAL_SNAPSHOT_DIR`) plus anonymous pages, at 1440×900,
  2560×1440 and 390×844:
  - no horizontal overflow, nothing overlapping;
  - focus outlines visible;
  - headline and body text contrast at least 4.5:1 on the new surfaces.

## Out of scope

- Page consolidation and URL changes.
- The contact address. It still shows `[CONTACT EMAIL]` until Reece provides it.
- Deploying.
