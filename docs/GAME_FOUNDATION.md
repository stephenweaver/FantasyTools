# Chaos Cards server foundation

This folder documents the production game foundation added alongside the existing authentication API.

## What is implemented

- PostgreSQL schema for leagues, teams, weeks, matchups, card definitions, individual card copies, card plays, lineup/score snapshots, standings, commissioner adjustments, and immutable events.
- Explicit card-copy states: `deck -> hand -> secret_selection -> locked -> revealed/played -> weekly_discard -> deck`.
- Server-side validation for ownership, matchup participation, target validity, deadline, two-card pre-week limit, and one-card live limit.
- Deterministic scoring pipeline with referenced-player slot replacement, defensive attack reduction/blocking, additive percentages per target, flat points, and explicit custom-handler deferral.
- A no-third-party-package executable check project covering additive cancellation, defense reduction, Bromance, card ownership, and weekly play limits.

## Transaction rule

Every state-changing command must run in a PostgreSQL transaction. The repository locks the weekly team state and selected card copy (`SELECT ... FOR UPDATE`) before validating. This prevents simultaneous requests from spending one card twice or bypassing a weekly limit.

The command transaction must:

1. Lock relevant rows.
2. Re-read authoritative state.
3. Validate using `CardPlayRules`.
4. Update the card copy and card play.
5. Append a `game_events` record.
6. Commit as one unit.

## History rule

`game_events` is append-only and protected from update/delete by a PostgreSQL trigger. Corrections are new events and `commissioner_adjustments` rows; old facts are never rewritten. Final matchup calculations are also stored as JSON snapshots.

## Remaining integration work

1. Add the PostgreSQL driver after Stephen confirms the database host and connection-secret convention.
2. Implement `IGameRepository` with serializable transactions and row locks.
3. Add authenticated API commands/queries and league-membership authorization.
4. Add R2 artwork upload using short-lived signed URLs.
5. Add Sleeper synchronization and lineup/score snapshots.
6. Add a hosted weekly lifecycle worker for lock, reveal, finalization, discard return, and replacement draws.
7. Connect the React application to the API instead of browser-local state.

## Environment variables expected later

```text
DATABASE_CONNECTION_STRING=Host=...;Database=fantasytools;Username=...;Password=...;SSL Mode=Require
CARD_ARTWORK_BUCKET=fantasytools-cards
SLEEPER_REFRESH_SECONDS=30
```

Do not place credentials in source control.
