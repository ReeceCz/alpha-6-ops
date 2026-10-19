# For Dan: where S4 OPS is, and what needs you

Reece, 13 October 2026. About ten minutes to read; the decisions are in section 2.

## 1. Where things stand

- **The website is live at https://app.s4ops.com** (Render + Neon, behind Cloudflare). It's invite-only. Sign-in goes
  through the Auth0 dev tenant (password, passkey or Google), and emails go out from `noreply@s4ops.com` through
  Resend. Replies and the public contact go to `support@s4ops.com`, which forwards to Reece.
- **s4ops.com** is the landing page (Cloudflare Worker). Its account buttons lead to app.s4ops.com.
- **On the website today:**
  - pilot profile, a logbook built from CSV imports, and "Report a problem";
  - airlines with an overview, schedule, fleet, crew invitations and roles, and settings;
  - a staff area for Reece and you: problem reports, testers and invitation requests, release notes, and the versions
    pilots are on;
  - an "Ask for an invitation" form, and a one-click request for anyone who signs in uninvited.
- **Your 7.26B work** is merged with the account layer on top (`merge/dan-7.26b`, then `feat/launch-prep`). Tracking,
  recovery, storage, Map V2 and `SierraShell` were taken as they are.
- **Not yet:** no installer has been published, and the desktop doesn't send flights to the website. Both need you.

## 2. Decisions that need you

1. **Sign off two changes in your protected areas** (from the merge):
   - The coordinator takes an optional account identity and writes an `account_identity` history event when a row
     begins. The row identity doesn't change.
   - Retention also keeps OFPs referenced by `Data\accounts\**\active-flight.json`. This only adds to the keep set.
     New check: `ACCOUNT_WORKSPACE_ACTIVE_OFP_SURVIVES_QUOTA`.
   - Minor: the `verify-desktop.ps1` timeout went from 60 to 120 s (the full smoke run takes about 70 s here).
2. **The first tester installer.**
   - The desktop project says `0.23.0`, and pilots signed in so far report `0.16.0`. Which version ships, and is
     `packaging/build-desktop.ps1` ready to build it?
   - Once you say go, Reece runs `scripts/publish-release.ps1`. It builds against the hosted sign-in config and
     refuses an installer that points anywhere other than app.s4ops.com.
   - It then publishes to GitHub Releases, and the Download page shows the version, checksum and notes.
3. **Desktop flights reaching the website.** The server side is ready (`POST /api/v1/me/flights`, duplicates
   refused). The proposal is in `flight-upload-contract-draft.md`: the history row `id` is the upload identity,
   uploads are queued at `pirep_submitted`, and a small outbox retries them. It has five questions only you can answer:
   - Is the row `id` the right identity?
   - Queue inside closeout, or in an observer?
   - Which fields does closeout supply reliably?
   - What does a pilot see when uploads fail?
   - Backfill older flights, or skip them?
4. **The desktop icon and colours.** Swap to the S4 tile, and carry the website's colour tokens into WPF? This is
   cosmetic and can wait.
5. **Account data for your 7.27 UI.** `7.27-account-data.md` maps your mockup's panels to real data. Header,
   role, fleet, network and today's planned legs are available now (`ListRoutesAsync`, `ListFleetAsync`,
   `AirlineSchedule.Between`). Live counts, on-time %, alerts and finances aren't, so please keep sample numbers off them.

## 3. Ten minutes on the website

1. Sign in at https://app.s4ops.com with the email that's in `Staff__Emails`. If you aren't in it yet, Reece adds
   you in Render.
2. Open **Staff**: it asks for a fresh MFA or passkey check, then shows the tester funnel, reports and requests.
3. Create a test airline, add a route and an aircraft, and invite a second address of yours under **Crew**.
4. Anything wrong or confusing: **Report a problem** on your account page.

## 4. What Reece does after your answers

- Publish the installer (checklist step 11), write its release notes, and invite the first 3–5 testers.
- Build the website side of the flight upload to match whatever you decide in 2.3.
- Still to do on the hosting side, without you:
  - Auth0 email and login-page branding;
  - an outside uptime alert;
  - one test restore of a database backup.
