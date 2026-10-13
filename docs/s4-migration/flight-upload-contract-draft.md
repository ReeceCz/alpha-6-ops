# Draft for Dan: completed flights to the web logbook

Status: **proposal, nothing built.** Written 13 October 2026 for a conversation with Dan. The desktop side touches
PIREP closeout, which is a protected area (`docs/SIERRA4-PROJECT-CONTINUITY.md`), so Dan decides the desktop design.

## Why

Testers fly with the desktop, but the web logbook only shows CSV imports. The server has accepted flights at
`POST /api/v1/me/flights` since September. The desktop never calls it.

## What exists today

**Server** (`AccountsService.AddFlightAsync`). The body is `FlightLogRequest`: flight number, origin, destination, ICAO type,
registration, departure UTC, arrival UTC, block minutes, flight minutes, distance (nm), landing rate (fpm), fuel used (kg),
network and notes. The flight is stored with `Source = "desktop"`. A repeat with the same departure, flight number,
origin and destination gets `409 flight_exists`.

**Desktop.** `flight` rows in `FlightHistoryDatabase` have a durable `id`, created at `BeginFlight`. Closeout
writes `pirep_submitted` once per completed row (`FinalizeFlight`). The continuity doc says duplicate detection
must use that identity, "not display strings or flight-number matching".

## Proposal

1. **Identity.** Send the history row `id` as a new `ClientFlightId` field. The server stores it with a unique index
   per pilot, and a repeat returns the stored flight with 200. The existing 409 rule stays for CSV imports. Server
   work: one migration, a contract field and tests (Reece).
2. **When.** Queue the upload in the same step as `pirep_submitted`, from the completed row. It must never run from
   presentation code, and it can't change the flight's outcome.
3. **Outbox.** Add one small JSON file per queued flight in the account workspace
   (`Data\accounts\<id>\…\outbox\`), written atomically. Send on enqueue, at start-up and after sign-in. Delete
   on 200 or 409. Keep it on network errors and 5xx, retrying with backoff. On 400, keep it, mark it failed and
   show it to the pilot.
4. **Fields.** Times come from authoritative OOOI: departure = OUT, arrival = IN, block = IN − OUT,
   flight = ON − OFF. Origin, destination, flight number and type come from the row/assignment. Registration and
   fuel are sent only if the coordinator has them. Never fill a field from a guess.
5. **Signed out / personal vs airline.** Upload only when the flight was flown in a signed-in account workspace,
   and to that account. Airline attribution waits for a later contract.

## Questions for Dan

1. Is the history row `id` the right identity, or is there a better completed-flight ID?
2. Can the upload be queued inside closeout, or should a separate observer pick up `pirep_submitted` events?
3. Which fields can closeout supply reliably today (landing rate, fuel, distance, registration)?
4. What does a pilot see when uploads are failing: a status in the account panel, the logbook, or both?
5. Should flights from before sign-in or before this release be offered as a one-off backfill, or skipped?
