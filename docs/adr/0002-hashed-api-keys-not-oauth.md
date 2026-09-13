# ADR 0002: Hashed, out-of-band API keys, not OAuth/JWT

Date: 2026-09-13
Status: Accepted

## Context

Every caller is a device belonging to a known, finite household — not a
public user base that needs self-service sign-up, third-party delegated
access, or token refresh flows. There is deliberately no self-service
"register a device" endpoint (see the main README's Authentication
section): that would just relocate the authentication problem one level
up, to "who's allowed to register a device."

## Decision

Authenticate every request with a static API key in the `X-Api-Key`
header, generated and distributed out-of-band by whoever runs the
server. Only the key's SHA-256 hash is ever configured or stored on the
server — the same reasoning a password is never stored in plaintext —
compared with `CryptographicOperations.FixedTimeEquals` so a timing
side-channel can't leak how much of a guessed key matched. Implemented
as a proper ASP.NET Core `AuthenticationHandler<TOptions>`
(`ApiKeyAuthenticationHandler`), not ad-hoc middleware, so it composes
normally with `[Authorize]`/`.RequireAuthorization()` and with the rate
limiter, which partitions by the same identity claim the handler sets.

## Consequences

- No token issuance, refresh, or expiry infrastructure to build,
  operate, or get subtly wrong — appropriate for a fixed device fleet
  provisioned by one operator, inappropriate the moment self-service
  sign-up is a real requirement.
- Key rotation is manual (generate a new key, update the hash, redeploy
  configuration) — acceptable at this scale, a real limitation if the
  device fleet grows past what one operator can manage by hand.
- The key hash doubles as the rate-limiting partition key with no
  additional identity concept: `RateLimitPartition.GetFixedWindowLimiter`
  keys off the same `ClaimTypes.NameIdentifier` claim
  `ApiKeyAuthenticationHandler` already sets during authentication.

## Alternatives considered

- **OAuth2 / JWT with a real identity provider** — the standard choice
  for a public-facing API with many independent users, but pure
  overhead here: there is no third party ever requesting delegated
  access, and standing up or depending on an identity provider for a
  fixed, operator-provisioned device list solves a problem this server
  doesn't have.
- **mTLS (client certificates)** — stronger cryptographically, but
  requires certificate provisioning and rotation tooling on every
  client device — meaningfully more operational burden than generating
  and distributing a random key, for a threat model (a lost or stolen
  device's key) an API key revocation already addresses.
