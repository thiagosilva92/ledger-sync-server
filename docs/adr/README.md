# Architecture Decision Records

Short records of the significant, sometimes non-obvious decisions behind
this codebase — what was decided, why, and what alternatives were
rejected and why. Written in [Michael Nygard's ADR format](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions).

These are retrospective: written after the decisions were made and
implemented, not before, matching this project's actual practice
throughout of proving something works before writing it up (see the
main README's Status section). They exist so a decision's reasoning
survives independently of the person who made it.

| ADR | Decision |
| --- | --- |
| [0001](0001-minimal-apis-not-mvc.md) | Minimal APIs, not MVC controllers |
| [0002](0002-hashed-api-keys-not-oauth.md) | Hashed, out-of-band API keys, not OAuth/JWT |
| [0003](0003-postgres-ef-core-port-adapter.md) | PostgreSQL + EF Core behind a domain-owned port |
| [0004](0004-rate-limit-by-api-key-not-ip.md) | Fixed-window rate limiting partitioned by API key, not IP |
| [0005](0005-yarp-compose-not-kubernetes.md) | YARP + Docker Compose for horizontal scaling, not Kubernetes |
| [0006](0006-azure-container-apps-simplified-deployment.md) | Azure Container Apps, and a deliberately simplified live topology |
| [0007](0007-split-cd-from-infra-changes.md) | Routine app deploys and infrastructure changes are separate pipelines |
| [0008](0008-managed-identity-for-acr-pull.md) | The Container App pulls from ACR via managed identity, not admin credentials |
| [0009](0009-nbomber-for-load-testing.md) | NBomber for load testing, run manually against docker-compose |
