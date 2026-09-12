# ledger-sync-server

The server side of `event-sourced-ledger`'s sync story: a minimal ASP.NET
Core API that lets multiple devices exchange events through a shared
remote, playing the role `FakeSyncTransport` stands in for on the client
today.

> Fictional domain, same as the client repo. No code, data model or
> business logic is derived from any employer's product.

## What this server actually needs to do

Exactly what `SyncTransport` (the client-side interface it implements the
other end of) asks for — nothing more:

- `POST /events` — accept a batch of events, de-duplicated by `eventId`,
  idempotent to a retried call.
- `GET /events?after={sequence}&limit={n}` — return events after a
  sequence cursor, paginated.

## Why this server doesn't need to understand the ledger's domain

It stores an event's `eventId` and its raw payload, and nothing else. It
never decodes what's inside the payload — no `Account`, no
`LedgerTransaction`, no knowledge that "double-entry" is even a concept.
Everything this server is responsible for (deduplicate, sequence, page)
works identically no matter what the payload contains. That's a
deliberate boundary, not an oversight: it's what keeps this repository
decoupled from the mobile app's event schema, the same way
`DriftEventStore` on the client never needs to know what a "leg" is
either.

## Status

Built in dependency order, same discipline as the client repo: pure
domain logic first, verified with tests, before anything touches a
database or HTTP.

- ✅ `Ledger.SyncServer.Domain` — `IncomingEvent`, `StoredEvent`,
  `EventDeduplication.SelectNew` (pure decision logic: given the event
  ids already on record, which of an incoming batch are genuinely new,
  including within the same batch — a retried push can legitimately
  repeat an id). 6 unit tests, no database, no HTTP.
- ✅ Infrastructure — `SyncDbContext` + `EventRow` (EF Core mapping to a
  single `events` table: `Sequence` identity primary key, unique index on
  `EventId`) and `PostgresEventLog : IEventLog`, the only implementation
  of the port `Domain` defines, the same interface/adapter split
  `EventStore`/`DriftEventStore` uses on the client. A real, committed EF
  Core migration (`InitialCreate`), applied with `Database.MigrateAsync()`
  — not `EnsureCreatedAsync()`, which would prove nothing about whether
  the migration itself is correct. 6 integration tests run against a real
  PostgreSQL instance via Testcontainers (a fresh container per test,
  full isolation, no shared-database cleanup logic to get wrong):
  sequential sequence assignment, idempotent retry, mixed known/new
  batches, cursor pagination, and persistence across a fresh `EventLog`
  instance over the same database (the restart-survival proof, mirroring
  `DriftDeviceIdentityStore`'s equivalent test on the client).
- ⏳ API host (the two Minimal API endpoints) — next
- ⏳ CI (GitHub Actions, coverage via Codecov)

## Running

```bash
dotnet build
dotnet test
```

Requires the .NET SDK version pinned in [global.json](global.json), and
Docker running locally (`tests/Ledger.SyncServer.IntegrationTests` needs
it for Testcontainers — `dotnet test tests/Ledger.SyncServer.UnitTests`
runs without it).
