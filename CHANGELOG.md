# Changelog

Notable changes to this project, loosely following
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). There's been one
continuous line of development so far, no prior releases to diff against —
see `git log` for the exact commit-by-commit history this summarizes.

## [1.0.0] - 2026-09-13

### Added

- Domain layer (`Ledger.SyncServer.Domain`): `IncomingEvent`,
  `StoredEvent`, `EventDeduplication.SelectNew` — pure decision logic, no
  database or HTTP.
- Infrastructure layer: `PostgresEventLog` (EF Core + PostgreSQL) behind
  the `IEventLog` port, with a real, committed migration — see
  [ADR 0003](docs/adr/0003-postgres-ef-core-port-adapter.md).
- API host (Minimal APIs): `POST /events` (idempotent push, de-duplicated
  by `eventId`) and `GET /events` (cursor-paginated pull) — see
  [ADR 0001](docs/adr/0001-minimal-apis-not-mvc.md).
- API key authentication: hashed keys, constant-time comparison, no raw
  key ever stored — see
  [ADR 0002](docs/adr/0002-hashed-api-keys-not-oauth.md).
- Health checks: `/health/live` and `/health/ready`, deliberately
  answering different questions.
- Horizontal scaling demo: two API replicas behind a YARP gateway with
  active health-check-based failover, verified by actually stopping and
  restarting a replica — see
  [ADR 0005](docs/adr/0005-yarp-compose-not-kubernetes.md).
- Rate limiting: fixed-window, partitioned by API key (not IP) — see
  [ADR 0004](docs/adr/0004-rate-limit-by-api-key-not-ip.md).
- Structured logging (Serilog) and distributed tracing (OpenTelemetry +
  Jaeger), with log/trace correlation verified against real trace IDs.
- Resilient database connection (`EnableRetryOnFailure`, deliberately
  modest) and OpenAPI/Scalar documentation.
- Live public deployment on Azure Container Apps + PostgreSQL Flexible
  Server, deliberately simplified vs. the full local topology — see
  [ADR 0006](docs/adr/0006-azure-container-apps-simplified-deployment.md).
- Infrastructure as Code (`infra/main.bicep`) and a real CD pipeline,
  split into a routine `deploy` job and a manual, human-gated `infra`
  job — see
  [ADR 0007](docs/adr/0007-split-cd-from-infra-changes.md).
- Managed-identity ACR pull, replacing stored admin credentials — see
  [ADR 0008](docs/adr/0008-managed-identity-for-acr-pull.md).
- Dependabot (NuGet, Docker, GitHub Actions ecosystems) and CodeQL
  (scheduled + on push/PR).
- Load testing (NBomber, `tests/Ledger.SyncServer.LoadTests`), which
  surfaced a real finding about the rate limiter's in-memory,
  per-replica counting under horizontal scaling — see
  [ADR 0009](docs/adr/0009-nbomber-for-load-testing.md).
- 8 Architecture Decision Records (`docs/adr/`) documenting the
  non-obvious calls behind the codebase, retrospectively.

### Fixed

- A config-timing bug: the connection string was read eagerly right
  after `CreateBuilder()` instead of lazily inside `AddDbContext`'s
  callback, breaking every `WebApplicationFactory`-based test.
- PostgreSQL 18's official Docker image changed its volume-mount
  convention (a single parent-directory mount, not directly at
  `.../data`) — found by the container failing to start.
- A circular dependency in the first managed-identity rollout (the
  Container App's own provisioning needed a role assignment that
  couldn't be created until that same provisioning finished) caused a
  real ~20-minute outage, resolved live and documented in full in
  [ADR 0008](docs/adr/0008-managed-identity-for-acr-pull.md).
