# ADR 0004: Fixed-window rate limiting partitioned by API key, not IP

Date: 2026-09-13
Status: Accepted

## Context

Rate limiting needs a partition key identifying "whose budget is this
request against." Several devices can share a carrier or home NAT IP
address, and a single device legitimately changes IP when it switches
networks (Wi-Fi to mobile data). Partitioning by IP would make those two
completely ordinary situations look identical to genuine abuse.

## Decision

Use ASP.NET Core's built-in `Microsoft.AspNetCore.RateLimiting` (no
external package) with a fixed-window limiter
(`RateLimitPartition.GetFixedWindowLimiter`), partitioned by the
authenticated device's API key hash — the same claim
`ApiKeyAuthenticationHandler` already sets during authentication (see
[ADR 0002](0002-hashed-api-keys-not-oauth.md)), so rate limiting
introduces no new identity concept of its own. `PermitLimit` and
`WindowSeconds` are configuration values, not constants, so tests can
configure a deliberately tiny limit (2 requests / 10s) to prove
enforcement without firing 101 real requests against a 100/minute
production default.

## Consequences

- Several devices behind one NAT share nothing — each has its own
  budget, tied to its own key, regardless of the IP it happens to
  connect from.
- One device switching networks keeps its own budget instead of
  resetting it by acquiring a fresh IP.
- Rate limiting can only apply after authentication succeeds — an
  unauthenticated request is rejected by `[Authorize]` first, which is
  the correct order for this API (there's no anonymous surface worth
  rate-limiting independently; `/health/*` and the OpenAPI docs are
  intentionally exempt, per the main README).

### Found later, by actually load testing it

See [ADR 0009](0009-nbomber-for-load-testing.md): the built-in limiter's
counters live in-memory, per process. In the 2-replica `docker-compose`
topology, one key's budget is enforced independently by each replica — a
real load test sending 135 requests to one key against a 100/60s limit got
zero `429`s, because YARP split them 67/68 across two replicas that have
never heard of each other's counts. The *effective* system-wide budget for
a key scales with replica count, not the configured `PermitLimit` alone.
This is a known, accepted limitation of an in-memory limiter under
horizontal scaling, not a bug in this decision's reasoning — closing it
for real would need a distributed counter (e.g. Redis-backed), which is
real infrastructure this portfolio server's actual scale doesn't warrant
taking on.

The live Azure deployment (see the main README's Deployment section)
currently runs a single instance, so this doesn't change its effective
behavior today — but scaling it to more than one replica without also
addressing this would carry the same limitation live, not just in the
local `docker-compose` demo.

## Alternatives considered

- **IP-based partitioning** — rejected for the shared-NAT and
  network-switching failure modes above.
- **Sliding-window or token-bucket limiter** — smoother throttling
  behavior at request-pattern boundaries, but adds complexity this
  server's actual load profile (occasional device sync, not
  high-frequency bursty traffic) doesn't currently need; a fixed window
  is simpler to reason about and to test deterministically.
