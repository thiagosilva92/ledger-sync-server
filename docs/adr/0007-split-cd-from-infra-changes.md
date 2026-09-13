# ADR 0007: Routine app deploys and infrastructure changes are separate pipelines

Date: 2026-09-13
Status: Accepted

## Context

Every Azure resource behind the live deployment was originally created
by hand, one `az` command at a time — not reproducible, since recreating
the resource group today would mean re-reading a wall of past terminal
output rather than running something. Turning that into automation
raised a real question: should a routine code push and an infrastructure
change (a new resource, a different SKU, the database password) go
through the same pipeline, gated the same way?

## Decision

They are two deliberately separate GitHub Actions workflows:

- **`deploy`** (a job in `ci.yaml`) runs on every merge to `main`, after
  tests pass: build the image, push to ACR, point the existing Container
  App at the new tag. It never touches infrastructure definitions,
  resource SKUs, or secrets like the database password.
- **`infra.yaml`** applies `infra/main.bicep` (see that directory's own
  [README](../../infra/README.md)), but only via manual
  `workflow_dispatch`, defaulting to a `what-if` preview that changes
  nothing — applying requires explicitly checking a box.

Both authenticate to Azure via OIDC federated credentials on a scoped
Azure AD app registration (`Contributor` on the resource group,
`AcrPush` on the registry specifically) — no client secret stored or
rotated.

## Consequences

- A routine code push can never rotate the database password, resize a
  server, or otherwise change what infrastructure exists — the blast
  radius of "someone merges a PR" is bounded to "a new container image
  is running," which is what a merge should do.
- Infrastructure changes get a human decision point (clicking
  `workflow_dispatch`, reviewing the `what-if` preview) rather than
  happening as a side effect of an unrelated code change — appropriate
  given how rare and how consequential they are compared to app
  deploys.
- This was validated against a real, non-hypothetical failure: the
  first `deploy` run failed with `AADSTS700213` because this
  repository's actual GitHub OIDC subject claim uses the newer
  `repo:owner@ownerId/repo@repoId:ref:...` format (immutable numeric
  IDs), not the classic `repo:owner/repo:ref:...` most OIDC tutorials
  show — fixed by recreating the federated credentials with the correct
  subject, confirmed by a second push deploying cleanly and
  `/health/ready` reporting `Healthy` on the live URL.

## Alternatives considered

- **One pipeline, always applying infrastructure-as-code on every
  push** — simpler to reason about as "one workflow does everything,"
  but means every merge carries the ability to touch the database
  password or resource sizing, a blast radius mismatched to what a
  routine code change should be able to do.
- **Infrastructure changes applied by hand, indefinitely** — the
  starting point this ADR replaces; not reproducible, and increasingly
  error-prone as the number of resources grows.
