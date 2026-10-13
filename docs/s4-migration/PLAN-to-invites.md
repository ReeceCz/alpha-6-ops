# S4 OPS: plan from here to inviting users

Written 13 October 2026 after a full review of `feat/launch-prep` (worktree `C:\Dev\_scratch\s4-merge`).
`LAUNCH-CHECKLIST.md` is the manual go-live sitting. This file covers everything around it: what to do before
that sitting, the loop with the first testers, and the work needed before invitations widen.

## 1. Where things stand

### Code (verified 13 October)

- `feat/launch-prep` is the newest branch. It contains everything on `chore/s4-rebrand`, `merge/dan-7.26b` and
  `fork/accounts`, plus 33 launch commits. **It is not pushed.** `fork/accounts` (the branch Render deploys) is still at 259d9cb.
- Both solutions build with 0 warnings. Core 194 and accounts 90 pure checks pass. The DB suites were not re-run
  today; on 5 October they were accounts 282 and server 225, all green.
- The portal has: an invite-only sign-up with a verified email allow-list, profile, cloud logbook (CSV import,
  undo, insights), the airline console (schedule, fleet, crew, invitation codes, ownership, logo, audit),
  getting started, Report a problem with replies, the staff area (overview, testers, invitation requests,
  release notes), `/Guide`, `/Download`, `/Releases`, `/Request`, Privacy/Terms, data export and deletion, and
  optional Resend email.
- `s4ops.com` (landing Worker) is live.

### Live (checked 13 October with `scripts/check-live.ps1`)

| Check | Result |
|---|---|
| `s4ops.com` and its assets | OK |
| `s4ops.com` HSTS | **Missing.** The 4 October landing fixes aren't deployed. |
| `app.s4ops.com` | **No DNS record.** Every account link from the landing page is dead. |
| `alpha6ops.onrender.com` | Runs the 2 October code (`fork/accounts`) |

### Gaps found in this review (not in the launch checklist)

1. **The installer would ship without a usable server configuration.** `alpha6-identity.json` is gitignored and
   copied into the build only if it exists. It's missing in the worktree. The main checkout's copy points
   `ApiBaseUrl`/`PortalUrl` at `https://localhost:7246/`. `publish-release.ps1` doesn't check for either case.
   A tester build made today would either refuse sign-in or call the tester's own localhost.
2. **The desktop doesn't upload flights.** `POST /api/v1/me/flights` exists, but nothing in the desktop calls it.
   Duplicates are already handled: a unique index on pilot, departure, flight number, origin and destination
   makes a repeat return `409 flight_exists`, so a desktop outbox can treat that as delivered.
3. **The main checkout is stale.** `C:\Users\…\dev\alpha-6-ops` is on `chore/s4-rebrand`, 81 commits behind. Its
   uncommitted page/CSS edits repeat fixes that already landed in `0b2bae6`. Its `docs/web-status-2026-10-13.md`
   reviewed that stale branch, so it misses the staff area, requests, feedback and email.
4. **Airline invitations are still copy-paste codes.** The console says "Email delivery is not connected yet",
   although Resend now sends tester invitations.
5. **Production basics are unverified.** There's no build/test CI, no tested backup restore, and no alert when
   the site is down. The Auth0 tenant is the `dev-…` tenant. The Neon password and Render deploy hook pasted
   into chat on 18 September were rotated on 13 October.

## 2. Phases

Each phase lists its exit criteria. Don't start widening invitations until Phase 4's criteria are met.

### Phase 0: consolidate (one short session, Claude)

- [ ] Make `C:\Dev\_scratch\s4-merge` (`feat/launch-prep`) the only working tree for web work. In the main checkout,
      move the uncommitted edits and `web-status-2026-10-13.md` to `C:\Dev\_scratch\stale-s4-rebrand\` after a diff
      check against `feat/launch-prep`, then leave that checkout alone. (Its untracked Neon leftovers stay as they are.)
- [x] Start the local PostgreSQL cluster and run `/verify` on `feat/launch-prep` (13 Oct: core 194, desktop identity 102,
      accounts 282, server 225, all green).
- [x] **Fix gap 1** (0de4b14): `publish-release.ps1` refuses to build unless `alpha6-identity.json` exists and its
      `ApiBaseUrl` and `PortalUrl` are `https://app.s4ops.com/`. It builds with the gitignored
      `alpha6-identity.hosted.json` and checks the payload inside the installer. A local dry-run build passed, and a
      localhost payload was rejected.
