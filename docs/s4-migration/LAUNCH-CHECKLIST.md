# Tester launch checklist (Reece's manual steps)

Everything below needs a person in a dashboard or a push. The code for all of it is done on branch
`feat/launch-prep` (worktree `C:\Dev\_scratch\s4-merge`). Everything is free. Steps are in order; each one says how
to check it worked.

`powershell -File scripts/check-live.ps1` checks the live sites. On 2026-10-04 it failed 10 checks: nine
`app.s4ops.com` checks (the name doesn't exist) and HSTS on s4ops.com (the new landing isn't deployed yet).

## 1. Ship the code (about 5 min)

1. Read the branch: `git log --oneline merge/dan-7.26b..feat/launch-prep` (all suites green).
2. Push it to the fork and move `accounts` forward. Render deploys `accounts` automatically.
   ```
   git push fork feat/launch-prep
   git push fork feat/launch-prep:accounts
   ```
   `feat/launch-prep` starts at `fork/accounts` (259d9cb), so the second push is a fast-forward.
3. Render applies the five new migrations when the container starts (`entrypoint.sh` runs `--migrate-accounts`):
   - lower-case emails
   - feedback tables
   - tester invitations
   - release notes
   - invitation requests
4. **Check:** `https://alpha6ops.onrender.com/healthz` → `ok`, and `/Support` shows the "Invitations" and "Report a
   problem" cards.

## 2. Render environment (about 5 min)

Dashboard → service `alpha6ops` → Environment:

| Key | Value | Why |
|---|---|---|
| `Staff__Emails` | your and Dan's sign-in emails, `;` separated | Opens `/Staff/Feedback` and `/Staff/Testers` to you two |
| `Legal__Contact` | a public email, when you have one (see step 4) | Privacy, Terms, Help and Download link it. Leave it empty until then |
| `Hosting__PublicHosts` | `app.s4ops.com` | Already in `render.yaml`; confirm it's set before step 3 |
| `Hosting__TrustCloudflare` | `true` | Same |

**Check:** sign in at `https://alpha6ops.onrender.com`. Your account page shows a "Staff" button. `/Staff` opens the
overview (tester funnel, desktop versions, what needs you), with tabs for Problem reports, Testers and Release notes.
The staff pages ask for a fresh MFA check the first time.

## 3. `app.s4ops.com` (about 15 min, mostly waiting)

Follow "Custom domain through Cloudflare" in `docs/hosting.md`, steps 1–8. In short:

1. **Render** → Custom Domains → add `app.s4ops.com`.
2. **Cloudflare** → DNS → `CNAME app → alpha6ops.onrender.com`, **DNS only** (grey cloud).
3. Wait for Render's certificate.
4. **Cloudflare** → SSL/TLS **Full (strict)**, then switch the record to **Proxied**, then turn on **Always Use
   HTTPS**.
5. **Auth0** → Portal application. Add:
   - `https://app.s4ops.com/signin-oidc` to Allowed Callback URLs
   - `https://app.s4ops.com/signout-callback-oidc` to Allowed Logout URLs
   - `https://app.s4ops.com` to Allowed Web Origins
6. **GitHub** → fork → Settings → Variables → set `ALPHA6_HEALTH_URL` to `https://app.s4ops.com/healthz`, so keep-warm
   goes through Cloudflare.

**Check:** `powershell -File scripts/check-live.ps1` shows no `app.s4ops.com` failures, and sign-in/sign-out works
at `https://app.s4ops.com`.

## 4. s4ops.com landing (about 2 min)

```
cd sites/s4ops.com
npx wrangler deploy
```

This puts the HSTS header, the phone menu and the analytics-friendly CSP live. Cloudflare Web Analytics is
already injecting its script; the new CSP lets it run.

**Optional, free:** to have a contact email without exposing a personal address, Cloudflare → `s4ops.com` → Email →
Email Routing → create `support@s4ops.com`, forwarded to your inbox. Then set `Legal__Contact = support@s4ops.com`
(step 2).

**Check:** `check-live.ps1` passes every check (or only the release line says "info").

## 5. First installer (about 10 min)

Coordinate the version with Dan; `packaging/build-desktop.ps1` has it hard-coded.

```
powershell -File scripts/publish-release.ps1             # build + hash, dry run
powershell -File scripts/publish-release.ps1 -SkipBuild -Upload
```

The upload creates the public pre-release `desktop-v<version>` on the fork. Paste the three `Release__*` lines it
prints into Render's Environment.

Then write the notes at `/Staff/Releases` (version, title, one change per line). They appear on `/Download`, on
`/Releases`, and as "Update available" on the account page of any pilot on an older desktop.

**Check:** `/Download` shows the version, the button, the checksum and the notes, and `check-live.ps1` downloads the
installer URL.

## 6. Invite testers (1 min each, repeatable)

Strangers can ask at `https://app.s4ops.com/Request`; "Ask for an invitation" on s4ops.com and the portal links there.
Requests appear at the top of `/Staff/Testers` with **Invite** and **Decline** buttons. You can also add an email
there directly. Either way, tell the tester:

> Go to s4ops.com → "Create your invited account" with that email → verify it → download S4 OPS for Windows → sign in.
> Something wrong? Use "Report a problem" in your account.

No Render change is needed per tester any more. `Accounts__InvitedEmails` still works alongside it.

## 7. Optional: Turnstile on the request form (about 5 min, free)

The form already has a honeypot and a limit of 3 requests per address per hour. If spam gets through, Cloudflare →
Turnstile → Add widget for `app.s4ops.com` (Managed mode). Copy the keys into Render as `Turnstile__SiteKey` and
`Turnstile__SecretKey`. The widget appears on `/Request` by itself; nothing else changes.

## Still open with Dan (not blocking testers)

- Sign-off on the two protected touches in the 7.26B merge (see the merge notes).
- "Diverted" is shown on the site, but the engine has no diversion detection yet.
- The desktop icon swap to the S4 tile (`src/Alpha6Ops.Desktop` assets) and carrying the tokens into WPF.
- The installer version bump in `packaging/build-desktop.ps1`.
