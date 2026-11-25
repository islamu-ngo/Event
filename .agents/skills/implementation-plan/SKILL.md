---
name: implementation-plan
description: "Load when asked to create, update, re-baseline, or continue a repository-grounded implementation/technical/refactor plan or multi-PR programme and its `dev/active/<task>` plan/context/tasks files; not for a product PRD, informal advice, reviewing an existing plan (senior-cto-feedback), or executing one (implement-tasks)."
type: workflow
enforcement: block
priority: high
---

## Rules

1. **Plan only, in the root.** Write the canonical triad to the main checkout's `dev/active/<task>/`, where it persists for every PR of the programme. Never create or switch branches, create worktrees, push tracking, or edit product code. Execution topology belongs to [implement-tasks](../implement-tasks/SKILL.md).
2. **Fresh evidence first.** Fast-forward the root as in [AGENTS.md §8](../../../AGENTS.md#8-verification-baseline), classify every matching intent, and verify each claimed path, symbol, test and contract before reporting current state ahead of future state.
3. **Integrated intake.** Run [I-VSD](../i-vsd/SKILL.md) `planning` mode from the shared evidence packet and resolve scope-shifting branches through [grill-me](../grill-me/SKILL.md). Only genuinely deferrable unknowns stay open.
4. **Behavior before code.** Section 3 states RFC 2119 requirements with `WHEN`/`THEN` scenarios; classes, handlers and migrations belong in Section 5. Declare Behavioral or Non-Behavioral Delta.
5. **Programme -> PR -> phase -> task.** Follow the [PR scope contract](resources/pr-scope-contract.md): group every phase and task under a stable PR ID in plan, tasks and context; each PR is independently mergeable with its prerequisites, owned paths, acceptance, verification, release impact and rollback. Detail the next candidate; later scopes keep rolling detail but retain every required outcome, dependency and acceptance criterion. Never move required work to backlog to fit a size.
6. **Proportional verification.** Phase gates stay targeted; every PR has a mandatory acceptance gate for the checks its changes require; cross-capability programme acceptance runs once its prerequisites exist. Preserve the Tier 4 documentation exemption, sequential execution profiles and recorded user overrides ([quality gates](resources/quality-gates.md#verification-gates)).
7. **Greenfield.** Break and replace; never plan compatibility shims or deprecated aliases.
8. **Executable commits.** Each phase closes with declarative [conventional-commit](../conventional-commit/SKILL.md) contracts (rules 1, 13, 14) with exact owned paths; never an umbrella commit. Non-breaking internal types and engineering scopes omit automatically without `Changelog: skip`; optional high-impact fragments are created only when structured impact metadata is required. Human-facing text follows [reader-first writing](../conventional-commit/resources/reader-first-writing.md).
9. **Tracking by PR.** One Plane item per separately merged PR, authored locally per [plan-tracking](../plane-compose/resources/plan-tracking.md); never per phase, test or review.
10. **Retention.** Durable decisions graduate to ADRs and lessons to the journal within the PR that owns them. Only optional follow-ups go to backlog. The programme retires only under the contract's completion rule.

## Workflow

1. Sync root; find overlapping `dev/active/` or `dev/pause/` work and re-baseline it instead of duplicating.
2. Resolve intents, rules and skills; verify repository reality; write the current-state report.
3. Run I-VSD planning intake and grill-me; use robin-neutral for material technical forks.
4. Define PR scopes, then phases and tasks inside each; write plan, tasks and context.
5. Revalidate I-VSD mappings and run the quality gates; fix any triad disagreement.

## Resources

- [index](resources/index.md) - which resource answers which planning decision.
- [pr-scope-contract](resources/pr-scope-contract.md) - scope definition, registry fields, readiness, delegation and retention.
- [investigation-workflow](resources/investigation-workflow.md) - intake, evidence, slicing and optional OmO Prometheus intake.
- [plan-template](resources/plan-template.md) - writing or re-baselining `*-plan.md`.
- [operational-artifacts](resources/operational-artifacts.md) - `*-tasks.md` registry/ledger and `*-context.md`.
- [quality-gates](resources/quality-gates.md) - pre-handoff gates and the final response brief.

## Anti-Patterns

- Memory-based facts, future-state-first design, or non-deferrable open questions.
- Code described as behavior, or requirements without testable scenarios.
- A large PR renamed as a scope, a phase treated as a PR, or required scope moved to backlog.
- Mock-mirroring, framework or source-text tests; provider matrices or runtime sweeps no changed surface requires.
- Task checklists, live status or handoffs inside `plan.md`; whole-triad moves or copies as execution state.
- Plane items per phase, test or commit, or several merged PRs hidden under one item.

## Final Response

Present a self-contained brief and link the three artifacts, ending with:

### Recommended Next Action

Run /senior-cto-feedback in a fresh session if you want an adversarial Senior CTO architectural audit of the plan triad first, or
Run /implement-tasks to start the first Ready PR scope after its readiness check.

## Verification

- `git diff --check -- .agents/skills/implementation-plan`
- Prose-only skill changes: review metadata, links and readability; run no product tests.
