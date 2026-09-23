# S4 OPS — Production hosting options on Cloudflare

Status: **free-tier hybrid (§4) is the current direction** (Reece, 2026-09-22); options A/B/C are kept for when paying users arrive. Prices were checked against official pages on
2026-09-22 and are in USD. The page for each figure is linked at the end.

## 1. What exists today

### `s4ops.com` (checked with public DNS on 2026-09-22)

- The zone is on Cloudflare nameservers: `bruce.ns.cloudflare.com` and `heidi.ns.cloudflare.com`.
- There are no A, AAAA or MX records for the apex, `www`, `app` or `api`, so nothing is served and no mail is
  received.
- The repo has no Cloudflare configuration: no `wrangler.*`, Pages, `_headers` or `_redirects`.
- **Needed from Reece:** screenshots or a list covering the account plan (Free or Workers Paid), existing
  Workers/Pages projects, R2/D1/KV, Turnstile widgets, and the registrar. The Cloudflare MCP isn't connected (see
  PROGRESS.md), so I can't see the dashboard.

### What the server needs from any host

| Piece | Today | Needs on Cloudflare |
|---|---|---|
| Portal + `/api/v1` (`Alpha6Ops.Server`) | ASP.NET Core 10 container on Render free tier. It sleeps after 15 minutes idle; Render quotes about a minute to spin up. | Something that runs a .NET 10 container **or** a rewrite. A plain Worker can't run .NET. |
| Account store | Neon Postgres (free tier). EF Core + Npgsql. 6 migrations with Postgres functions and triggers, `bytea`, `pg_advisory_xact_lock`. | Postgres reachable from the app runtime |
| Media (avatars, airline logos) | `media_blob.bytea` in Postgres, 1 MB or less each, served with ETag/304 | Nothing extra. It moves with the database. R2 is optional later. |
| Data-protection key ring | DB table `data_protection_key`, encrypted with a certificate held in an env var | Nothing extra. The container is **stateless**, so ephemeral disk is fine. |
| Installer downloads | `Release__DownloadUrl` points at an external URL (a GitHub Release is planned) | R2 bucket + `downloads.s4ops.com` (optional). R2 has free egress. |
| Secrets | Render environment | `wrangler secret` / container env |
| Keep-warm | GitHub Actions cron | Not needed if the instance is kept warm or cold starts are acceptable |
| Identity | Auth0 dev tenant | Unchanged by this decision. Covered in Phase 4. |

The server keeps no state on local disk. That's the key fact that makes a container-based move cheap.

## 2. Runtime options

### A. Cloudflare Containers running the existing Dockerfile (recommended once paying)

A Worker on `app.s4ops.com` routes every request to a container built from
`src/Alpha6Ops.Server/Dockerfile`.

- **Status:** generally available on Workers Paid. Images must be `linux/amd64`, which the Dockerfile already
  produces. Disk is ephemeral ("When a Container instance goes to sleep ... it will have a fresh disk"). Cold
  starts are "often in the 1-3 second range" (Render Free: about a minute).
- **Instance size:** `basic` (1/4 vCPU, 1 GiB, 4 GB) is the realistic minimum for ASP.NET + EF. At 256 MiB,
  `lite` is too tight.
- **Cost:** $5/month Workers Paid, which includes 25 GiB-h memory, 375 vCPU-min and 200 GB-h disk.
  - One `basic` instance kept on 24/7 (730 h) is roughly $5 + $6.35 (memory) + ≤ $12.70 (vCPU) + $0.70 (disk),
    so **≤ about $25/month**. The vCPU term is the upper bound: it assumes billing on allocated rather than used
    CPU, which should be confirmed on the first invoice.
  - With `sleepAfter` (scale to zero) and low traffic, it's close to $5.
- **Scaling:** 10 → 100 users fits one `basic` instance. 1,000 users would mean `standard-1` or two instances,
  about $30–60/month.
- **Lock-in: low.** The unit is a standard Docker image. The only Cloudflare-specific code is the small routing
  Worker.
- **Migration effort: small.** Add `wrangler.jsonc` and a routing Worker (TypeScript, about 30 lines), set the
  secrets, and add `app.s4ops.com` to `Hosting__PublicHosts`. The app code doesn't change.
