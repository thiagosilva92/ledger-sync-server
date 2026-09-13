# ADR 0001: Minimal APIs, not MVC controllers

Date: 2026-09-13
Status: Accepted

## Context

This server exposes exactly two HTTP operations (`POST /events`,
`GET /events`), deliberately knows nothing about the ledger's domain
(see the main README's "Why this server doesn't need to understand the
ledger's domain"), and stores payloads verbatim rather than binding them
to typed request/response models beyond the one field it reads
(`eventId`).

## Decision

Use ASP.NET Core Minimal APIs (`MapPost`/`MapGet` in
`EventsEndpoints.MapEventsEndpoints`), not MVC controllers with
attribute routing.

## Consequences

- No controller class, action filters, or model-binding conventions for
  a surface this small — the endpoint definitions are two method calls
  with `.RequireAuthorization()`, `.RequireRateLimiting(...)`, and
  `.WithSummary()`/`.WithDescription()` chained on, immediately readable
  top to bottom in one file.
- Endpoints work directly with `JsonElement`, matching the deliberate
  choice not to model the ledger's event payload as a typed DTO — MVC's
  model-binding pipeline is built around typed models and adds no value
  when the server intentionally stores "whatever it was handed."
- Trade-off: MVC's richer conventions (model validation attributes,
  action filters, versioned API controllers) aren't available if the
  surface grows significantly — acceptable for a server whose entire
  contract is two operations, revisit if that changes.

## Alternatives considered

- **MVC controllers** — more structure than two endpoints need; would
  add ceremony (a controller class, routing attributes) without a
  corresponding benefit at this scope.
