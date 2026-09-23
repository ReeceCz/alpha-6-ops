# Hosting the account server (testing phase: Render + Neon, free)

The account server (`src/Alpha6Ops.Server`) runs anywhere that can run a container and reach PostgreSQL. For
the testing phase it lives on Render's free web service with a Neon free PostgreSQL project: $0, a stable
HTTPS address, no card. The only cost is that a free Render instance sleeps after 15 minutes without traffic
and takes about half a minute to wake; the `keep-warm` GitHub workflow pings it every 10 minutes.

## What the repository provides

| Piece | Purpose |
| --- | --- |
| `src/Alpha6Ops.Server/Dockerfile` | Multi-stage build: SDK 10 publish → ASP.NET 10 runtime image |
| `src/Alpha6Ops.Server/entrypoint.sh` | Applies pending migrations (`--migrate-accounts`), then serves on `$PORT` |
| `render.yaml` | Render blueprint: free web service, Docker runtime, `/healthz` health check, env var list |
| `scripts/new-keyring-certificate.ps1` | Generates the certificate that encrypts the data-protection key ring, as base64 PFX |
| `.github/workflows/keep-warm.yml` | Optional cron ping (set repository variable `ALPHA6_HEALTH_URL`) |
| `GET /healthz` | Anonymous liveness endpoint: `ok` when identity is configured, `unconfigured` otherwise |

Production requires a data-protection certificate and a key store. With no writable directory configured the
key ring is stored in the account database (`data_protection_key`, migration `202609190001`), encrypted with the
certificate, so cookies and antiforgery tokens survive deploys and replicas. `Hosting__BehindProxy=true` makes
the app honour `X-Forwarded-Proto`/`X-Forwarded-For` from the host's proxy; leave it unset when Kestrel
terminates TLS itself.

## Environment variables

| Variable | Value |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Hosting__BehindProxy` | `true` on Render or any reverse proxy |
| `Hosting__TrustCloudflare` | `true` when Cloudflare proxies the custom domain (requires `Hosting__BehindProxy`). Rate limits then key on `CF-Connecting-IP`, honoured only from Cloudflare's published ranges |
| `Hosting__PublicHosts` | Extra public host names, semicolon separated (a custom domain, for example). `appsettings.json` only allows `localhost`; the app also admits Render's own `RENDER_EXTERNAL_HOSTNAME` automatically. A host that is not admitted is answered `400 Invalid Hostname` — including Render's health check, which then never goes live |
| `Auth0__Authority` | `https://dev-2m805d64kx5233sh.us.auth0.com/` (trailing slash) |
| `Auth0__Audience` | `https://api.alpha6ops.dev` |
| `Auth0__ClientId` / `Auth0__ClientSecret` | The **Portal** (Regular Web Application) client |
| `ConnectionStrings__Accounts` | Neon pooled connection string, e.g. `Host=ep-….neon.tech;Port=5432;Username=…;Password=…;Database=alpha6_accounts;SSL Mode=Require;Channel Binding=Require` |
| `DataProtection__CertificateBase64` / `DataProtection__CertificatePassword` | Output of `scripts/new-keyring-certificate.ps1` |
| `Release__DownloadUrl` / `Release__Version` / `Release__Sha256` | Optional; enables `/Download` and the desktop Updates dialog once an installer is published |

Local development keeps using `dotnet user-secrets` and the 55439 PostgreSQL cluster; nothing above applies there.

## Step by step

1. **Neon** (console.neon.tech, free plan): create project `alpha6ops`, database `alpha6_accounts`. Copy the
   *pooled* connection string in .NET format (Connection Details → .NET). Migrations run at container start, so
   no manual SQL is needed.
2. **Certificate**: run `powershell -File scripts/new-keyring-certificate.ps1`; copy the two lines from
   `work/keyring-certificate.txt` into Render, then delete the file.
3. **Render** (dashboard.render.com, free plan): New → Blueprint → connect the GitHub fork
   `ReeceCz/alpha-6-ops`, branch `accounts`. Render reads `render.yaml`; fill the `sync: false` secrets when
   prompted. First deploy takes a few minutes (SDK image pull + publish). The service address is
   `https://<name>.onrender.com`.
