---
name: senior-cto-feedback
description: "Load for Senior CTO critique, architectural audit, risk review, sequencing correction, or refinement of an existing implementation-plan triad or multi-PR programme; edits the requested plan/context/tasks in place without creating a worktree or review file. Not for open-ended CTO advice, writing a new plan, product implementation, or implementation workspace setup."
type: workflow
enforcement: suggest
priority: high
---

## Rules

1. **In place, review only.** Edit the requested canonical `plan.md`, `context.md` and `tasks.md` at their existing path. Never write `*-cto-review.md`, create worktrees or branches, relocate artifacts, execute implementation, or treat an execution copy as canonical.
2. **Apply without approval pauses**, but never overwrite active delegated work. Refine Planned or Ready scopes and shared decisions freely; for an Implementing or In review scope, its canonical section is a delegated snapshot. Record proposed amendments for its live owner instead ([PR scope contract](../implementation-plan/resources/pr-scope-contract.md#two-reconciliation-checkpoints)).
3. **Reality over aspiration.** Verify architectural claims against repository code with `code-review-graph` before codifying them; separate verified, inferred and missing evidence.
4. **Bounded PR scopes (1–2 PR default).** Apply [right-sizing](resources/review-rubric.md) and the [PR sizing default](../implementation-plan/resources/pr-scope-contract.md#pr-sizing-default-and-anti-over-splitting): prefer **1 multi-phase PR** (or **2 PRs** along one concrete risk, trust, or contract boundary) with atomic phase commits; split to 3+ PRs only for genuinely multi-week or cross-surface programmes, and **consolidate** over-split plans that turn phases into serial PRs or create unwired/throwaway intermediate work. Each PR scope keeps a stable ID, independently mergeable outcome, prerequisites, readiness conditions, owned paths, acceptance and verification. Retain every requested outcome in the same triad; never move required scope to backlog. Only explicit user direction removes scope.
5. **Correct, invariant-first verification.** Name the worst break and require Red invariant-breaker tests before implementation where invariants, concurrency, tenancy or security are at stake; reject mock-mirroring. Enforce the planner's [verification gates](../implementation-plan/resources/quality-gates.md#verification-gates): targeted phase gates, a mandatory per-PR acceptance gate, final programme acceptance, the Tier 4 exemption and recorded user overrides.
6. **3D evaluation.** Completeness (capabilities, I-VSD mitigations), Correctness (boundaries, races, failure paths), Coherence (Clean Architecture, HAL affordances, tenant isolation, outbox).
7. **Greenfield.** Delete obsolete paths; reject shims and deprecated aliases.
8. **Executable commits.** Every phase keeps self-sufficient declarative Conventional Commit contracts with exact paths; these are later instructions, never actions taken during review. Preserve explicit user constraints.
9. **I-VSD binding.** Bind edits to the reviewed plan/tasks and I-VSD revisions. Changes to provider authority, stakeholders, defaults or `IVSD-*` mappings mark the report `stale`; never fabricate approval.
10. **Plane audit.** One item per separately merged PR; fail flooding or under-tracking and rewrite the Tracking Plan using the [audit checklist](resources/plan-rewrite-guidance.md#plane-tracking-plan-audit).
11. **Zero-loss preservation.** Every finding, scorecard, worst break, ranked risk and trade-off is written into its [triad destination](resources/plan-rewrite-guidance.md#zero-loss-information-preservation-where-review-data-lives); chat only summarizes it.
12. **Reader-first report.** Lead with the verdict and decisions, then mechanisms, following [output template](resources/output-template.md) and [reader-first writing](../conventional-commit/resources/reader-first-writing.md#progress-and-completion-reports).

## Workflow

1. Check inputs with the [input contract](resources/input-contract.md); read the triad in place and identify delegated scopes and their owners.
2. Audit with the 3D scorecard, per-PR right-sizing, worst break and greenfield posture.
3. Update the triad per [rewrite guidance](resources/plan-rewrite-guidance.md); set CTO review to `Applied & Aligned (YYYY-MM-DD)`.
4. Report in chat using the output template.

## Resources

- [plan-rewrite-guidance](resources/plan-rewrite-guidance.md) - section patterns, delegation-safe rewrites, Plane audit.
- [input-contract](resources/input-contract.md) - input completeness, I-VSD and PR registry checks.
- [review-rubric](resources/review-rubric.md) - 3D scorecard, Socratic stress tests, right-sizing signals.
- [output-template](resources/output-template.md) - chat report shape.
- [islamu-event-guardrails](resources/islamu-event-guardrails.md) - tenant, Clean Architecture or HAL audits.
- [enterprise-self-hostable-checklist](resources/enterprise-self-hostable-checklist.md) - self-hosting, migrations, runbooks.
- [severity-model](resources/severity-model.md) - blocker, critical or major classification.

## Verification

- No `*-cto-review.md` exists; the triad stays at the requested path; no branch or worktree was created.
- Every requested outcome maps to a PR scope; none moved to backlog.
- No delegated live section was overwritten; amendments to it are recorded for its owner.
- Plan, context and tasks agree on registry state, status, next scope and phases.
- Frontmatter follows [../_SKILL_SCHEMA.md](../_SKILL_SCHEMA.md).
