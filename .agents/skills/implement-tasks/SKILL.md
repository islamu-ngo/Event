---
name: implement-tasks
description: "Load when executing or resuming one PR scope of an approved dev/active/<programme> plan, including scoped worktrees, in-tree execution, phase commits, verification, review handoff, merge reconciliation or authorized cleanup; not for plan authorship or architectural review."
type: workflow
enforcement: suggest
priority: high
---

## Rules

1. Execute only approved scope. Keep the complete canonical triad in the main
   checkout throughout the programme; delegate only one eligible PR and its
   required shared context. Never move the whole programme into a worktree.
2. Resume explicit ownership before selecting new work. Historical checkboxes
   never choose topology. A missing execution directory is recovery, not a fresh
   start. Keep delivery, reconciliation and cleanup as separate facts.
3. One delegated scope has one live detailed ledger. The canonical selected
   checklist is a labelled snapshot until reconciliation; explicit user-selected
   in-tree execution uses that canonical section directly.
4. Finish the selected PR's acceptance and applicable checks, reconcile its
   actual delivery, open the authorized PR and stop at its review handoff.
   Required future scope remains in the programme. Do not accumulate later
   PRs into a permanent hub or silently send requested work to backlog.
5. Reconcile again after verified merge and review changes. Preserve ignored
   information and account for local work before any user-authorized deletion.
   Approval, merge, acceptance and cleanup are distinct; approval is not merge.
6. Preserve local overrides, secret authority, task namespace isolation, literal
   owned commit paths and native tracking. Keep `.githooks/commit-msg` active
   (delegating to `preflight-commit`, where internal commits omit automatically
   without skip trailers). No new controller, lock, manifest, custom REST client
   or source-wording test implements this workflow.
7. Tier 4 Markdown changes use metadata, links, scenario review and diff checks;
   no product build/test. Product verification remains proportional and mandatory
   per PR, distinct from final programme acceptance. Keep explicit user overrides
   visible, scoped and truthful.

## Workflow

1. Read canonical context first, then the registry/current task and its named plan
   sections. Verify approval, owner, scope state, dependencies and source revision.
   Normalize legacy state on explicit resume before relocating or deleting anything.
2. Follow [selection and topology](resources/pr-execution-lifecycle.md#selection-and-topology).
   Resume active implementation, review or pending finalization. Otherwise
   evaluate a Planned candidate and mark it Ready only after readiness passes.
3. Follow [execution](resources/pr-execution-lifecycle.md#execution-and-verification)
   for scoped extraction, environment setup, Red/Green where required, proportional
   verification, failure attribution and planned phase commits.
4. Follow [review handoff](resources/pr-execution-lifecycle.md#review-reconciliation-and-handoff).
   Graduate durable decisions, reconcile selected canonical sections, run the PR
   gate, publish with release-impact and issue metadata, park and stop.
5. On later user direction, follow
   [merge and cleanup](resources/pr-execution-lifecycle.md#merge-reconciliation-and-authorized-cleanup).
   Verify native merge/target facts, reconcile final evidence and retained
   obligations, then perform only authorized remaining cleanup.

## Resources

- [Scope contract](../implementation-plan/resources/pr-scope-contract.md) -
  ownership, readiness, extraction, registry fields and reconciliation interfaces.
- [Execution lifecycle](resources/pr-execution-lifecycle.md) -
  native topology, commands, failure recovery, publication and cleanup details.
- [Context contract](../../CONTEXT_ENGINEERING.md) -
  retrieval budgets, concise session memory and evidence rules.
- [Plane tracking](../plane-compose/resources/plan-tracking.md) -
  execution-start ID binding, per-PR state and final programme retirement.
- [Conventional commits](../conventional-commit/SKILL.md) -
  load when replacing a materially diverged packet; reuse truthful approved packets.
- [Reader-first writing](../conventional-commit/resources/reader-first-writing.md) -
  self-contained progress, decision and final teaching reports.

## Verification

- Check changed metadata against [the schema](../_SKILL_SCHEMA.md), relative
  targets, router budgets and the selected scope's scenario/ownership assertions.
- `GIT_MASTER=1 GIT_PAGER=cat git diff --check -- .agents/skills/implement-tasks`
- Record actual commands and outcomes; never claim unrun checks or merged delivery.
