---
name: senior-cto-feedback
description: "Load when asked for blunt Senior CTO critique, architectural audit, risk review, sequencing correction, or direct refinement of an existing `dev/active/<task>` implementation plan/context/tasks workstream before coding; directly updates plan.md, context.md, and tasks.md without writing review markdown files; not for open-ended CTO advice or direct code implementation."
type: workflow
enforcement: suggest
priority: high
---
<!-- ABOUTME: Senior CTO review skill for repository-grounded implementation plans and active dev-doc workstreams. -->
<!-- ABOUTME: Directly updates plan.md, context.md, and tasks.md with actionable architectural refinements and reports findings to chat without writing review files. -->

## Resources
- [../../../AGENTS.md](../../../AGENTS.md)
- [resources/output-template.md](resources/output-template.md) — load for the chat reporting template and high-signal summary structure.
- [resources/plan-rewrite-guidance.md](resources/plan-rewrite-guidance.md) — load for exact section patterns and triad update rules when rewriting plan.md, context.md, and tasks.md.
- [resources/review-rubric.md](resources/review-rubric.md) — load for 3D scorecard, Socratic stress-testing, and 4-point right-sizing rules.
- [resources/input-contract.md](resources/input-contract.md) — load to verify triad and I-VSD input completeness before editing.
- [resources/islamu-event-guardrails.md](resources/islamu-event-guardrails.md) — load when auditing tenant boundaries, clean architecture, or HAL affordances.
- [resources/enterprise-self-hostable-checklist.md](resources/enterprise-self-hostable-checklist.md) — load when assessing self-hosting configuration, migrations, or operational runbooks.
- [resources/severity-model.md](resources/severity-model.md) — load when classifying blocker, critical, or major architectural risks.

## Rules

1. **Direct Triad Refinement (Zero Review Files)**: NEVER write, emit, or generate any `*-cto-review.md` or separate feedback markdown files in `dev/active/<task>/` or elsewhere. The CTO review directly updates `dev/active/<task>/<task>-plan.md`, `...-context.md`, and `...-tasks.md` in place. 100% of the CTO's review brain and architectural rigor goes into actionable edits in the triad. Zero artifact clutter.
2. **Autonomous Execution Without Approval**: Do NOT pause or block to ask the user for approval before editing the triad. Directly apply the architectural, sequencing, testing, and commit contract improvements.
3. **Crisp, High-Signal Chat Reporting**: When finishing, report back to the user with a concise, high-signal summary in the chat response following [resources/output-template.md](resources/output-template.md) (decisions made, changes applied to the triad, top risks resolved, and execution readiness). Do not duplicate full files in chat; deliver a clear summary that is not too long, but does not omit essential details.
4. **Follow I-VSD Integration**: Bind updates to exact plan/tasks and I-VSD revisions. If architectural refinements change provider authority, affected stakeholders, or `IVSD-*` mappings, mark the I-VSD report `stale` in the triad metadata and record the revalidation need; do not fabricate approval.
5. **Codebase Reality Over Aspiration**: Distinguish verified codebase reality from plan aspiration. Verify claims against real repository files using `code-review-graph` before codifying them in the triad.
6. **Socratic Stress-Testing & "Worst Break" Catastrophic Scenario**: Identify the single most catastrophic production failure mode. Mandate that Phase Red in `tasks.md` contains dedicated failing invariant tests proving it is prevented before handler implementation.
7. **3-Dimensional Evaluation Model**: Enforce Completeness (capabilities, I-VSD mitigations), Correctness (boundary conditions, concurrency races, negative failure paths), and Coherence (Clean Architecture, HAL link affordances, tenant isolation, transactional outbox).
8. **Invariant-First & Anti-Tautology Verification**: Enforce strict Test-First Invariant order in `tasks.md` (failing Red Phase tests before Green Phase implementation for core domain invariants, concurrency, and security). Prohibit tautological mock-mirroring (`Received(1)` on internal services) or framework boilerplate.
9. **Greenfield Breaking Change Posture**: ISLAMU Event is pre-v1 with 0 external adopters. Reject backward-compatibility shims, deprecated aliases, and adapter baggage. Directly simplify contracts and delete obsolete paths in the plan.
10. **4-Point "Right-Sizing" Rule**: Mandate a PR split when 2+ symptoms match (multi-intent "and also" scope, > 8-10 major tasks, big-bang layer mixing, or backend slice could ship independently). Scope the active triad to the primary slice and graduate deferred scope to `dev/backlog/<slug>.md`.
11. **Per-Phase Planned Commit Readiness & Atomic Slicing**: Ensure every phase in `tasks.md` has a self-sufficient declarative Conventional Commit contract (or atomic commit sequence if large/multi-concern) with exact metadata, commit paths, inspection commands, `git add`, path-limited `git commit`, and verification command. Commits strictly stage only plan-related files on the task branch.
12. **Knowledge Graduation**: Move deferred scope to `dev/backlog/<slug>.md`, durable architectural decisions to `docs/internal/adr/`, and lessons to `dev/_journal/`.
13. **Zero-Loss Information Preservation**: Eliminating separate review files does NOT mean discarding review intelligence. Every critical finding, 3D evaluation scorecard, Socratic stress-test challenge, ranked risk with minimum acceptable fix, "Worst Break" failure mode, and architectural trade-off MUST be permanently written into its dedicated section in `plan.md` (§0, §2, §5, §7.1, §12, §13/§14.2), `context.md` (Key Decisions, Review State), and `tasks.md` (Phase Red Invariant Tests). Chat output is strictly an executive summary of what is already durably preserved in the triad.

## Workflow

1. **Ingest Triad**: Read `dev/active/<task>/<task>-plan.md`, `...-context.md`, and `...-tasks.md`. Verify architectural claims against actual repository code using `code-review-graph`.
2. **Audit Architecture**: Evaluate against the 3D Scorecard, 4-Point Right-Sizing, Worst Break failure scenario, and greenfield breaking change principles.
3. **Directly Update Triad**:
   - `plan.md`: Refine architecture, tighten sequence, define RFC 2119 behavior scenarios, remove legacy shims, update metadata CTO review status to `Applied & Aligned (YYYY-MM-DD)`.
   - `context.md`: Synchronize active status, next step, key decisions, validation baseline, and blockers.
   - `tasks.md`: Restructure into Test-First Invariant ordering (Red -> Green -> Refactor), right-size phases, embed exact atomic commit contracts.
   - *Never write any `*-cto-review.md` file.*
4. **Report to Chat**: Output the crisp, high-signal summary following [resources/output-template.md](resources/output-template.md) (verdict, decisions made, changes applied across the triad, top risks resolved, and execution readiness).

## Verification

- Confirm zero `*-cto-review.md` files exist in `dev/active/<task>/`.
- Validate triad consistency: `plan.md`, `context.md`, and `tasks.md` agree on status, next steps, and phase breakdown.
- Ensure frontmatter adheres to [../_SKILL_SCHEMA.md](../_SKILL_SCHEMA.md).
