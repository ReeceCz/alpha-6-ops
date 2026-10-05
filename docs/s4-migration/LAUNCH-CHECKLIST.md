# Tester launch checklist (Reece's manual steps, one sitting)

Everything here needs a person in a dashboard, or a push. The code is done on branch `feat/launch-prep` (worktree
`C:\Dev\_scratch\s4-merge`, all suites green). It's all free and takes about 45 minutes, mostly waiting for one
certificate. The order puts that wait in the middle so the other steps happen during it.

`powershell -File scripts/check-live.ps1` checks the live sites at any point. Before these steps it fails on
`app.s4ops.com` (no DNS record) and on HSTS for s4ops.com (the new landing isn't deployed).

## 0. Have ready

- [ ] Logins: GitHub (fork `ReeceCz/alpha-6-ops`), Render, Cloudflare (the `s4ops.com` account), Auth0 dev tenant,
      and a free Resend account (resend.com) if you want emails on from day one.
- [ ] The sign-in emails for you and Dan (for `Staff__Emails`).
- [ ] A decision on the public contact email: create `support@s4ops.com` in step 6 (recommended, free), or leave it
      empty for now.
- [ ] Node 22+ for `npx wrangler` (landing deploy).

## 1. Push the code (2 min)

```
cd C:\Dev\_scratch\s4-merge
git log --oneline merge/dan-7.26b..feat/launch-prep
git push fork feat/launch-prep
git push fork feat/launch-prep:accounts
```

`feat/launch-prep` starts at `fork/accounts` (259d9cb), so the second push is a fast-forward. Render deploys
`accounts` automatically. At start-up the container applies the five new migrations:
- lower-case emails
- feedback
- tester invitations
- release notes
- invitation requests

## 2. Render: add the custom domain (1 min)

Render → service `alpha6ops` → Settings → Custom Domains → **Add** `app.s4ops.com`. Keep the page open; it shows the
certificate status.

## 3. Cloudflare: the `app` record, grey cloud (1 min)

Cloudflare → `s4ops.com` → DNS → Add record:

| Type | Name | Target | Proxy |
|---|---|---|---|
| CNAME | `app` | `alpha6ops.onrender.com` | **DNS only** (grey) |

There must be no `AAAA` record for `app`. Render now starts issuing its certificate (usually a few minutes). Do
steps 4–7 while you wait.

## 4. Auth0: allow the new address (3 min)

Auth0 → Applications → **Portal** application → Settings. Add, keeping the existing entries:

| Field | Add |
|---|---|
| Allowed Callback URLs | `https://app.s4ops.com/signin-oidc` |
| Allowed Logout URLs | `https://app.s4ops.com/signout-callback-oidc` |
| Allowed Web Origins | `https://app.s4ops.com` |

While you're there, note which sign-in options are on (Authentication → Database / Social / Passwordless). The site
promises only "email and password, or another option the page offers". If passkeys or Microsoft/Google sign-in are
on, tell me and the pages can say so; Help's "Passkeys and MFA" card assumes passkeys may be available.

## 5. GitHub: keep-warm through the new address (1 min)

Fork → Settings → Secrets and variables → Actions → Variables → `ALPHA6_HEALTH_URL` =
`https://app.s4ops.com/healthz`.

## 6. Cloudflare extras (5 min, optional but recommended)

- [ ] **Contact email:** Email → Email Routing → enable → create `support@s4ops.com`, forwarded to your inbox.
      Confirm the verification email. Use it as `Legal__Contact` in step 9.
- [ ] **Turnstile** (only if you want it from day one; the request form already has a honeypot and a limit of 3
      requests per address per hour): Turnstile → Add widget → hostname `app.s4ops.com`, Managed mode → copy the
      site key and secret key for step 9.

- [ ] **Emails (Resend, free: 3,000 a month, 100 a day):**
  1. Resend → Domains → Add `s4ops.com`.
  2. Add the DNS records Resend shows in Cloudflare DNS, all **DNS only**. Usually that's an MX and an SPF TXT on a
     `send` subdomain, and a DKIM TXT on `resend._domainkey`.
  3. Click Verify.
  4. API Keys → create one with **Sending access** for `s4ops.com`, and copy it for step 9.

  Without a key nothing breaks: emails are only logged, and `/Staff/Testers` keeps the copy-paste message.

## 7. Deploy the landing (2 min)

```
cd C:\Dev\_scratch\s4-merge\sites\s4ops.com
npx wrangler deploy
```

This puts live the HSTS header, the phone menu, the CSP that lets Cloudflare Web Analytics run, and "Ask for an
invitation" pointing to `/Request`.

## 8. Cloudflare: proxy the record (2 min, once Render shows the certificate as issued)

1. SSL/TLS → Overview → **Full (strict)**.
2. DNS → `app` record → switch to **Proxied** (orange cloud).
3. SSL/TLS → Edge Certificates → **Always Use HTTPS** on.

## 9. Render environment, in one save (3 min)

Render → `alpha6ops` → Environment. Every save restarts the service, so enter all of these and save once:

| Key | Value |
|---|---|
| `Staff__Emails` | `you@…;dan@…` (the emails you two sign in with) |
| `Hosting__PublicHosts` | `app.s4ops.com` (should already be there from `render.yaml`; confirm) |
| `Hosting__TrustCloudflare` | `true` (same) |
| `Legal__Contact` | `support@s4ops.com`, if you created it in step 6; otherwise leave it out |
| `Turnstile__SiteKey` / `Turnstile__SecretKey` | only if you created them in step 6 |
| `Email__ResendApiKey` | only if you set up Resend in step 6 |

## 10. Check everything (5 min)

- [ ] `powershell -File scripts/check-live.ps1` → all checks pass (the release line shows "info" until step 11).
- [ ] At `https://app.s4ops.com`: sign in → the account page shows **Getting started** and a **Staff** button →
      sign out works.
- [ ] `/Staff` asks for a fresh MFA check, then shows the overview with tabs for Problem reports, Testers and
      Release notes.
- [ ] `https://s4ops.com` on your phone: the **Menu** button opens; "Ask for an invitation" opens `/Request`.
- [ ] Privacy and Terms show your contact email (if set) and Wisconsin.
- [ ] Emails on? Invite a second address of yours at `/Staff/Testers`. The invitation email should arrive from
      `noreply@s4ops.com`, and replies to it should go to `Legal__Contact`. Check the spam folder the first time.

## 11. First installer (10 min, with Dan's go-ahead on the version)

```
cd C:\Dev\_scratch\s4-merge
powershell -File scripts/publish-release.ps1                 # build + hash, dry run
powershell -File scripts/publish-release.ps1 -SkipBuild -Upload
```

1. Paste the three `Release__*` lines it prints into Render → Environment (one save).
2. Write the notes at `/Staff/Releases`: the version, a title, and one change per line.
3. **Check:** `/Download` shows the version, the checksum and the notes, and `check-live.ps1` downloads the installer.

## 12. Invite testers (1 min each, repeatable)

- Requests from `/Request` appear at the top of `/Staff/Testers` with **Invite** / **Decline**, and staff get an
  email for each new request once emails are on.
- To invite someone directly, add their email on the same page.
- With emails on, the tester receives the invitation automatically. Otherwise send them the message shown on the
  page:

> Go to s4ops.com → "Create your invited account" with that email → verify it → download S4 OPS for Windows → sign in.
> Something wrong? Use "Report a problem" in your account.

## Still open with Dan (not blocking testers)

- Sign-off on the two protected touches in the 7.26B merge (see the merge notes).
- The installer version bump in `packaging/build-desktop.ps1` (needed for step 11).
- Desktop flight upload to the web logbook. The server's `POST /api/v1/me/flights` is ready; the desktop doesn't
  call it yet. Until then the web logbook holds imports only, and the site says so.
- "Diverted" is shown on the site, but the engine has no diversion detection yet.
- The desktop icon swap to the S4 tile, and carrying the colour tokens into WPF.
