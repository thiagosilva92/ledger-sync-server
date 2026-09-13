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

## A real incident hit applying this: a same-deployment identity/role/consumption deadlock

The first `infra.yaml` apply caused a genuine, if brief, production outage — worth
recording in full rather than smoothing over, the same honesty standard the rest
of this repo's obstacle write-ups hold to.

**What went wrong.** `main.bicep` does three things to the same Container App
in one deployment: (1) attach a system-assigned identity, (2) switch
`registries` to pull using that identity, (3) grant that identity `AcrPull`
via a role assignment that references `containerApp.identity.principalId`.
Step 3 depends on step 1/2 having fully applied — but Azure Container Apps'
own provisioning doesn't consider a revision-affecting update "succeeded"
until the new revision actually starts, and step 2 means the new revision
can't start (can't pull the image) until step 3's permission is not just
created but has propagated through Azure AD. The role assignment resource,
in turn, can't be created until ARM considers the Container App resource's
own deployment operation complete. Three resources, each waiting on the
next, all in one atomic deployment: a real circular wait, not a
hypothetical one. ARM eventually gave up after ~21 minutes
(`Operation expired`), and in the meantime the app was down — Container
Apps' single-revision mode has no previous healthy revision to fall back to
once the new one is the only one recorded.

**How it was actually resolved**, from the live incident, not from reasoning
about it afterward:
1. Diagnosed via `az containerapp revision show` (`provisioningError:
   "Pending:ImagePullBackOff"`) and `az containerapp replica list` — evidence,
   not a guess.
2. Confirmed the role assignment genuinely didn't exist yet (`az role
   assignment list` on the registry scope) — the deadlock's actual proof.
3. Broke the cycle by creating the `AcrPull` role assignment **manually**,
   outside the stuck deployment, so it no longer needed the deployment to
   finish first.
4. Forced an immediate retry (`az containerapp revision restart`) rather
   than waiting for Container Apps' own backoff timer, which shortened the
   recovery window materially.
5. Confirmed recovery from the actual container logs, not just an HTTP 200 —
   watched it connect to the (also just-rotated) Postgres password and serve
   `/health/ready` successfully.
6. A second `infra.yaml` run then failed too, but harmlessly: Azure's RBAC
   model rejects two separate role-assignment resources for the identical
   (principal, role, scope) triple, and the manually-created one didn't
   share Bicep's deterministic name (`guid(acr.id, containerApp.id,
   'AcrPull')`), so the template's own attempt to create "its" role
   assignment collided with the hand-created one (`RoleAssignmentExists`).
   Deleted the manual one — safe, since the identity's permission is only
   checked at image-pull time and the container was already running, not
   about to pull again — and re-ran once more, which finally converged
   cleanly: Bicep's own role assignment now exists, nothing else needed to
   change, and the live state fully matches the template.

**Why this couldn't have been caught by `what-if`.** `what-if` correctly
previewed exactly the resources this change would create/modify, but it
cannot simulate runtime behavior like RBAC propagation latency or a
revision's actual pull success — it validates the desired end state, not the
path a live system takes to get there. That gap between "the diff looks
right" and "the live rollout order is safe" is itself worth knowing: for a
change that couples identity creation, permission grants, and that same
identity's own resource depending on the permission working, the safer
pattern is two separate deployments (grant the permission first, confirm it
took effect, *then* switch the resource to depend on it) rather than one
atomic apply — a refinement this repo has not yet gone back to make
`main.bicep` itself enforce, since the fix here was operational, not a
template change.
