# ADR 0009: NBomber for load testing, run manually against docker-compose

Date: 2026-09-13
Status: Accepted

## Context

Nothing in this repo measured throughput or latency under concurrent load,
or proved the rate limiter (see [ADR 0004](0004-rate-limit-by-api-key-not-ip.md))
actually holds under real, simultaneous HTTP traffic rather than a
hand-timed two-request unit test. That's a real gap for a server whose own
README claims a horizontal-scaling story — "it scales" is an untested
assertion without a tool that generates the load to test it.

## Decision

Added `tests/Ledger.SyncServer.LoadTests`, a small console app using
[NBomber](https://nbomber.com/) (a .NET-native load-testing library, not
an external process like k6 or JMeter — same "idiomatic .NET, no separate
tool ecosystem" preference already behind this repo's choice of the
built-in `Microsoft.AspNetCore.RateLimiting` over an external package).
Two scenarios, run manually (`dotnet run -- throughput` /
`dotnet run -- ratelimit`) against the real `docker-compose` stack — not
part of `ci.yaml`, the same reasoning [`http_sync_transport_live_test.dart`](https://github.com/thiagosilva92/event-sourced-ledger/blob/main/test/sync/http_sync_transport_live_test.dart)
on the client repo is excluded from its default suite: a load test needs a
real, running multi-replica server, which CI doesn't stand up automatically.

- **`throughput`** — round-robins across five demo device keys doing
  realistic push-then-pull cycles, run against an elevated
  `RateLimiting__PermitLimit` (an environment variable override
  `docker-compose.yml` already exposes) specifically so the *rate limiter*
  isn't what's being measured.
- **`ratelimit`** — one device, deliberately exceeding its budget, run
  against the *default* limit (100/60s) — see Consequences below for what
  this one actually found.

## Consequences

- **Measured, not assumed**: `throughput` real run — 1,035 push+pull
  cycles (2,070 requests), 0 failures, 51.75 req/s sustained, push
  p50/p95/p99 = 8/22/44ms, pull p50/p95/p99 = 4/8/17ms, through the real
  YARP gateway across both real replicas.
- **A genuine finding, not the result I expected**: `ratelimit`'s real
  run sent 135 requests to one device key in 65 seconds against a 100/60s
  limit — and all 135 got `200`, zero `429`s. Splitting the request count
  by replica in the containers' own logs explained why: 67 landed on
  `api1`, 68 on `api2`. `Microsoft.AspNetCore.RateLimiting`'s fixed-window
  limiter keeps its counters **in-memory, per process** — each replica
  tracks its own independent 100-request budget for that key. In this
  2-replica topology, one device's *effective* system-wide budget is
  closer to 200/60s than the 100/60s the configuration and
  [ADR 0004](0004-rate-limit-by-api-key-not-ip.md) describe, and it scales
  with replica count. This was never visible in the existing integration
  tests (`WebApplicationFactory` runs one in-process instance) or the
  local docker-compose failover demo (which never generated enough
  traffic from one key to hit the limit) — only a real load test against
  the real multi-replica topology surfaced it.
- **This is a known, accepted limitation, not a fix made here**: closing
  it for real would mean a distributed counter (e.g. a Redis-backed rate
  limiter, sharing state across replicas) — real infrastructure and a new
  dependency, disproportionate to what this portfolio server needs to
  prove about rate limiting's *design* (partition by key, not IP). Recorded
  here, and in ADR 0004's own notes, so it's an documented, known trade-off
  rather than something someone discovers by accident in production.

## Alternatives considered

- **k6 / JMeter / Gatling** — mature, capable tools, but external to the
  .NET ecosystem this whole repo otherwise stays inside; NBomber gives the
  same load-generation and percentile-reporting capability as a C# console
  app referencing the server's own `ApiKeyHasher` directly (see
  `DeviceKeys.cs`), rather than a second language's tooling and its own
  separate script format.
- **Skipping load testing** — the option this ADR replaces; would leave
  the horizontal-scaling and rate-limiting design claims as untested
  assertions.
