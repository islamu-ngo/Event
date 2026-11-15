# Persistent Implementation Plans With Bounded Pull Request Execution

> **Audience:** Contributors | Maintainers | AI agents
> **Status:** Accepted
> **Owner:** Contributor Experience | Agent Context
> **Last Verified:** 2026-11-14
> **Source Anchors:** `.agents/skills/implementation-plan/resources/pr-scope-contract.md`, `.agents/skills/implement-tasks/resources/pr-execution-lifecycle.md`, `.agents/skills/plane-compose/resources/plan-tracking.md`, `docs/internal/project-management/work-item-taxonomy-and-granularity-report.md`, `docs/internal/adr/ADR-plane-project-management-standardization.md`, `.agents/rules/plane-tracking.md`, `islamic-value-sensitive-design/workstreams/i-vsd-multi-pr-agentic-workflow.md`

---

## 1. Problem Context

Implementation plans live in a gitignored `dev/active/<task>/` triad: `plan.md` (architecture and scope), `tasks.md` (execution ledger), and `context.md` (session memory). Until this decision, execution moved the whole triad into one worktree and kept working through every remaining phase there. That caused three problems:

1. **Long-lived branches.** A programme that should land as several reviewable pull requests became one branch that lived for weeks. The maintainer had to trade unfinished branch commits to use the parts that were already done, and the branch kept drifting from `develop`.
2. **Lost planning knowledge.** The triad is ignored by Git, so merging tracked changes never preserved it. Removing the worktree after merge could delete discoveries, added tasks, and unmet obligations that later work still needed.
3. **Units that disagreed.** Plane already tracked one item per merged pull request, but the execution unit (the whole plan), the verification boundary (the end of the plan), and the cleanup boundary (the branch) did not match it. Topology detection guessed in-tree versus worktree execution from old checked tasks, which misclassifies a retained plan as soon as its first pull request is complete.

This ADR records the accepted model so it does not have to be argued again for each programme. The operational steps live in the routed skill resources named in the source anchors; this document explains why they are shaped the way they are.

---

## 2. Decision

### A. Programme → pull request scope → phase → task

A plan is a **programme**. It is divided into **pull request scopes**, each an independently mergeable outcome with its own acceptance and verification boundary. Phases are reviewable commit groups inside one scope, and tasks are checklist steps inside a phase. Every scope has a stable ID and a descriptive outcome name, so status reports never depend on bare phase codes.

### B. One canonical triad, one live detailed ledger per delegated scope

- The **canonical triad stays in the main checkout** for the programme's whole life, across every intermediate merge. `plan.md` owns architecture, requirements, and scope boundaries. `tasks.md` owns the **PR registry** and delegation metadata. `context.md` owns programme-level resume state.
- To execute a scope, the executor **delegates** it to an execution location (by default a fresh worktree created from the current merged target branch). The execution copy holds only that scope's detailed tasks plus the shared invariants, scenarios, value-design constraints, dependency contracts, verification, and commit packets it needs. It records its canonical source and the scope revision it was extracted from.
- While a scope is delegated, the **execution copy is its one live detailed ledger**. The canonical checklist for that scope is labelled a delegated snapshot and is not edited in parallel. Future scopes stay editable in the canonical triad.
- Execution in the main checkout without a worktree remains available only when the user explicitly selects it. Old checked tasks never select a topology.

### C. Separate delivery, reconciliation, and cleanup facts

Each registry row records three independent facts:

| Fact | Values | Meaning |
|---|---|---|
| Delivery | Planned, Ready, Implementing, In review, Merged, Closed unmerged | Where the pull request actually is. Blockers are explicit metadata, not a delivery state. |
| Reconciliation | pending, current | Whether the canonical triad reflects what was actually delivered. |
| Cleanup | not requested, pending, complete | Whether the authorized execution storage and branches have been removed. |

A scope is **complete** only when it is Merged **and** its required acceptance has been reconciled. Cleanup is tracked separately and never implies completion. Approval is not merge. A PR closed without merging is not delivered, and requested scope cannot be discarded without the user's explicit direction.

### D. Selection and readiness

An invocation first resumes an existing active scope, or reports its review, blocked, or finalization state; it never creates a duplicate. Otherwise it evaluates the earliest unstarted scope whose predecessors are all merged and reconciled, refreshes that scope's contracts against the target branch, marks it Ready only when its readiness criteria hold, and then delegates it. **One active dependent scope per programme** is the default. Independent programmes still run in parallel.

### E. Two reconciliation checkpoints

1. **Before review handoff:** summarize the actual delivery, necessary added work, changed decisions, and incomplete criteria into the scope's canonical section.
2. **After merge:** account for changes made during review and the final evidence, then correct the dependencies of future scopes.

Reconciliation edits only the delegated scope's section and any justified shared decision. It never overwrites the canonical triad wholesale from a worktree copy, so independent edits to future sections survive. The approved intent stays distinguishable from the as-delivered result. A required criterion that is still unmet stays an explicit obligation in this programme, or becomes an approved successor scope; it is never closed by a checked task or a merged PR, and never moved silently to `dev/backlog/`.

### F. Bounded delivery and verification

