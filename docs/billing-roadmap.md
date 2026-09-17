# Billing roadmap: subscriptions with Stripe and Link

Status (17 September 2026): **designed, not built.** The public site shows the levels
and how payment will work (`/Levels`, no prices shown until they are decided); every level is complimentary during early access. This
note records the decisions already taken so the build is mechanical when it starts.

## Decisions

- **Provider: Stripe, with Link.** Link is Stripe's one-tap wallet, not a separate processor. It appears
  automatically in Stripe Checkout and the Payment Element; customers without Link get a card form
  (plus Apple Pay / Google Pay) on the same page. Alpha 6 never handles card data.
- **Hosted Checkout + Stripe Billing + Customer Portal.** No custom payment UI. Checkout creates the
  subscription; the Customer Portal handles card changes, invoices and cancellation.
- **SDK: `Stripe.net` in `Alpha6Ops.Server` only** (approved 17 September; not yet added — the repo rule
  is no new NuGet packages until the feature is actually built).
- **Products** (prices set in the Stripe dashboard, not shown on `/Levels` until decided):
  Premium pilot monthly and yearly; Pro airline monthly and yearly. Free and Community stay free.

## How it maps onto what exists

The data model already carries the outcome of billing, so nothing in the desktop, the portal or the
capability checks changes:

| Today | With billing |
| --- | --- |
| `user_account.Plan` / `SubscriptionStatus` / `SubscriptionExpiresAt` | Set by the webhook from the subscription (`active`, `canceled`, `expired`; `ExpiresAt` = `current_period_end`) |
| `virtual_airline.Plan` / `SubscriptionStatus` / `SubscriptionExpiresAt` | Same, for the owner's airline subscription |
| `SetPersonalPlanAsync` / `SetAirlinePlanAsync` (complimentary) | Kept for early access and for staff grants; refused with `billing_required` once billing is live |
| `SubscriptionStatuses.IsCurrent` | Unchanged — `complimentary` and `active` both count |

New columns (one migration): `user_account.StripeCustomerId`, `user_account.StripeSubscriptionId`,
`virtual_airline.StripeSubscriptionId`, and a `billing_event` table keyed by Stripe event id for
idempotent webhook processing.

## Server work

1. `BillingSettings` (`Stripe:SecretKey`, `Stripe:WebhookSecret`, price ids) in user-secrets; `IsConfigured`
   false keeps the complimentary path and hides Subscribe buttons.
2. `POST /Account/Plan?handler=Checkout` and `POST /Account/Airline/{id}?handler=Checkout` (antiforgery,
   verified email, owner-only for airlines): create or reuse the Stripe customer (email + account id in
   metadata), create a Checkout Session in `subscription` mode with `success_url=/Account/Plan?checkout=success`
   and `cancel_url=/Account/Plan`, redirect. Link needs nothing extra.
3. `POST /billing/webhook` (anonymous, outside the cookie/antiforgery pipeline, raw body, signature check with
   `Stripe-Signature`): handle `checkout.session.completed`, `customer.subscription.created|updated|deleted`,
   `invoice.payment_failed`; write the status fields inside `AccountsService.Execute` and audit
   `account.subscription_changed` / `airline.subscription_changed`. Ignore unknown events; never trust
   metadata for authorization — look the account up by customer id.
4. `POST /Account/Plan?handler=Billing` → Customer Portal session → redirect ("Manage billing").
5. Tests: fake Stripe HTTP handler in `Alpha6Ops.Server.Tests`; signed webhook fixtures (valid, tampered,
   replayed); status transitions on the accounts suite.

## Desktop work

`PlanSelectionWindow` shows "Subscribe on the web" (opens `{PortalUrl}/Account/Plan`) when bootstrap says
billing is configured — one boolean added to `BootstrapResponse` — and keeps the current complimentary
choice otherwise. No payment UI in the desktop.

## Not doing

- No stored card data, no custom card forms, no PayPal.
- No per-seat airline pricing: an airline subscription covers every member.
- No free trials with a clock; early access is complimentary until public release.
