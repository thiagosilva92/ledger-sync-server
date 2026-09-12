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

Both require a valid API key in the `X-Api-Key` header — see
[Authentication](#authentication) below.

## Authentication

Every request needs `X-Api-Key: <key>`. There's no self-service "register
a device" endpoint — that would be its own chicken-and-egg authentication
problem — so keys are generated and configured out-of-band by whoever
runs the server, one per device.

Only a key's SHA-256 hash is ever configured or stored — never the key
itself, the same reason a password is never stored in plaintext.
Generate a new key and its hash (PowerShell):

```powershell
$keyBytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
$key = [Convert]::ToBase64String($keyBytes)
$hashBytes = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($key))
$hash = [Convert]::ToHexString($hashBytes).ToLower()

Write-Host "Key (give this to the device, never store it):`n$key"
Write-Host "Hash (put this in ApiKeys:Hashes on the server):`n$hash"
```

Add the hash to `appsettings.json`, an environment variable
(`ApiKeys__Hashes__0`), or user-secrets — a hash can't be reversed back
into the key it came from, but a real deployment's configuration still
shouldn't be committed to source control, for the same reason a
password's config file never is even though the hash inside it is
already one-way.

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

**MVP complete at this point**: push, pull, idempotent, cursor-paginated,
CI-verified against a real database. Everything below is the deliberate
post-MVP roadmap, not scope that was missing from day one.

- ✅ API key authentication — `ApiKeyAuthenticationHandler`, a proper
  ASP.NET Core `AuthenticationHandler<TOptions>` (not ad-hoc middleware),
  checking `X-Api-Key` against SHA-256 hashes in configuration via
  constant-time comparison (`CryptographicOperations.FixedTimeEquals`).
  No key is ever stored, only its hash — see
  [Authentication](#authentication) above. No self-service device
  registration endpoint on purpose (that's its own bootstrapping
  problem); keys are provisioned out-of-band by whoever runs the server.
  5 new tests (12 unit, 13 integration total): hash matching against one
  or several configured keys, missing/wrong key rejected with 401, and
  every existing endpoint test updated to authenticate — proving the new
  requirement doesn't just exist, it's actually enforced on the
  endpoints that matter.
- ✅ Health checks — `/health/live` and `/health/ready`, deliberately
  answering different questions: liveness runs zero checks (`Predicate
  = _ => false`) and only proves the process can respond to HTTP at
  all — a database outage is not a reason for an orchestrator to kill
  and restart a process that can't fix the database by restarting.
  Readiness runs `AddDbContextCheck<SyncDbContext>` (tagged `"ready"`),
  so a load balancer or orchestrator knows to stop sending this replica
  traffic the moment Postgres becomes unreachable. Neither endpoint
  requires an API key — the caller is infrastructure, not a device.
  4 integration tests, including one that stops the real Testcontainers
  Postgres mid-test and confirms readiness reports unhealthy (503)
  while liveness stays healthy (200) throughout — the split is proven,
  not just described.
- ⏳ Horizontal scaling demo (docker-compose, multiple replicas, a
  reverse proxy)
- ⏳ Rate limiting
- ⏳ Structured logging + OpenTelemetry tracing
- ⏳ Resilient database connection (`EnableRetryOnFailure`)
- ⏳ OpenAPI/Swagger
- ⏳ A real, public, deployed instance

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