Every pull request must have a truthful disposition for the checks its changed scope requires **before it merges**. A check cannot be postponed only because more pull requests remain. Final cross-capability acceptance runs once its prerequisites exist. The executor stops at the selected scope's review handoff; it does not continue into the next scope inside the same branch. Hub-and-spoke workers may help implement one scope, but they cannot widen its outcome or accumulate future scopes into a permanent hub branch.

### G. Tracking follows the programme

Plane tracking keeps one item per separately merged pull request. An item reaches `Done` only after merge plus acceptance reconciliation. A parent item reaches `Done` only when every child is `Done` and every retained obligation is satisfied. The plan's `event/work/active-<task>.yaml` file, like the triad, survives intermediate merges and is folded into `event/work/workitems.yaml` only after the final programme reconciliation. A programme that honestly needs more than 6 new items, or a new Epic, keeps one triad and asks for that budget explicitly; it is never fragmented into several plans to fit the budget. ID minting stays with `plane-compose`, routine work never runs `plane pull` or `--force`, and no custom Plane client is added. See the [Plane project management ADR](ADR-plane-project-management-standardization.md) and [plan-tracking protocol](../../../.agents/skills/plane-compose/resources/plan-tracking.md).

### H. Cleanup authority

Removing a worktree, a local branch, or a remote branch is destructive, and the execution copy may be the only record of ignored information. Cleanup therefore requires all of the following:

1. **The user's current instruction** for that cleanup. Earlier approval of the plan, or of the pull request, is not cleanup authorization.
2. **Merge evidence** from the actual pull request: its merged state and target branch. Squash and rebase merges count as merged even though the original commit identities differ, so original-tip ancestry alone is not the test. An unmerged cleanup request needs explicit preservation or cancellation handling and never marks delivery complete.
3. **Preservation evidence:** uncommitted and ignored execution information has been accounted for and reconciled into the canonical triad. A clean `git status` is not proof, because ignored files do not appear in it. Unresolved ownership blocks deletion.

Cleanup that stops midway is recorded as pending and resumed later without repeating implementation or touching the canonical programme.

### I. Legacy plans are normalized on explicit resume

There is no bulk migration and no compatibility switch. When the owner of an existing active plan explicitly resumes it under this workflow, the executor first takes an inventory of what was actually completed, what is uncommitted, what is still required, and what was deferred. It then normalizes that into PR scopes, keeping existing IDs and authority, before any relocation or cleanup. Plans that are not resumed are left untouched.

---

## 3. Alternatives Considered

1. **Keep moving the whole triad into one worktree.** Rejected: this is the reported problem. The plan's lifetime gets tied to one branch, and cleanup can delete the only copy of ignored planning knowledge.
2. **Two full, independently editable copies of the triad.** Rejected: there would be two competing ledgers with no rule for which one is authoritative, and reconciliation would turn into a merge conflict on ignored files.
3. **A stack of dependent unmerged branches.** Rejected as the default: every rebase of the base branch has to be carried through the whole stack, which brings back long-lived integration cost. It may be chosen explicitly for one programme as a recorded exception.
4. **An automated orchestration engine, workflow manifest, lock service, or heartbeat.** Rejected: it adds a controller to maintain and conflicts with the repository's native-tooling rules. Readable Markdown contracts plus ordinary Git, `gh`, and Plane commands are enough.
5. **One plan per pull request.** Rejected: cross-cutting invariants, value-design constraints, and required later work would be spread over several triads, and required scope could quietly disappear between them. Fragmenting a plan to meet the Plane item budget is rejected for the same reason.

---

## 4. Consequences

- **Positive:** completed outcomes merge on their own schedule, branches stay short-lived, and the canonical plan keeps every obligation that has not been met. Plane, the PR registry, and Git history each record one kind of fact.
- **Cooperative concurrency only:** ownership is a written convention, not an atomic lock. Registry delegation and reconciliation are serialized through the responsible agent. If two sessions claim the same scope, both stop duplicate execution and align with the user. No locking, lease, or race-free guarantee is claimed, and none should be inferred.
- **Accepted cost:** reconciliation is extra work at review and after merge, and the canonical triad has to be kept current by hand. It is what replaces the risk of silent loss.
- **No new machine contract:** no schema field, intent, parser, executable validator, hook, or CI check is added. The contract lives in skill resources and is checked by review.
- **Not yet measured:** no pilot or numeric improvement is claimed. A later real programme may measure pull request age, cross-branch imports, reconciliation corrections, and review turnaround.

---

## 5. Related

- [PR scope contract](../../../.agents/skills/implementation-plan/resources/pr-scope-contract.md) · [PR execution lifecycle](../../../.agents/skills/implement-tasks/resources/pr-execution-lifecycle.md)
- [Plane project management ADR](ADR-plane-project-management-standardization.md) · [work-item taxonomy §4.1](../project-management/work-item-taxonomy-and-granularity-report.md#41-tracking-budget-per-implementation-plan-plan--plane-mapping) · [`plane-tracking` path rule](../../../.agents/rules/plane-tracking.md)
- [Agentic Context Engineering](../AGENTIC_CONTEXT_ENGINEERING.md)
- [I-VSD assessment: multi-PR agentic workflow](../../../islamic-value-sensitive-design/workstreams/i-vsd-multi-pr-agentic-workflow.md)
- [Agentic workflow journal](../../../dev/_journal/domains/agentic-workflow.md)
