# ledger-sync-server

[![CI](https://github.com/thiagosilva92/ledger-sync-server/actions/workflows/ci.yaml/badge.svg)](https://github.com/thiagosilva92/ledger-sync-server/actions/workflows/ci.yaml)
[![codecov](https://codecov.io/gh/thiagosilva92/ledger-sync-server/graph/badge.svg)](https://codecov.io/gh/thiagosilva92/ledger-sync-server)

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
- ✅ API host (`Ledger.SyncServer`) — Minimal APIs, endpoints extracted
  into `EventsEndpoints.MapEventsEndpoints`, not left inline in
  `Program.cs`. `POST /events` and `GET /events` accept and return raw
  `JsonElement`s rather than a typed DTO — this server only ever needs
  one field (`eventId`) out of what it's handed; everything else is
  stored verbatim and returned unchanged, which is what "doesn't
  understand the ledger's domain" (see above) actually looks like in the
  wire format. 11 integration tests: 6 against `PostgresEventLog`
  directly, 5 through real HTTP via `WebApplicationFactory<Program>` —
  the layer that catches wiring mistakes the direct tests can't see (see
  below).
- ✅ CI (GitHub Actions) — one job: restore, build in Release (where
  `TreatWarningsAsErrors` actually applies), `dotnet test` with coverage
  collection, upload to Codecov. No separate Docker setup step: GitHub's
  `ubuntu-latest` runners ship with Docker preinstalled, which is all
  `Testcontainers.PostgreSql` needs. Verified by running the exact same
  command sequence locally before trusting it in CI, the same discipline
  as every other checkpoint in this repo (and the client repo before it).

### A bug only a real HTTP call could have caught

`Program.cs` originally read `ConnectionStrings:SyncDatabase` and threw
immediately if it was missing — right after `WebApplication.CreateBuilder`,
before `Build()`. Every `EventsEndpointsTests` test failed with that exact
exception, not a test assertion failure: `WebApplicationFactory`'s
`ConfigureAppConfiguration` overlay (the test's connection string,
pointing at the Testcontainers instance) is applied during `Build()` —
after `CreateBuilder()` returns, which is exactly when the eager read ran.
The app's own configuration was still incomplete at the moment it asked
for a value it needed.

Fixed by moving the read *inside* `AddDbContext`'s configuration callback,
which EF Core doesn't invoke until something first resolves
`DbContextOptions<SyncDbContext>` from the container — well after
`Build()`, by which point every configuration source, test overlay
included, is actually in place. This isn't just a test-harness quirk: the
same ordering bug would have hit a real deployment reading its connection
string from an environment variable layered on late in `Program.cs`
before `Build()` — the integration test caught something the unit tests
(which never construct a `WebApplicationFactory` at all) structurally
could not.

## Running

```bash
dotnet build
dotnet test
```

Requires the .NET SDK version pinned in [global.json](global.json), and
Docker running locally (`tests/Ledger.SyncServer.IntegrationTests` needs
it for Testcontainers — `dotnet test tests/Ledger.SyncServer.UnitTests`
runs without it).

To run the API itself, `ConnectionStrings:SyncDatabase` must point at a
real PostgreSQL instance (no default is provided — a missing connection
string fails loudly at startup, not silently):

```bash
dotnet run --project src/Ledger.SyncServer -- --ConnectionStrings:SyncDatabase="Host=localhost;Database=ledger_sync;Username=postgres;Password=postgres"
```