- [ ] Decide what happens to `docs/web-roadmap.md`. It's a September inventory. Add a banner that points here.

**Exit:** one branch, all suites green, and a release build that can only point at the hosted server.

### Phase 1: go live for staff (Reece, about 1 hour: `LAUNCH-CHECKLIST.md` steps 1–10)

Claude can't push, change DNS or edit dashboards, so this is your sitting. Do it in order: push, Render domain,
grey-cloud CNAME, Auth0 URLs, keep-warm variable, email routing and Resend, `wrangler deploy`, orange cloud,
Render environment. Then run `check-live.ps1` until it's clean.

Add to that sitting:
- [x] Rotate the Neon password and the Render deploy hook (done 13 October: new `neondb_owner` password in
      `ConnectionStrings__Accounts`, deploy hook regenerated, sign-in confirmed).
- [ ] Optional: create the Auth0 M2M app with only `delete:users` (`docs/hosting.md`). Without it, deleting an
      account leaves the Auth0 sign-in for you to remove by hand.

**Exit:** `check-live.ps1` passes. You and Dan can sign in at `app.s4ops.com`, see `/Staff`, and send a test
invitation that arrives.

### Phase 2: first installer and the first 3–5 testers (Reece + Dan, 1–2 weeks)

- [ ] Dan agrees the version and bumps `packaging/build-desktop.ps1`. Then run checklist step 11 with the hosted
      `alpha6-identity.json`. The release goes to GitHub Releases on the fork. Moving the installer to R2 is a later
      change, not a blocker.
- [ ] Install it on a clean Windows account (not your dev profile): download, compare the checksum, install,
      sign in, fly one short leg, report a problem from the account page. That's the tester's exact path.
- [ ] Invite 3–5 people you know: Dan plus friendly simmers. Use `/Staff/Testers`.
- [ ] Each week: read `/Staff` (funnel, versions, open reports), reply to every report, publish fixes as releases
      with notes.

What to learn from them, in order: does install and sign-in work without help, does the desktop track their
flights correctly, and what do they look for on the website that isn't there.

**Exit:** every tester completed install → sign-in → one tracked flight without you on a call, and no report
older than a week is unanswered.

### Phase 3: desktop flights reach the web (Dan + Reece, the main product gap)

The web logbook holds CSV imports only. A pilot who flies sees nothing on the web, so the portal has little
reason to exist for them. This is the most valuable build. It touches Dan's PIREP/closeout area, so it starts
with an agreed contract.

1. **Contract with Dan:** draft in `flight-upload-contract-draft.md`. It proposes using his flight-history row
   ID as the upload identity, which needs one server field and a migration once he agrees. Points to settle: which completed-flight record is sent and
   when (at block-in or at PIREP close), its fields, and its stable ID (the desktop's flight/row ID). Also agree
   whether it carries an airline and route, and what the pilot sees when an upload fails.
2. **Server (Reece):** after step 1, add `ClientFlightId` (unique per pilot, a repeat returns the stored flight).
   Until then, the 409 rule in gap 2 stops duplicates. Airline/route attribution depends on step 1. The logbook already records `Source` (`desktop` or `import`).
3. **Desktop (by agreement):** a small durable outbox in the account workspace. The flight is queued at closeout
   and sent on success, on start-up, and when the user signs in again. `200` and `409 flight_exists` both mean delivered. Failures are shown, not swallowed.
4. **Web:** the flight detail page (times, OOOI, block/flight minutes, aircraft), and change the
   copy that currently says flights don't arrive automatically.

**Exit:** a flight flown offline appears on the web exactly once after reconnecting, and killing the app
mid-upload doesn't lose or duplicate it.

### Phase 4: ready to widen (Reece, can run alongside Phase 3)

Make the service safe for strangers before inviting them.

- [x] **CI** (9a8576b, committed, not pushed, not yet run): a GitHub Actions workflow on the fork that builds `Alpha6Ops.Cloud.slnx` on Linux and runs the
      accounts and server suites against a Postgres service container. WPF stays out. Needs your go-ahead,
      because it adds a workflow file to GitHub.
- [ ] **Backups:** restore Neon's production branch to a temporary branch at a past time, point a local server
      at it, sign in, delete the branch. Write down how far back the free plan lets you restore.
- [ ] **Monitoring:** keep-warm now fails (and GitHub emails you) when `/healthz` isn't `ok` (9a8576b). Still to do: an outside uptime check on `/healthz` that emails you (for example Cloudflare's health check
      or a free uptime service), and make the keep-warm workflow fail loudly. Check Render's logs for unhandled
      exceptions once a week during the beta.