- **Operational burden:** low. No OS to patch, and rollouts come from `wrangler deploy`.
- **⚠ Must spike before committing:** the outbound networking docs say that only HTTP/HTTPS traffic on ports
  80/443 goes through outbound handlers. They don't clearly say whether a raw TCP connection to Postgres on
  **5432** leaves the container by default. Npgsql needs 5432. The spike is to deploy the image to a throwaway
  container and hit `/healthz` with a real connection string, which takes about an hour. If 5432 is blocked, the
  fallbacks are B, or reaching the DB through a Cloudflare Tunnel / Workers VPC service.

### B. A .NET host (VM or paid container platform) behind Cloudflare

Examples are a small Hetzner/DigitalOcean VM running Docker, or Render's paid tier. The host is reached through a
**Cloudflare Tunnel** (no open inbound ports) or the orange-cloud proxy.

- **Cost:** a VM with 1–2 vCPU and 2 GB costs about $5–12/month plus a database. Render Starter costs $7/month
  (not rechecked today).
- **Lock-in:** lowest.
- **Effort:** small for Render paid; medium for a VM (Docker, Tunnel, updates, backups).
- **Burden:** you own OS patching, TLS inside the Tunnel, and restarts. That's the most of the three.
- **Fit:** "on Cloudflare" only at the edge (DNS, WAF, rate limiting, Turnstile, Access). The app itself runs
  elsewhere.

### C. Rewrite the portal and API as Workers (TypeScript) + D1

- **Cost:** the cheapest at scale. $5/month covers 10M requests; D1's paid tier includes 25B reads, 50M writes
  and 5 GB.
- **Lock-in: highest.** D1 is available only through Workers bindings.
- **Effort: very large.** It replaces the Razor portal (about 20 pages), `AccountsService` and its rules, the EF
  store, the Auth0 OIDC integration, and 254 server/account checks. The Postgres-specific pieces (triggers,
  functions, advisory locks, `bytea`) all have to be re-designed for SQLite. Realistically that's weeks, and it
  resets the security review.
- **Burden:** low once done, but the risk of regressions in auth-critical code is high.
- **Fit:** the most "Cloudflare-native", but a .NET/WPF team would then be maintaining a TypeScript backend.

## 3. Database options

| Option | Works with | Cost (10 / 100 / 1,000 users) | Migration | Notes |
|---|---|---|---|---|
| **Neon Launch** (paid) | A, B | Usage-based with no minimum: $0.106/CU-h, $0.35/GB-month. At 0.25 CU with scale-to-zero, about $2–10 → $10–20 → $20–40/month. | **None.** Upgrade the existing project in place; the data, migrations and connection string stay. | 7-day point-in-time restore, no SLA. Scale-to-zero can be disabled. The free tier (100 CU-h, 0.5 GB) was the non-production part, not Neon itself. |
| **Neon Scale** | A, B | $0.222/CU-h, about 2× Launch | None | 30-day point-in-time restore, **SLA**, configurable always-on. The right tier for "production-grade". |
| Other managed Postgres (Supabase, RDS, DO Managed) | A, B | about $15–25/month and up | `pg_dump`/`pg_restore` (six migrations, small data) | More vendors to manage |
| Postgres on the same VM | B | Included in the VM | dump/restore | You own backups. Not recommended. |
| **Hyperdrive** | Workers only | Free/Paid | — | A pooling/cache **binding for Workers**. It doesn't apply to a .NET container, which connects straight to Postgres. Relevant only to C, or to future Worker endpoints. |
| **D1** | C only | Paid tier as above | Full data-model redesign | SQLite semantics. Not reachable from .NET. |

The six applied migrations and the live data carry over unchanged to any Postgres option. D1 would replace them.

## 4. Free-tier path (chosen direction 2026-09-22: stay at $0 as long as possible)

Cloudflare Containers needs Workers Paid ($5/month), so the free setup is a **hybrid**: Cloudflare does
everything at the edge, and the app stays on free Render + free Neon behind it.

| Hostname | Serves | Free tier used |
|---|---|---|
| `s4ops.com`, `www` | Public landing page as **static files on Cloudflare** (Workers static assets). No cold start, global CDN. | Workers Free |
| `app.s4ops.com` | The portal. Proxied CNAME → `alpha6ops.onrender.com`. | Render Free + Cloudflare proxy |
| `api.s4ops.com` | `/api/v1` for the desktop. The same Render service under a second custom domain, so it can get its own rate-limit rule. | Same |
| `auth.s4ops.com` | Auth0 custom domain | Auth0 Free (1 custom domain; card verification, no charge) |
| `downloads.s4ops.com` | Installer files | R2 Free (10 GB-month, free egress) |
| `support@s4ops.com` | Forwards to an inbox you already use | Cloudflare Email Routing (free) |

