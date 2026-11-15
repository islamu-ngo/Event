# Agentic Workflow Knowledge Ledger

> **Scope:** How implementation plans (`dev/active/<task>/` triads) are owned,
> delegated to execution worktrees, reconciled, and retired across several
> separately merged pull requests.

## Durable Decisions

- A multi-PR plan is one programme with one canonical triad in the main
  checkout. Each pull request scope is delegated to one execution location at
  a time, and that copy is the scope's one live detailed ledger. See
  [the ADR](../../../docs/internal/adr/ADR-multi-pr-agentic-workflow.md).
- Delivery, reconciliation, and cleanup are separate facts in the PR
  registry. Plane mirrors delivery and acceptance only; the plan's active
  tracking file is folded only after the final programme reconciliation. See
  [plan-tracking](../../../.agents/skills/plane-compose/resources/plan-tracking.md).

## [2026-11-14 Europe/Brussels] — Ignored plan files do not survive merge or worktree removal

**Context:** Designing the multi-PR workflow (`create-agent-context-skill`)
after large programmes had been executed by moving the whole triad into one
long-lived worktree.

**Symptom / Observation:** `dev/active/` is gitignored. Merging the branch
carries only tracked files, so discoveries, added tasks, and unmet obligations
recorded in the worktree's triad never reach `develop`. A clean `git status`
in that worktree does not show them, because ignored files are not listed.
Removing the worktree after merge can therefore delete the only record of work
that later pull requests still need.

**Root Cause:** The plan's lifetime was tied to one branch and one worktree,
while its real lifetime is the whole programme. Nothing required the execution
copy to be reconciled back to a persistent home before cleanup.

**Resolution:** Keep the canonical triad in the main checkout for the
programme's whole life. Reconcile the delegated scope's section into it before
review handoff and again after merge. Allow cleanup only with the user's
current instruction, actual merge evidence (squash and rebase merges count),
and evidence that uncommitted and ignored execution information has been
preserved. Check ignored files explicitly, for example with
`git status --short --ignored -- dev/active/`, instead of trusting a clean
status.

**Why This Matters for Future Work:** "Merged" and "safe to delete" are
different facts. Before removing any worktree that held plan files, prove
where its ignored knowledge now lives; a passing merge says nothing about it.

**References:**
- `docs/internal/adr/ADR-multi-pr-agentic-workflow.md`
- `.agents/skills/implement-tasks/resources/pr-execution-lifecycle.md`
- `.agents/skills/implementation-plan/resources/pr-scope-contract.md`
- `.gitignore` (`dev/active/` entry)

**Promotion Consideration:**
- [ ] Candidate for `docs/QUICK_REFERENCE.md` (new non-inferable rule)
- [ ] Candidate for new `.claude/rules/*.md` entry
- [x] Candidate for skill update: `implement-tasks` (PR execution lifecycle)
- [x] Candidate for ADR / `MAJOR_DECISIONS.md` (recorded in the multi-PR workflow ADR)
- [ ] Stays in journal only (one-off debugging lesson)
