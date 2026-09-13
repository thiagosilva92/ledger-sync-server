# ADR 0003: PostgreSQL + EF Core behind a domain-owned port

Date: 2026-09-13
Status: Accepted

## Context

Event storage needs sequential id assignment, a uniqueness constraint on
`EventId` for idempotent retries, and cursor-paginated reads — all
well-served by a relational database with a real migration story. The
domain layer (`Ledger.SyncServer.Domain`) should not know or care which
database, or even that a database exists at all, so it stays testable
with zero I/O.

## Decision

`Domain` defines `IEventLog` as the port; `Infrastructure` provides the
only implementation, `PostgresEventLog`, backed by EF Core
(`SyncDbContext` mapping a single `events` table: `Sequence` identity
primary key, unique index on `EventId`) against PostgreSQL. A real,
committed EF Core migration (`InitialCreate`) is applied with
`Database.MigrateAsync()` in production and in the `docker-compose`
`migrator` service — never `EnsureCreatedAsync()`, which would prove
nothing about whether the migration itself is correct. This mirrors the
`EventStore`/`DriftEventStore` interface-and-adapter split on the client
repository.

## Consequences

- `Domain`'s 6 unit tests run with zero database and zero HTTP —
  `EventDeduplication.SelectNew`'s pure decision logic (given ids
  already on record, which of an incoming batch, including
  within-batch, are genuinely new) is fully verified without
  Testcontainers.
- Integration tests exercise the real migration and a real PostgreSQL
  instance via Testcontainers (a fresh container per test method — full
  isolation, no shared-database cleanup logic to get wrong), so the
  migration's own correctness is part of what's tested, not assumed.
- Swapping the storage engine later (a different relational database,
  or a fundamentally different storage model) only requires a new
  `IEventLog` implementation — nothing in `Domain` or the API host would
  change.

## Alternatives considered

- **A document/NoSQL store** — event rows here have no nested structure
  needing a document model; relational fits directly, and Postgres's own
  `ActivitySource` (OpenTelemetry instrumentation built into the Npgsql
  driver since v6) was a real, concrete benefit realized later during
  the observability checkpoint.
- **Dapper or raw SQL instead of EF Core** — would avoid EF's abstraction
  overhead, but loses the generated, versioned migration story this
  repository leans on directly (`dotnet ef migrations add`, applied and
  tested as a real upgrade path) for comparatively little benefit at
  this schema's size (one table).
