# ADR 0006: Azure Container Apps, and a deliberately simplified live topology

Date: 2026-09-13
Status: Accepted

## Context

A live, public instance was worth having — "a real, running instance,
not a screenshot" — but needed a platform whose free tier is genuinely
usable long-term for a portfolio project with no revenue, and a decision
about how much of the locally-proven topology (see
[ADR 0005](0005-yarp-compose-not-kubernetes.md)) to actually run
publicly.

## Decision

Deploy to Azure Container Apps, chosen specifically because its
Consumption plan has an **ongoing, no-expiration** free monthly grant
(180,000 vCPU-seconds, 2M requests) as of 2026 — unlike most competing
platforms' free tiers, which have either disappeared entirely or only
cover a first trial period. The live deployment runs one Container App
instance (`min-replicas 0`, scaling to zero when idle) plus a managed
PostgreSQL Flexible Server on the smallest Burstable SKU
(`Standard_B1ms`) — not the full gateway/2-replica/Jaeger topology
`docker-compose.yml` proves locally.

## Consequences

- Ongoing cost stays at effectively zero for a portfolio project with
  sporadic real traffic, at the price of a cold start on the first
  request after a quiet period — an accepted trade-off, not an
  oversight.
- The horizontal-scaling and failover capability is not re-demonstrated
  publicly, because doing so would mean a second replica and a gateway
  running 24/7 for no functional benefit over what's already provable
  locally (see ADR 0005) — a cost/complexity decision, explicitly not a
  capability gap.
- Choosing Azure specifically (over a generalist PaaS) meant absorbing
  Azure-specific rough edges directly rather than avoiding them: ACR
  Tasks disabled on trial subscriptions, and an eligibility check that
  briefly rejected sign-up outright — both documented as real obstacles
  in the main README rather than smoothed over.

## Alternatives considered

- **Railway** — simpler initial setup and a more generous immediate
  trial experience, but its free tier as of 2026 does not offer an
  equivalent ongoing, no-expiration grant — rejected specifically
  because "only usable during a trial period" conflicts with wanting a
  portfolio deployment to still be live and free a year from now, not
  just at demo time.
- **AKS / a self-managed VM running the full docker-compose stack** —
  would let the *public* deployment match the local topology exactly,
  at real ongoing cost (a always-on VM or cluster control plane) for a
  project with no production traffic to justify it.