4. **Auth0** (Applications → Alpha 6 OPS Portal): add `https://<name>.onrender.com/signin-oidc` to Allowed
   Callback URLs, `https://<name>.onrender.com/signout-callback-oidc` to Allowed Logout URLs, and the origin to
   Allowed Web Origins — keep the localhost entries. The Native (desktop) app needs no change.
5. **Verify**: `https://<name>.onrender.com/healthz` → `ok`; `/` shows the landing page; **Sign in** completes
   and `/Account` shows your workspaces (the database is empty: create the airline again or import the logbook).
6. **Desktop**: set `ApiBaseUrl` and `PortalUrl` in `alpha6-identity.json` to the Render address (keep a copy of
   the localhost version for development). Build the installer with `packaging/build-desktop.ps1`; publish it as
   a GitHub Release on the fork and set the three `Release__*` variables on Render so `/Download` works.
7. **Keep-warm**: on the fork, Settings → Secrets and variables → Actions → Variables →
   `ALPHA6_HEALTH_URL = https://<name>.onrender.com/healthz`. The workflow starts pinging on the next schedule.

## Custom domain through Cloudflare (`app.s4ops.com`, free)

The `s4ops.com` zone is on Cloudflare, and the portal stays on Render Free. Cloudflare proxies requests to it.
Order matters, because Render has to issue its certificate before Cloudflare starts proxying.

1. **Render** → service `alpha6ops` → Settings → Custom Domains → add `app.s4ops.com`. Render shows the CNAME
   target (`alpha6ops.onrender.com`).
2. **Cloudflare** → `s4ops.com` → DNS → add `CNAME app → alpha6ops.onrender.com` with **Proxy status: DNS only**
   (grey cloud). Make sure there are no `AAAA` records for `app`.
3. Wait until Render shows the certificate as issued for `app.s4ops.com`. This usually takes a few minutes.
4. **Cloudflare** → SSL/TLS → Overview → encryption mode **Full (strict)**. Then switch the `app` record to
   **Proxied** (orange cloud). Turn on SSL/TLS → Edge Certificates → **Always Use HTTPS**.
5. **Render environment**: set `Hosting__PublicHosts = app.s4ops.com` and `Hosting__TrustCloudflare = true`,
   which are also in `render.yaml`. Without the host, every request under the new name is answered
   `400 Invalid Hostname`. Without Cloudflare trust, all visitors share the rate-limit bucket of the Cloudflare
   edge they arrive through. `CF-Connecting-IP` is honoured only when the connecting peer is in Cloudflare's
   published ranges (`src/Alpha6Ops.Server/CloudflareNetworks.cs`), so requests sent straight to
   `onrender.com` can't forge it.
6. **Auth0** → Applications → Portal app: add `https://app.s4ops.com/signin-oidc` to Allowed Callback URLs,
   `https://app.s4ops.com/signout-callback-oidc` to Allowed Logout URLs, and `https://app.s4ops.com` to Allowed
   Web Origins. Keep the Render and localhost entries until cutover.
7. **Verify**:
   - `https://app.s4ops.com/healthz` → `ok`.
   - Sign in and out under the new name.
   - The response headers include `cf-ray`, which shows the request went through Cloudflare.
8. **Keep-warm**: point `ALPHA6_HEALTH_URL` at `https://app.s4ops.com/healthz` so the ping also exercises the
   Cloudflare path.

Until the static landing page exists, add a Cloudflare **Redirect Rule** (Rules → Redirect Rules) that sends
`s4ops.com/*` and `www.s4ops.com/*` to `https://app.s4ops.com/${1}` with a 302. The redirect needs proxied
placeholder records for the apex and `www`: an `A` record to `192.0.2.1`, orange cloud. Switch the redirect to
301 once the landing page is final.

The desktop keeps using the Render address until its `ApiBaseUrl`/`PortalUrl` point at `https://app.s4ops.com`.
Both names reach the same service.

## Rotating secrets

- Auth0 client secret: rotate in Auth0, update `Auth0__ClientSecret`, redeploy.
- Key-ring certificate: generate a new one, add it as the new `DataProtection__CertificateBase64`; existing
  sessions become invalid (pilots sign in again). There is no cross-certificate re-encryption.
- Neon password: reset in Neon, update the connection string, redeploy.

## Moving off the free tier

The same image and variables run on Fly.io, Azure App Service or a VPS with Docker Compose. Give the container a
persistent volume and set `DataProtection__KeyDirectory` if you prefer file-based keys; otherwise the database
key ring keeps working as-is.
