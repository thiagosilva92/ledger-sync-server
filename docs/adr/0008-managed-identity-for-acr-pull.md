# ADR 0008: The Container App pulls from ACR via managed identity, not admin credentials

Date: 2026-09-13
Status: Accepted

## Context

The Container App originally authenticated to ACR with the registry's
admin username and password — `Microsoft.ContainerRegistry/registries`'
built-in static credential, fetched via `listCredentials()` in
`main.bicep` and stored as a Container App secret
(`ledgersyncacr2026azurecrio-ledgersyncacr2026`). That's a stored,
long-lived, reusable static secret sitting in the app's own
configuration — the exact pattern [ADR 0007](0007-split-cd-from-infra-changes.md)'s
CD pipeline had already moved away from for GitHub's own access
(OIDC federated credentials, no client secret stored anywhere), leaving
the registry pull path as the one remaining static credential in the
whole deployment.

## Decision

Give the Container App a system-assigned managed identity
(`identity: { type: 'SystemAssigned' }`), grant that identity the
built-in `AcrPull` role scoped to the registry only (least privilege:
pull, not push or manage), and point `configuration.registries` at
`identity: 'system'` instead of a username/password pair. With nothing
left depending on it, the registry's admin user is disabled entirely
(`adminUserEnabled: false`).

## Consequences

- No credential exists anywhere for image pulls — not in source
  control, not in a Container App secret, not in a GitHub secret. Azure
  itself issues and rotates the underlying token behind the managed
  identity; this repository never sees it.
- The CD pipeline's own registry access (`az acr login` in `ci.yaml`'s
  `deploy` job) was already using the GitHub OIDC service principal's
  `AcrPush` role, not the admin user — so disabling the admin user
  removes a credential nothing was actually still using, rather than
  breaking a working path.
- Applying this went through `infra.yaml` (per ADR 0007), which
  surfaced a real, unrelated gap in that workflow: `main.bicep`'s
  `containerImage` parameter defaults to `:latest`, so applying this
  change without pinning it would have silently moved the running
  Container App off the specific commit-SHA-tagged image the last
  `deploy` job had put it on — an infra-only change accidentally
  changing *which app version runs*, exactly what ADR 0007 exists to
  prevent. Fixed by having `infra.yaml` read the currently-running
  image with `az containerapp show` and pass it back as an explicit
  parameter, so an infra apply's blast radius stays limited to
  infrastructure, never the deployed app version, confirmed via
  `az deployment group what-if` showing zero image diff once fixed.

## Alternatives considered

- **User-assigned managed identity** — would let the identity be
  provisioned independently of the Container App's lifecycle (useful if
  multiple resources needed to share one identity), unnecessary
  complexity here where exactly one resource ever uses it.
- **Leave the admin-credential approach as-is** — the simplest change is
  no change, but leaves a stored, rotatable-only-by-hand static secret
  in place for no remaining reason once the identity-based path works,
  undermining the credential-hygiene story the rest of this deployment
  already tells.
