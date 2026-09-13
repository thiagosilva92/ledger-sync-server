# ADR 0005: YARP + Docker Compose for horizontal scaling, not Kubernetes

Date: 2026-09-13
Status: Accepted

## Context

The horizontal-scaling checkpoint needs to demonstrate multiple replicas
behind a load balancer with health-check-based failover — a real,
provable capability, not just a diagram. The question is what
infrastructure proves it at a scope matched to what this project
actually is: a two-endpoint sync server with one dependency (Postgres).

## Decision

`docker-compose.yml` runs two explicitly named API replicas (`api1`,
`api2`) behind `Ledger.SyncServer.Gateway`, a YARP reverse proxy
round-robining between them with an *active* health check against each
replica's own `/health/ready`. A one-shot `migrator` service applies the
schema migration once, before either replica starts, avoiding the real
race two replicas migrating concurrently would otherwise hit (EF Core's
migration history table gives no cross-process locking guarantee).

## Consequences

- The failover story is provable with `docker compose up` on a laptop
  and Docker Desktop, nothing else — stopping `api1` and watching
  requests briefly `502` until YARP's active health check catches up,
  then route cleanly to `api2` alone, is a live demonstration anyone
  cloning this repo can reproduce in minutes.
- The live public deployment (see the main README's Deployment section)
  intentionally does **not** run this topology — it runs one instance
  that scales to zero, because a second replica and a gateway running
  24/7 publicly would cost real money for no benefit beyond "look, it's
  the same thing you can already see running locally." The
  docker-compose topology and the live deployment are different
  environments answering different questions on purpose (see
  [ADR 0007](0007-split-cd-from-infra-changes.md)'s reasoning for a
  related environment split).
- No cluster control plane, no YAML-templating tool, no separate
  operations skill set (kubectl, Helm charts, ingress controllers) to
  install, learn, or maintain for a two-service, single-dependency
  system.

## Alternatives considered

- **Kubernetes** (a Deployment with 2 replicas, a Service, an Ingress)
  — would demonstrate the same round-robin-with-health-check-failover
  capability, at the cost of an entire control plane, its own
  configuration surface, and an operations model disproportionate to
  what this project's actual scope needs to prove. Revisit if the real
  requirement ever grows to genuinely need Kubernetes-specific
  capabilities (autoscaling on custom metrics, multi-node bin-packing,
  a service mesh) this system does not currently have.
- **A managed load balancer with two independently deployed instances,
  no reverse-proxy layer of this repo's own** — would work for the
  live cloud deployment, but wouldn't demonstrate the failover behavior
  locally, and Azure Container Apps' own ingress already does this job
  for the live single-instance deployment where it's actually needed.
