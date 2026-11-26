# Persistent Programme And Pull Request Scope Contract

This contract keeps the maintainer's full request available while contributors
deliver independently mergeable outcomes. Planning and review define the boundaries;
the [executor lifecycle](../../implement-tasks/resources/pr-execution-lifecycle.md)
owns native execution and recovery.

## Ownership And Hierarchy

A programme contains PR scopes; each PR contains phases; each phase contains tasks
and its own verified commit packets. A phase, session, or commit is not a PR.
Assign stable scope IDs such as `PR-FOUNDATION`; descriptive names explain their
outcome. Reordering scopes does not change their IDs.

The complete `dev/active/<programme>/` triad remains canonical in the main checkout:

| Artifact | Programme responsibility |
|---|---|
| `*-plan.md` | Original requested outcomes, shared architecture/invariants, scenarios, PR boundaries and dependency contracts |
| `*-tasks.md` | PR registry, delegation metadata, retained obligations, detailed tasks for undelegated scopes and snapshots for delegated scopes |
| `*-context.md` | Programme resume pointer, material decisions, blockers and concise evidence |

One selected PR has one live detailed execution owner. Its scoped execution copy
owns that PR's tasks, necessary discoveries and session memory. Mark the canonical
selected checklist **delegated snapshot** with its owner and location; it is not a
second editable live ledger. Explicit in-tree execution uses the canonical selected
section directly, without creating a duplicate copy.

Serialize delegation and reconciliation through the responsible agent. One active
dependent scope per programme is the default; independent programmes may proceed
in parallel. Cooperative ownership is not an atomic lock. Conflicting owners stop
duplicate work and resolve ownership before editing the same scope.

## PR Sizing Default And Anti-Over-Splitting

Multi-PR execution exists to prevent **genuinely multi-week, long-lived branches**,
not to force every implementation plan into several pull requests. Default to the
fewest pull requests that keep delivery reviewable and safe:

1. **1 multi-phase PR (default, Shape S):** Use a single pull request structured
   into reviewable phases with atomic Conventional Commits for a cohesive feature,
   refactor, bug fix, or isolated subsystem/tooling change. Not every plan needs
   multiple PRs.
2. **2 multi-phase PRs (normal ceiling, Shape M):** Split into two pull requests
   only when the work divides cleanly along one real boundary—such as risk or
   trust isolation (offline tooling vs. supply-chain/release pipeline), contract
   stabilization (backend/OpenAPI foundation vs. generated client/UI consumer), or
   a distinct verification/approval gate.
3. **3+ PRs (exceptional, explicit justification required):** Reserve three or
   more pull requests for genuinely multi-week or cross-surface programmes where a
   1–2 PR branch would drift or mix unrelated release risks (for example:
   multi-provider schema/migrations + public API contract + BFF/UI + rollout/ops
   with independent review or deployability). State the concrete justification in
   `plan.md` §0; never split merely to hit a Plane budget or because sub-steps can
   theoretically compile alone.

**Anti-Over-Splitting Rule:** Never manufacture an extra PR boundary when splitting
would:

- turn phases, small helper layers, single CLI commands, or an isolated project
  with a fast test loop into tiny serial PRs;
- merge unused/dead interfaces, unconsumed ports, or half-wired commands onto
  `develop` waiting for a later PR to wire them;
- create throwaway transitional refactors, temporary test patches, or duplicate
  documentation rewrites solely to bridge an intermediate PR; or
- multiply serial merge, worktree, reconciliation, and Plane tracking overhead
  when atomic phase commits inside 1–2 PRs already give clean commit-by-commit
  reviewability.

## Required Scope Definition

In the canonical plan, group phases under each stable PR ID and state:

- Independently usable outcome and original requirements/scenarios it satisfies.
- Predecessor scopes and the merged contracts it consumes or changes.
- Included phases/tasks, exact owned paths, exclusions and applicable I-VSD IDs.
- Acceptance, required verification, release/docs impact and rollback/recovery.
- Shared architecture, security/privacy boundaries and downstream obligations.

