# ledger-sync-server

[![CI](https://github.com/thiagosilva92/ledger-sync-server/actions/workflows/ci.yaml/badge.svg)](https://github.com/thiagosilva92/ledger-sync-server/actions/workflows/ci.yaml)
[![codecov](https://codecov.io/gh/thiagosilva92/ledger-sync-server/graph/badge.svg)](https://codecov.io/gh/thiagosilva92/ledger-sync-server)

**Live**: https://ledger-sync-api.mangocoast-d3d45471.brazilsouth.azurecontainerapps.io/scalar/v1
— a real, running instance on Azure Container Apps, not a screenshot. See
[Deployment](#deployment) below for what's actually running there versus
what only exists in `docker-compose.yml`, and why.

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

## Architecture Decision Records

The non-obvious calls behind this codebase — Minimal APIs over MVC,
hashed API keys over OAuth, PostgreSQL behind a domain-owned port,
rate limiting by API key instead of IP, YARP + Compose over Kubernetes,
the simplified Azure Container Apps deployment, splitting routine app
deploys from manual infrastructure changes, and pulling from ACR via
managed identity instead of stored admin credentials — are written up
with context, consequences, and rejected alternatives in
[docs/adr](docs/adr/README.md).

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
- ✅ Horizontal scaling demo — `docker-compose.yml` runs two explicitly
  named API replicas (`api1`, `api2`) behind `Ledger.SyncServer.Gateway`,
  a YARP reverse proxy round-robining between them. This is the payoff
  of every earlier post-MVP checkpoint, not a bolt-on: YARP's cluster is
  configured with an *active* health check against each replica's own
  `/health/ready` (see [Authentication](#authentication) above and the
  health checks entry below) — a replica that loses its Postgres
  connection gets pulled out of rotation automatically, not just marked
  unhealthy for someone to notice later.
  - A schema migration runs once, in a dedicated one-shot `migrator`
    service (`--migrate-only`, see `Program.cs`), before either replica
    starts — not as a side effect of each replica's own startup. With
    two replicas starting together, that's a real race (EF Core's
    migration history table gives no cross-process locking guarantee),
    not a hypothetical one.
  - Verified by actually running it, not just by the compose file
    parsing: `docker compose up`, pushed and pulled through the gateway
    with a real API key, confirmed both replicas execute SQL (proving
    round-robin, not one replica quietly doing all the work) by reading
    each container's own logs. Stopped `api1` outright and watched
    requests briefly return `502` until YARP's active health check
    caught up (its `ConsecutiveFailures` policy takes more than one
    10-second interval to act — a real, observed window, not
    instantaneous failover) and then routed 100% correctly to `api2`
    alone; restarted `api1` and confirmed it rejoined rotation on its
    own once healthy again, no manual step needed either direction.
  - Found and fixed a real environment issue along the way: PostgreSQL
    18's official image changed its volume convention (a single mount
    at `/var/lib/postgresql`, not directly at `.../data`) — discovered
    by the container actually failing to start, not by reading the
    changelog first.
- ✅ Rate limiting — ASP.NET Core's built-in `Microsoft.AspNetCore.RateLimiting`
  (no external package), a fixed-window limiter partitioned by the
  authenticated device's API key hash — never by IP, since several
  devices sharing a NAT/carrier IP shouldn't share one budget, and one
  device switching networks shouldn't reset its own. The key hash comes
  from a claim `ApiKeyAuthenticationHandler` already sets during
  authentication, so rate limiting adds no new identity concept of its
  own. Both `PermitLimit` and `WindowSeconds` are configuration, not
  constants — tests configure a deliberately tiny limit (2 requests /
  10s) rather than firing 101 real requests to prove a 100/minute
  production default gets enforced. 3 new integration tests (32 total:
  12 unit, 20 integration): within-limit requests succeed, exceeding it
  returns `429`, and two different device keys have independent,
  unaffected budgets.
- ✅ Structured logging + distributed tracing — Serilog (structured JSON
  to stdout, the shape a real log collector actually wants) and
  OpenTelemetry, in both the API and the gateway.
  - `Enrich.WithSpan()` puts the active `TraceId`/`SpanId` on every log
    line — the same trace ID Jaeger shows for that request, so a log
    line and its distributed trace can be pivoted between instead of
    being two disconnected systems that happen to describe the same
    call.
  - Tracing instrumentation is always on; only the OTLP *export* is
    conditional on `Observability:OtlpEndpoint` being configured — a
    plain `dotnet test`/`dotnet run` with no collector nearby never
    spends its life quietly retrying a connection to nobody.
  - Npgsql's own `ActivitySource` (built into the driver since v6) is
    just told to listen (`AddSource("Npgsql")`) — every SQL command
    lands in the same trace as the HTTP request that caused it, with no
    query-tracing code of this repo's own to maintain.
  - The gateway adds `HttpClientInstrumentation` for the outgoing call
    YARP makes to whichever replica it picked; .NET's automatic W3C
    `traceparent` propagation over `HttpClient` (no extra wiring) is
    what turns "gateway request" and "replica request" into *one*
    connected trace instead of two that happen to be about the same call.
  - `docker-compose.yml` adds a Jaeger all-in-one container (OTLP
    receiver + UI on `:16686`) as the local-demo stand-in for a real
    OTel Collector + backend. Verified by actually generating traffic
    and reading it back out of Jaeger's own API, not just by the wiring
    compiling: a real `POST /events` produced one trace
    (`1ec57fbd6890c3f242326c118f1a6171`) with four connected spans —
    `POST /{**catch-all}` (gateway's route match) → `POST` (the outgoing
    call to the replica) → `POST /events` (the replica handling it) →
    `postgresql` (the actual insert) — and a log line from that same
    request carried the identical `TraceId`, confirming the log/trace
    correlation actually works, not just that both features exist
    independently.
- ✅ Resilient database connection — `EnableRetryOnFailure` on the
  Npgsql connection, so a fleeting network blip or a Postgres failover
  doesn't fail a push/pull outright. Deliberately modest (3 attempts, 2s
  max delay), not Npgsql's own defaults (6 attempts, 30s): the same
  `SyncDbContext` backs `/health/ready`, and a health probe that could
  take up to 30s to report unhealthy during a real outage would
  undermine the failover story the horizontal-scaling checkpoint already
  proved — YARP's own active health check times a probe out at 5s. Two
  unit tests (14 total) prove the retrying execution strategy is
  actually configured (and that it's genuinely different from the
  default) — not a full failure/recovery drill, which would need timing
  a container outage precisely against a retry backoff window, exactly
  the kind of flaky-by-construction test this repo has avoided
  elsewhere.
- ✅ OpenAPI/Swagger — .NET's own OpenAPI document generation
  (`AddOpenApi()`, no Swashbuckle) plus Scalar for a browsable UI, the
  current idiomatic pairing for Minimal APIs. Both endpoints get a
  name, summary, and description via `.WithSummary()`/`.WithDescription()`,
  so the generated document says something real, not just "200 OK, body:
  object". The document and UI are public — same reasoning as the
  health endpoints, the caller is a developer or a tool, not a device
  with a key. 3 new integration tests (23 total): the document is valid
  JSON and describes both `/events` operations, and the UI page loads,
  all without an API key.
- ✅ A real, public, deployed instance — Azure Container Apps + a
  managed PostgreSQL Flexible Server, verified end-to-end against the
  live URL (health checks, auth rejection, push, pull, the OpenAPI
  docs) — see [Deployment](#deployment) for what's deliberately
  simplified versus the full local `docker-compose` topology, and for
  two real deployment obstacles found and fixed along the way.

**Every item in this repo's original roadmap, from the MVP through the
last post-MVP checkpoint, was done at this point.** Hardening continued
afterward — Infrastructure as Code and a real CD pipeline, Architecture
Decision Records, managed-identity ACR pull (see
[docs/adr](docs/adr/README.md) and [Deployment](#deployment) for both)
— and automated dependency/security scanning:

- ✅ Dependabot (`.github/dependabot.yml`) — weekly update PRs for NuGet
  packages (respecting Central Package Management), the two Dockerfiles'
  base images, and the GitHub Actions themselves.
- ✅ CodeQL (`.github/workflows/codeql.yml`) — static security analysis
  on every push/PR to `main` plus a weekly scheduled scan (catches a
  newly-disclosed vulnerability in code that hasn't changed, not just a
  new one introduced by a change). A manual build step mirrors
  `ci.yaml`'s own restore/build exactly, rather than trusting CodeQL's
  autobuild to figure out a multi-project solution with Central Package
  Management on its own.

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

## Deployment

The [live instance](https://ledger-sync-api.mangocoast-d3d45471.brazilsouth.azurecontainerapps.io/scalar/v1)
is deliberately a simplified deployment, not the full `docker-compose.yml`
topology: one Container App instance (scales to zero when idle) plus a
managed PostgreSQL Flexible Server, not the gateway/2-replica/Jaeger
stack. That's a cost and complexity decision, not a capability gap — the
horizontal-scaling story (round-robin, active health-check failover) is
fully built and already proven in this README's own
[horizontal scaling](#status) section; a live public instance of *that*
specific topology would need a second replica and a gateway running
24/7, for no benefit beyond "look, it's the same thing you can already
see running locally."

Deployed to Azure because Azure Container Apps' Consumption plan has an
**ongoing, no-expiration** free monthly grant (180,000 vCPU-seconds,
2M requests) — unlike most competing platforms' free tiers as of 2026,
which have either disappeared entirely or only cover a first trial
period. Resources: a Container Apps environment, one Container App
(`min-replicas 0`, so it scales to zero and costs nothing while idle —
the trade-off is a cold start on the first request after a quiet
period), and a PostgreSQL Flexible Server on the smallest Burstable SKU
(`Standard_B1ms`).

### Two real obstacles hit deploying this, not hypothetical ones

**ACR Tasks are disabled on new trial subscriptions.** `az containerapp up
--source .` normally builds the image remotely via Azure Container
Registry's build service (ACR Tasks) — Azure blocks that specific
capability for brand-new trial subscriptions as an anti-abuse measure
(it's a plausible cryptomining vector), returning
`TasksOperationsNotAllowed`. Worked around by building the image locally
with the same Docker installation `docker-compose` already needs, then
`docker push`-ing it straight to a manually-created ACR — registry
push/pull isn't gated the same way the remote build service is.

**`--migrate-only` swallowed the next argument.** Running the compiled
binary directly against the Azure database
(`Ledger.SyncServer.dll --migrate-only "--ConnectionStrings:SyncDatabase=..."`)
threw the exact "missing connection string" exception the earlier
config-timing bug did — for an unrelated reason this time.
.NET's command-line configuration provider treats an argument with no
`=` (like `--migrate-only`) as expecting its value in the *next* token,
so it silently consumed the entire connection string as `--migrate-only`'s
value, leaving `ConnectionStrings:SyncDatabase` never set. Not a bug in
this repo's code — `args.Contains("--migrate-only")` (a raw string
check, not configuration-based) still worked exactly as designed, which
is why `docker-compose.yml`'s `migrator` service (connection string via
an environment variable, `--migrate-only` as the *only* command-line
argument) was never affected. Fixed for the one-off manual migration
by putting the self-contained `--key=value` argument first and the
bare flag last.

### From manual CLI commands to a real pipeline

Everything above was done by hand, one `az` command at a time — which is
honest about how the first deploy actually happened, but not
reproducible: recreating the resource group today would mean re-reading
this section rather than running something. [`infra/`](infra/) closes
that gap:

- **[`infra/main.bicep`](infra/main.bicep)** — the same resources
  described above (Container Apps environment, Container App, ACR,
  PostgreSQL Flexible Server), as code. Validated against the live
  environment with `az deployment group what-if` — see
  [`infra/README.md`](infra/README.md#known-deliberate-diffs-from-the-live-environment)
  for the two harmless, understood diffs that showed up (an
  auto-generated Log Analytics workspace name and a timestamped
  firewall-rule name), rather than papering over them.
- **A `deploy` job in [`ci.yaml`](.github/workflows/ci.yaml)** — on every
  merge to `main`: builds the image, pushes it to ACR, updates the
  Container App to the new tag. Authenticates via an OIDC federated
  Azure AD app registration, not a stored client secret.
- **[`infra.yaml`](.github/workflows/infra.yaml)** — applying
  `main.bicep` is a separate, `workflow_dispatch`-only pipeline
  (defaults to a `what-if` preview; applying requires explicitly
  checking a box), so that a routine code push can never touch the
  database, its password, or resource SKUs. Infra changes are rare and
  deliberate; app deploys are frequent and automatic — the pipeline
  keeps that distinction rather than collapsing both into "push to
  main."

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

Once it's running, the API's own docs are at `http://localhost:5000/scalar/v1`
(or whatever port `dotnet run` prints) — no API key needed to view them.

### Running the full stack (2 API replicas + gateway + Postgres)

```bash
docker compose up --build
```

Brings up Postgres, Jaeger, runs the schema migration once, starts both
API replicas, and starts the gateway on `http://localhost:8080` — the
only port exposed to the host; `api1`/`api2` are only reachable from
inside the compose network, through the gateway. Jaeger's UI is at
`http://localhost:16686` — every service exports traces to it.

The compose file bakes in the hash of a fixed demo key,
`demo-local-only-key` — fine for `docker compose up` on your own machine,
never how a real deployment would handle secrets (see
[Authentication](#authentication)):

```bash
curl -X POST http://localhost:8080/events \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: demo-local-only-key" \
  -d '[{"eventId":"evt-1","aggregateId":"acc-1","eventType":"demo","timestamp":"1000-0-node","payload":{"hello":"world"}}]'

curl "http://localhost:8080/events?after=0" -H "X-Api-Key: demo-local-only-key"
```

On Windows PowerShell, `curl` is aliased to `Invoke-WebRequest`, which
doesn't accept this syntax, and PowerShell's own argument quoting mangles
embedded `"` characters passed to `curl.exe` directly — found by actually
trying it, not assumed. Use `Invoke-RestMethod` instead:

```powershell
$headers = @{ "X-Api-Key" = "demo-local-only-key" }
$body = '[{"eventId":"evt-1","aggregateId":"acc-1","eventType":"demo","timestamp":"1000-0-node","payload":{"hello":"world"}}]'
Invoke-RestMethod -Uri "http://localhost:8080/events" -Method Post -Headers $headers -ContentType "application/json" -Body $body
Invoke-RestMethod -Uri "http://localhost:8080/events?after=0" -Headers $headers
```

Open `http://localhost:16686`, search for service `ledger-sync-server-gateway`,
and the request above shows up as one trace spanning the gateway's route
match, the outgoing call to whichever replica handled it, that replica's
own `POST /events`, and the Postgres command it issued — four spans, one
trace, two services.

`docker compose down -v` tears everything down, including the Postgres
volume.

## Load testing

`tests/Ledger.SyncServer.LoadTests` is a small [NBomber](https://nbomber.com/)
console app — not part of `dotnet test` or `ci.yaml`, since it needs a
real, running server, the same reasoning the client repo's
`http_sync_transport_live_test.dart` is excluded from its own default
suite. See [ADR 0009](docs/adr/0009-nbomber-for-load-testing.md) for why
NBomber over an external tool like k6.

```bash
docker compose up -d
dotnet run --project tests/Ledger.SyncServer.LoadTests -c Release -- ratelimit
docker compose down

RATE_LIMIT_PERMIT=100000 docker compose up -d
dotnet run --project tests/Ledger.SyncServer.LoadTests -c Release -- throughput
docker compose down
```

**`throughput`** — real measured numbers from an actual run, round-robining
push-then-pull cycles across five demo devices through the real 2-replica
YARP gateway: **2,070 requests, 0 failures, 51.75 req/s sustained**; push
latency p50/p95/p99 = 8/22/44ms, pull p50/p95/p99 = 4/8/17ms.

**`ratelimit`** — run against the *default* 100-requests-per-60-seconds
limit, sending 135 requests from one device in 65 seconds. The result
wasn't the one the scenario was written to demonstrate:

> All 135 requests got `200`. Zero `429`s — even though 135 > 100.

Splitting the count by replica in the containers' own logs explained why:
67 landed on `api1`, 68 on `api2`. `Microsoft.AspNetCore.RateLimiting`'s
fixed-window limiter keeps its counters **in-memory, per process** — each
replica independently tracks its own 100-request budget for that key.
Across this 2-replica topology, one device's *effective* system-wide
budget is closer to 200/60s than the configured 100/60s, and it scales
with replica count. Nothing in the existing test suite could have caught
this: `WebApplicationFactory`-based integration tests run one in-process
instance, and the local failover demo never sent enough traffic from one
key to approach the limit. Only a real load test against the real
multi-replica topology surfaced it — see
[ADR 0004](docs/adr/0004-rate-limit-by-api-key-not-ip.md#found-later-by-actually-load-testing-it)
for why this is a documented, accepted trade-off (a true fix needs a
distributed counter, e.g. Redis-backed) rather than something left for
someone to discover by accident in production.

## License

[MIT](LICENSE). See [CHANGELOG.md](CHANGELOG.md) for the history of
notable changes.
