# Tester feedback ("Report a problem") — design

Date: 2026-10-04 · Branch: `feat/launch-prep` · Approved in chat by Reece (scope and choices below).

## Why

Test pilots have no way to report a flight that was recorded wrongly, a bug, or an idea. Email isn't an option yet:
there's no public contact address. Testers are signed in anyway, so the report goes to the portal and is tied to the
account. Reece and Dan read it on a staff page.

## Decisions (from Reece)

- Only signed-in pilots can send reports. No anonymous form and no CAPTCHA.
- Reports are read on a staff page. Access is an allowlist from configuration (`Staff__Emails`) plus a recent MFA
  check. There's no general staff role.
- A report may carry one attachment of up to 5 MB: the desktop's exported test log (`.zip` or `.json`) or a
  screenshot (`.png` or `.jpg`).

**Change from the chat design:** attachments don't reuse `media_blob`. That table's CHECK constraints allow only
images of up to 1 MB, kinds `avatar`/`airline-logo`. Loosening a shared table for this is worse than a small
dedicated table with its own limits and a cascade delete.

## Data (migration `202610040002_Feedback`)

```
feedback
  Id uuid PK
  UserId uuid NOT NULL FK user_account(Id) ON DELETE RESTRICT
  Category integer NOT NULL CHECK (Category IN (0,1,2,3))   -- FlightRecording, Bug, Idea, Other
  Message varchar(4000) NOT NULL CHECK (length(btrim(Message)) >= 10)
  FlightNumber varchar(10) NOT NULL DEFAULT ''               -- optional, normalised like the logbook
  FlownOn date NULL                                          -- optional
  AppVersion varchar(40) NOT NULL DEFAULT ''                 -- copied from the profile's LastSeenVersion
  Status integer NOT NULL CHECK (Status IN (0,1))            -- New, Handled
  CreatedAt timestamptz NOT NULL
  HandledAt timestamptz NULL, HandledByUserId uuid NULL FK user_account(Id) ON DELETE RESTRICT
  CHECK ((Status = 1) = (HandledAt IS NOT NULL))
  INDEX (UserId, CreatedAt), INDEX (Status, CreatedAt)

feedback_attachment
  FeedbackId uuid PK FK feedback(Id) ON DELETE CASCADE
  FileName varchar(120) NOT NULL
  ContentType varchar(40) NOT NULL CHECK (IN ('image/png','image/jpeg','application/zip','application/json'))
  Bytes bytea NOT NULL CHECK (octet_length(Bytes) BETWEEN 1 AND 5242880)
  Sha256 varchar(64) NOT NULL
```

## Rules (`FeedbackRules`, pure, unit-tested in the accounts suite)

- **Message:** trimmed, 10–4000 characters.
- **Category:** must be a defined value.
- **Flight number:** optional, normalised like the logbook (upper case, no spaces).
- **Flown on:** optional, a date no later than today (UTC) and no earlier than 2020.
- **Attachment:** 1 byte to 5 MB. The type is decided by content, never by the file name:
  - PNG signature
  - JPEG `FF D8 FF`
  - ZIP `PK\x03\x04`
  - JSON: the whole file is UTF-8 that `JsonDocument` parses.
- **File name:** reduced to `[A-Za-z0-9._-]`, at most 120 characters, and given an extension that matches the
  detected type.
- **Rate limit:** at most 5 reports per account in a rolling hour (`feedback_rate_limited`, 429). It's counted in
  the database, so it holds across server instances.
- **Storage guard:** attachments are refused (`feedback_storage_full`, 507) once all feedback attachments together
  reach 200 MB. The report itself can still be sent, without the file. This protects the Neon Free 0.5 GB quota.

## Staff access (`StaffAccess`, beside `AccountAdmission`)

`StaffAccess.FromEmails("a@x; b@y")` builds a case-insensitive set. `IsStaff(actor)` is true only when the email
is in the list **and** verified. Every staff operation calls `IsStaff` (otherwise `not_found`, 404, so the page
isn't revealed) and then `AccountRules.RequireRecentMfa`. An empty or missing setting means no staff, and every
staff URL answers 404.

## Service (`AccountsService.Feedback.cs`)

- `SubmitFeedbackAsync(actor, FeedbackRequest, FeedbackFile?)` → `FeedbackView`. Writes the audit event
  `feedback.submitted`.
- `ListMyFeedbackAsync(actor)`: the pilot's own reports, newest first, with their status.
- `ListFeedbackAsync(actor, staff, FeedbackStatus? filter)`: staff only. Includes the reporter's display name,
  email and callsign.
- `GetFeedbackAsync(actor, staff, id)` and `GetFeedbackAttachmentAsync(actor, staff, id)`: staff only.
- `SetFeedbackStatusAsync(actor, staff, id, status)`: staff only. Marks a report handled or reopens it, and
  writes an audit event.
- **Deletion:** the purge deletes the user's feedback rows, and their attachments cascade with them.
- **Export:** the ZIP gains `feedback.json` (category, message, flight, dates, status, attachment file name). It
  holds no attachment bytes, because the pilot sent those files themselves.

## Web

- **`/Account/Feedback`** (pilot): the form, then "Your reports" with each report's status.
  - Form fields: category (radio), message, optional flight number and date, optional file.
  - Page size limit: `[RequestSizeLimit(5_600_000)]`.
  - After a successful send it redirects with a confirmation.
  - Errors use the existing `_AccountError` pattern.
- **`/Staff/Feedback`**: a table of reports, filtered to New (the default), Handled or All.
- **`/Staff/Feedback/{id}`**: the full report, an attachment download, and a "Mark handled" or "Reopen" button.
  - A missing recent MFA shows the existing "Complete a fresh security check" link.
  - Staff navigation shows only to staff.
- **Attachment download:** `Content-Disposition: attachment`, the stored content type, and `nosniff`. It's never
  shown inline.
- **Links:**
  - Account navigation: "Report a problem".
  - Help page: a card about reporting problems.
  - Download page: "If something goes wrong" now points at `/Account/Feedback` (the desktop log still comes
    from Settings → Export test log).
- **Privacy:** add "Problem reports" to "What we store".
- **Configuration:** add `Staff__Emails` to `docs/hosting.md`.

## Tests

- **Accounts suite, pure checks:**
  - Message, category, flight and date rules.
  - Attachment detection (real PNG/JPEG/ZIP/JSON headers pass; an SVG, an EXE, a renamed text file and >5 MB
    fail).
  - File name sanitising.
  - `StaffAccess` parsing, and that an unverified email is refused.
- **Accounts suite, database checks:**
  - Submit with and without an attachment.
  - The rate limit at the 6th report.
  - List-mine shows only your own reports.
  - Staff operations are refused for a non-staff user (`not_found`) and without recent MFA (`mfa_required`).
  - Handled, then reopened.
  - Purge removes the reports and attachments.
  - The export includes the reports.
  - The migration count and snapshot parity.
- **Server suite:**
  - The form needs sign-in and CSRF.
  - Posting a report with a file works.
  - `/Staff/Feedback` gives 404 for a non-staff user, and 404 when unconfigured.
  - It renders for a staff user with MFA.
  - The attachment downloads with `Content-Disposition: attachment`.
  - No inline styles or scripts.

## Out of scope

Email notifications (would need a mail provider account), replies to the pilot, screenshots captured by the desktop
app, and any change to the desktop app.