- [ ] **Auth0:** decide whether the `dev-…` tenant is good enough for a beta or whether to create a production
      tenant now. A move later forces every user to sign up again. Check branding on the universal login page,
      the email templates (verification and reset say S4 OPS, not Alpha 6) and the free-plan user limits.
- [ ] **Capacity:** Render Free (cold starts, 512 MB) and Neon Free are fine for about 20 testers. Write down
      the numbers that trigger the paid upgrade in `hosting-options.md`.
- [ ] **Legal:** `Legal__Contact` set and Wisconsin shown. Read Privacy/Terms once more against what is actually
      collected now (feedback attachments, Resend, Cloudflare Web Analytics).
- [ ] **Security pass:** run `/security-review` over the launch-prep diff (staff area, request form, email outbox,
      feedback uploads). Fix anything confirmed before widening.

**Exit:** CI is green on every push, a restore has been done once, and a downed site emails you within minutes.

### Phase 5: airline setup that works without you (Reece)

Testers who run a virtual airline need to set it up alone. This is the "usable airline setup" milestone from the
13 October status review, still valid.

- [x] (62fc930) Airline **Overview** page with real counts (routes, aircraft, active crew) and a setup checklist: add a
      route, add an aircraft, upload a logo, invite a pilot. Each step links to the existing form and respects the
      user's access level.
- [x] Split today's single Crew & settings page into Crew (roster, invitations, roles) and Settings (level, logo,
      ownership transfer). Fresh MFA stays on privileged actions.
- [x] **Emailed airline invitations.** Invitations are bound to the verified email, so the email carries only a link to
      Join an airline, which lists invitations waiting for that address. The code remains a fallback.
- [x] Create and join land on the airline Overview. (Imports and invites already stay on their page.)
- [x] Crew roster stacks one member per block at phone width, so the roles form stays visible.
- [ ] Test the journey as owner, dispatcher, pilot, outsider, and an account pending deletion, at phone width.

**Exit:** a tester who has never seen S4 OPS creates an airline, adds a route and an aircraft, and invites a
second account by email. The member sees the schedule but can't edit it.

### Phase 6: open the doors in batches

- Turn on Turnstile on `/Request` if spam appears.
- Approve requests in batches (10, then 25, then 50) and watch `/Staff` and the error log after each batch.
- Hold if any of these happen: an unanswered report older than a week, an outage that monitoring missed, or a
  duplicated or lost flight.
- Ask each batch one question, through a reply on their first report or a short email: "What did you expect
  S4 OPS to do that it didn't?" Those answers choose what comes after Phase 5.

## 3. Deliberately later (each must justify itself first)

Each needs a named user and a gap it closes before it's built. Revisit after Phase 6.

| Idea | Why it waits |
|---|---|
| Public airline pages `/va/{slug}` and a directory | Worth it once airlines exist and want recruits. Opt-in only. Never expose rosters or emails. |
| Recruitment applications | Needs public pages and email first. |
| Assignments, flight review by staff, statistics | Needs reliable attributed flights (Phase 3) first. |
| Billing (Stripe and Link, designed in `billing-roadmap.md`) | No paid promises until the paid features exist. |
| Installer on R2 at `s4ops.com` | GitHub Releases works. Move it when download counts matter. |
| Moving off Render/Neon to Cloudflare Containers | Upgrade path when there are paying users (decision of 22 September). |
| Code rename `Alpha6Ops` → `S4Ops` | Shelved on 2 October. Users never see it. |
| Diversion detection | Dan's call. The website shows no "Diverted" status (checked 13 October). |

## 4. Waiting on Dan

1. Sign-off on the two protected touches in the 7.26B merge (account identity event, OFP retention).
2. Installer version for the first test release.
3. The Phase 3 flight contract.
4. Desktop icon swap to the S4 tile and the WPF colour tokens.
5. Diversion detection: yes or no.

## 5. Order at a glance

```
Phase 0 consolidate (Claude, 1 session)
  └─ Phase 1 go-live sitting (Reece, ~1 h)
       └─ Phase 2 installer + 3–5 testers (1–2 weeks)  ──┐
            ├─ Phase 3 flight sync (needs Dan's contract) │ in parallel
            └─ Phase 4 production basics ─────────────────┘
                 └─ Phase 5 airline setup
                      └─ Phase 6 batched invitations
```