**Free-tier limits that matter (checked 2026-09-22):**

- **Render Free:**
  - Custom domains with managed TLS are supported.
  - The service spins down after 15 minutes idle, and spin-up takes about a minute.
  - Each workspace gets 750 instance hours a month. One service running 24/7 uses about 744, so the existing
    keep-warm ping fits, but only for **one** service.
  - Render itself says free services are not for production.
  - Free services can't send SMTP.
  - Cloudflare setup: add the record as **DNS only** first so Render can issue its certificate, then switch it
    to proxied. Use SSL/TLS mode **Full**, and have no AAAA records.
- **Neon Free:** 100 CU-hours and 0.5 GB per project. When a limit is hit, compute suspends or writes are
  blocked; data is never deleted. Images stored as `bytea` count against the 0.5 GB, so they move to R2 if that
  gets tight.
- **Auth0 Free:**
  - Up to 25,000 monthly active users, MFA, 1 custom domain, 5 Actions (2 are used today).
  - The built-in email provider is for testing only. Real verification and reset emails need your own SMTP
    sender on `s4ops.com`; several providers have free tiers.
- **Cloudflare Free:** DNS, proxy/CDN, universal TLS, managed WAF rules, Turnstile and Email Routing are free.
  The free plan allows only a small number of rate-limiting rules; confirm the count in the dashboard when
  creating them.

**Costs:** $0 a month. The code changes are small:

- Add the new host names to `Hosting__PublicHosts`.
- Add the Auth0 callback URLs.
- Trust Cloudflare's forwarded headers. `Hosting__BehindProxy` exists already, but proxy trust must stay
  limited to Render's hop.
- Build a static landing page in Phase 3.

**When to leave free:** move when paying users arrive or the one-minute wake-ups start costing sign-ups. The
same Docker image then moves to Cloudflare Containers ($5/month + usage, option A) or Render Starter, and Neon
moves up to Launch/Scale in place. No code changes are needed for either.

**Only fully-Cloudflare $0 option:** C (Workers + D1 rewrite). Weeks of work, and it throws away the tested
.NET server. Not recommended.

## 5. Paid recommendation (for later)

**A + Neon (Launch now, Scale when paying users arrive).** Run the unchanged server image on Cloudflare
Containers behind `app.s4ops.com`, keep the Postgres store on a paid Neon plan, and put Cloudflare's WAF, rate
limiting, Turnstile and (for staff) Access in front.

- It reuses everything that's built and tested: 254 server/account checks, the migrations and the live data.
- Everything runs on Cloudflare except the database. Neon is managed, has point-in-time restore, and offers an
  SLA tier.
- Estimated $10–30/month at 10–100 users.
- **Exit path:** the same image runs on B with no code change if Containers disappoints.
- **Condition:** the 5432 egress spike passes. If it doesn't, choose between B (a VM or Render paid, behind a
  Tunnel) and a Tunnel to the database.

Proposed hostnames:

| Hostname | Serves |
|---|---|
| `s4ops.com` + `www` | Public landing (the same portal, anonymous pages) |
| `app.s4ops.com` | Signed-in portal |
| `api.s4ops.com` | `/api/v1` for the desktop app (same container, separate host so it can be rate-limited separately) |
| `auth.s4ops.com` | Auth0 custom domain (Phase 4) |
| `downloads.s4ops.com` | R2 installer bucket |

**Decision needed from Reece:** A, B or C; the database; and whether the Workers Paid plan ($5/month) can be
turned on for the spike.

## Sources

- Containers pricing: https://developers.cloudflare.com/containers/pricing/
- Containers overview: https://developers.cloudflare.com/containers/
- Platform details (ephemeral disk, cold start, amd64): https://developers.cloudflare.com/containers/platform-details/
- Instance types and limits: https://developers.cloudflare.com/containers/platform-details/limits/
- Outbound traffic: https://developers.cloudflare.com/containers/platform-details/outbound-traffic/
- Workers, D1 and R2 pricing: https://developers.cloudflare.com/workers/platform/pricing/
- Hyperdrive: https://developers.cloudflare.com/hyperdrive/
- Neon pricing: https://neon.com/pricing
- Render free tier: https://render.com/docs/free
- Render with Cloudflare DNS: https://render.com/docs/configure-cloudflare-dns
- Auth0 pricing: https://auth0.com/pricing