Detail the next candidate enough to execute; retain later required outcomes,
dependencies and acceptance in the same triad with rolling detail. Both extremes
fail readiness: a genuine multi-week mega-PR disguised as one scope when concrete
architectural, risk, or rollout boundaries justify splitting, and an over-split
plan that fragments a 1–2 PR effort into 3+ serial PRs leaving unwired code or
throwaway transitional work. Do not move requested work to backlog to meet a sizing
threshold.

The tasks registry records these mutable fields for every scope:

| Field | Meaning |
|---|---|
| Scope ID / outcome / prerequisites | Stable identity, readable result and dependency references |
| Delivery | Planned, Ready, Implementing, In review, Merged, Closed unmerged |
| Reconciliation | Pending or current, with review snapshot or final merged evidence identified |
| Cleanup | Not requested, pending or complete, with remaining authorized operations |
| Delegation | Canonical source revision, execution location, branch, owner and live-ledger location |
| PR / blockers / obligations | Actual PR identity and target when known; missing evidence and unmet criteria with owners |

These fields belong in tasks/context, not dynamic status inside plan design.
Readiness and merge do not imply acceptance, reconciliation, or cleanup.

## Selection And Readiness

Resume an existing Implementing or In review scope, or its pending finalization,
before selecting another. A missing execution location requires recovery, never
automatic recreation. Old checked tasks cannot choose topology or prove delivery.

Otherwise evaluate the earliest Planned candidate with merged and reconciled
required predecessors. Refresh its contracts against the actual merged target.
Mark it Ready only after approval, ownership, independently mergeable acceptance,
exact paths, applicable shared context, verification and commit packets are
complete. Record an unmet readiness condition as a blocker. An approved programme
does not make every future scope immediately executable.

## Scoped Extraction

The execution copy identifies canonical path, source revision, selected scope ID,
owner and delegation state. Copy only:

- The selected PR's phases, tasks, acceptance and exact commit packets.
- Original intent and the shared invariants, architecture decisions, scenarios,
  I-VSD mitigations and escalation boundaries that govern those tasks.
- Merged predecessor contracts, affected downstream interfaces, verification,
  release/documentation requirements and explicit user overrides.
- Current blockers, local environment constraints and recovery/resume pointers.

Do not copy all future task detail or move the canonical directory. Missing a
required shared boundary blocks extraction/readiness. This contract does not
broaden permission to copy secrets or inspect another programme's worktree.

## Two Reconciliation Checkpoints

Before review handoff, compare original intent and acceptance with actual delivery.
Record necessary discovered work, changed contracts/decisions, exact checks and
limits, incomplete criteria and their continuing ownership. Update affected future
dependencies before they can become Ready. Keep original intent distinguishable
from the delivered result.

After merge, repeat that comparison using final reviewed changes and native
merge/target evidence. Approval or Closed unmerged is not delivery. Squash/rebase
merges do not require original-tip ancestry. A merged PR with unmet required
acceptance retains that obligation or an explicitly approved successor scope;
it does not complete the promise by changing a checkbox.

Patch only the delegated canonical sections and justified shared decisions. Check
the source revision and preserve independent future-section edits. Never replace
the whole canonical triad with an execution copy. Reviewers may refine future scope
but must coordinate amendments to an active delegated section with its live owner.

## Completion And Retention

PR completion requires verified merge plus acceptance reconciliation. Cleanup is
separate and needs current user authorization, accounted local work and preserved
ignored information. A clean Git status does not account for ignored files.
Interrupted cleanup resumes only remaining operations.

The canonical programme and shared active Plane file survive intermediate merges.
Retire them only after all retained obligations are satisfied or explicitly removed
by the user, final reconciliation is current, and durable decisions/lessons have
graduated. An executor cannot retire the programme merely by delivering its PR.
Tracking follows [plan-tracking](../../plane-compose/resources/plan-tracking.md);
one item represents each separately merged PR, never each phase or verification.
