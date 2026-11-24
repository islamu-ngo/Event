<!-- ABOUTME: Completion and self-review gates for implementation-plan workstreams. -->
<!-- ABOUTME: Prevents unverified claims, vague tasks, artifact drift, and accidental implementation claims. -->

# Quality Gates

## Intake Gate (I-VSD & Grill-Me)

- `islamic-value-sensitive-design/workstreams/i-vsd-<task-name>.md` exists, contains `Last Updated: YYYY-MM-DD`, and is linked from the task-owned plan, context, and tasks files.
- The I-VSD report follows `planning` mode, names its workstream, is `current`, and has a `plan-aligned` disposition after the completed triad was revalidated.
- The I-VSD report traces applicable principles, stakeholders, provider-controlled decisions, evidence, stable finding/mitigation IDs, uncertainty, refresh triggers, and escalation boundaries.
- Every material `IVSD-*` ID maps in plan Section 9 to a named scenario/task, explicit non-applicability, or named escalation gate; the same report path and status appears in plan, context, and tasks.
- Architectural, product, failure-mode, and edge-case ambiguities were first resolved from repository evidence, then decided through `grill-me` with recommendations rather than filled with assumptions.
- Any conditional `robin-neutral` technology or architecture comparison is recorded in plan Section 5 and remains separate from the I-VSD assessment.

## Evidence Gate

- Every existing path, project, symbol, route, contract, setting, and test named in current state was verified by search and relevant file reads.
- Missing items are labeled not found or new; they are never described as implemented.
- Evidence entries separate repository facts, source-derived constraints, decisions, assumptions, and unknowns.
- Current-state strengths and pain points both cite concrete evidence.
- Related active or paused work was checked for overlap and conflicts.

## PR Scope Gate

- Every phase and task in plan, tasks and context sits under a stable PR scope ID; reordering scopes did not rename them.
- Each scope states the [required definition](pr-scope-contract.md#required-scope-definition): independently usable outcome, requirements/scenarios, predecessors and consumed contracts, owned paths and exclusions, I-VSD IDs, acceptance, PR acceptance gate, release/docs impact, rollback, and downstream obligations.
- Each scope is independently mergeable. A large PR renamed as a scope fails when smaller independently mergeable outcomes exist; a split that leaves contradictory merged instructions or unusable intermediate states also fails.
- Readiness conditions are explicit; an approved programme does not make every future scope Ready.
- The next candidate is executable in detail; later scopes may carry rolling detail but retain every required outcome, dependency and acceptance criterion. No required work moved to backlog.
- The `tasks.md` registry lists every scope with delivery, reconciliation, cleanup and delegation fields; `plan.md` carries none of that mutable state.
- Re-baselines left delegated snapshots untouched and recorded amendments for their live owner.
- Each PR scope maps to exactly one item row in the Plane Tracking Plan; M/L grouping parents have no PR branch. Never create rows per phase.

## Contract Gate

- Every relevant implementation intent was captured.
- **Change Classification** is explicitly declared (`Behavioral Delta` vs `Non-Behavioral Delta`).
- Intent docs, skills, rules, scope, tests, docs impact, acceptance criteria, and forbidden moves are reflected in the plan.
- The **Release, Changelog, And Phase Commit Strategy** is classified. Every phase has planning-authored declarative commit metadata (type, scope, title, description, changelog treatment — automatic internal omission, public release visibility, optional explicit skip, or optional high-impact fragment — trailers, and commit paths) validated against `conventional-commit`; no placeholder remains. For large phases (touching dozens or hundreds of files across multiple concerns), planning sequences multiple atomic commit contracts rather than one monolithic umbrella commit.
- Security, authorization, privacy, abuse, multi-tenancy, federation, localization, accessibility, product, observability, operations, and migration are each classified with rationale.
- Clean Architecture ownership and API/HAL/BFF trust boundaries are explicit where applicable.
- **Greenfield Breaking Change Freedom**: Backward-compatibility shims, deprecated route aliases, and legacy adapters are forbidden in pre-v1. Cleanly break and replace obsolete structures.

## Executability & Invariant-First Gate

- The plan reports current state before proposed future state.
- **Behavior vs. Code Separation**: Section 3 defines observable system behavior using RFC 2119 keywords (`SHALL`/`MUST`) and concrete `WHEN`/`THEN` scenarios without polluting behavioral contracts with internal class names or library choices.
- **Invariant-First Slicing**: High-criticality slices (money, concurrency races, aggregate state transitions, security boundaries) sequence authoring failing invariant tests (Task N.1: Red Phase) directly bound to named Section 3 Scenarios *before* implementing production logic (Task N.2: Green Phase). Standard feature/CQRS orchestration and UI slices implement directly and verify against public contracts.
- **Strict Quality Over Quantity in Tests**: Tests MUST NOT use mock-mirroring (`NSubstitute.Received(1)` on internal repositories/caches), framework-testing boilerplate (testing EF Core cancellation), or raw C#/CSS text scraping. Tests assert observable outcomes, state transitions, and RFC 7807 ProblemDetails.
- **Atomic Verification Criteria**: Every single task checkbox in `tasks.md` states its concrete verification assertion (`- [ ] N.M ... and verify ...`).
- **Strict Deferrable Open Questions**: Unknowns in Section 2.6 contain only genuinely deferrable details; non-deferrable branches were resolved during intake.
- Every task names exact files or a bounded investigation that will discover them.
- Every task includes observable acceptance criteria, dependencies, effort, and required guidance.
- Phases are reviewable slices with rollback or failure-diagnosis guidance.
- **Clean Architecture Slicing & Proportional Verification**: verification follows the [Verification Gates](#verification-gates) below.
- **Yak-Shaving Quarantine Rule**: The plan must forbid agents from absorbing, debugging, or fixing pre-existing test rot or broken fixtures outside the task scope. When unrelated tests fail, agents must reproduce on clean base, log under `*-context.md` / `dev/backlog/`, quarantine the failure, and proceed with the assigned deliverable.
- Tests are specified against public contracts (native CQS requests, HTTP routes, ProblemDetails RFC 7807, database states) rather than private implementation details.
- Every phase then ends with a self-sufficient declarative commit contract. If a phase touches dozens or hundreds of files or multiple separable layers, it sequences multiple atomic commit contracts following `conventional-commit`. The commit stages and commits strictly phase-owned files on the selected PR's task branch, accounting for remaining working-tree changes before completing the phase. Normal execution does not reload `conventional-commit`.
- Message overrides are limited to explicit user-driven outcome changes, atomic phase splits, material implementation divergence, changed breaking/change-fragment classification, or a planned message that became factually false. Stylistic preference is never sufficient.
- Only an allowed override loads `conventional-commit`; every resulting actual contract repeats the declarative schema before any commit executes.
- Phase-attributable failures block the commit and must be resolved before phase completion.
- Runtime, browser, container, or provider-matrix checks appear only where a matched intent, path rule, or the PR's changed surface requires them, and then in that PR's acceptance gate rather than as phase-level ritual.

## Verification Gates

These gates apply the authoritative [verification policy](../../../../docs/internal/OPERATIONS.md#verification-policy), [testing rings](../../../../docs/internal/TESTING.md) and [AGENTS.md §8](../../../../AGENTS.md#8-verification-baseline) to PR-scoped programmes; they do not weaken any product check.

- **Ring 1 (inner loop, < 2s)**: subtasks use in-memory TUnit slicing (`--treenode-filter "/*/*/*<TestClass>/*"`) in Domain or Application unit tests; no containers or network I/O. Required security invariant-breaker Red tests run once before their implementation.
- **Phase gate (Ring 2, < 15s)**: one targeted Release build and at most one fastest relevant project test against one selected provider. Never plan provider matrices or container sweeps at intermediate phases.
- **PR acceptance gate**: before review handoff and merge, every check the PR's changed surface and matched intents require — the applicable provider matrix and migrations for persistence changes, architecture tests, security invariant-breakers, integration or UI verification mandated by a path rule such as the [Blazor workflow](../../../../docs/internal/BLAZOR_DEV_WORKFLOW.md). Checks cannot be deferred merely because more programme PRs remain; a later PR never owns the first validation of merged changes.
- **Programme acceptance**: cross-capability scenarios are planned once, after their prerequisite scopes merge, and must be resolved before the programme retires.
- **Tier 4**: documentation and agent-context scopes use metadata, link, schema and diff checks; never .NET builds or tests.
- **Execution profile**: builds and tests run sequentially one project at a time under the selected resource profile (AGENTS rule 21).
- **User overrides**: an explicit programme-specific verification override is recorded in plan Section 7 and context, never generalized to other programmes, and each PR handoff discloses the checks it left unrun.

## Continuity Gate

- The stable task name and `Last Updated: YYYY-MM-DD Europe/Brussels` appear in all three files.
- Plan, context, and tasks agree on status, PR scope IDs, registry state, current priority, next action, blockers, task ids, decisions, risks, and verification.
- Plan, context, and tasks agree on I-VSD path, status/disposition, CTO-review status, and user-approval status.
- **Dev-Doc Triad Single Responsibility**: `plan.md` defines architectural phase exit criteria without embedding granular task execution checklists, checkboxes (`- [ ]`), or session handoffs; `tasks.md` is the sole hot execution ledger; `context.md` is the ephemeral session memory and handoff log.
- Context puts resume state and blockers near the top.
- Tasks are checkable and mirror the plan's phases.
- The implementation-agent contract makes `tasks.md` the hot ledger, batches task updates at phase milestones, and separates implementation completion, verification disposition, and the phase commit.
- Every phase records exact phase-owned paths, a concrete planned declarative commit contract, and post-verification commit checkboxes; completed phases account for remaining working-tree changes.
- Executive summaries, planned commit messages, and decision briefs pass the [shared readability check](../../conventional-commit/resources/reader-first-writing.md#readability-check): concrete meaning first, sufficient technical detail next.
- Context and plan update triggers are narrow enough to prevent documentation churn, while a dated handoff remains mandatory before pause or transfer.
- Resume guidance is context-first: it selects the active PR scope from the registry (active or in-review scope first, then the earliest Planned candidate passing readiness), never the first phase or first unchecked historical task, and reads only that scope's ledger and named plan sections.
- No instruction moves the whole triad into a worktree, keeps two editable full copies, or retires the programme when one PR merges.

## Scope Gate

- Planning artifacts are the only product of this workflow.
- Runtime/application code was not changed.
- The response does not claim the feature or refactor was implemented.
- Unknowns that materially affect design are visible and assigned to investigation or user review.
- The workstream is ready for user correction/approval before implementation begins.
- No artificial test-only, documentation-only, QA-only, reporting-only, or verification-only phase was added.
- No final-only catch-all commit task or separate commit-only session is required; the approved workstream authorizes the implementing agent to close each phase directly.

## Verification

Planning artifacts in `dev/active/<task-name>` are gitignored local working memory; they are accessed and verified directly using native harness file tools by deterministic path.

For prose-only skill changes, check metadata, links, and readability, then run:

```bash
git diff --check -- .agents/skills .agents/contract
```

Validate changed machine-consumed manifests with their repository parser; run product tests only when executable behavior changed.

Run the planned implementation test suite only during implementation, not while producing the plan. Record known baseline failures honestly in context and tasks.

## Final Response (Self-Contained Executive Plan Brief)

The final response presenting the plan MUST be a **Self-Contained Executive Plan Brief** so the user can understand, review, and approve the plan directly in chat without being forced to open `dev/active/<task-name>/`:

```text
Created/updated implementation plan for `<task-name>`:
- dev/active/<task-name>/<task-name>-plan.md
- dev/active/<task-name>/<task-name>-context.md
- dev/active/<task-name>/<task-name>-tasks.md

### Executive Summary & Architectural Approach
<Concrete problem and what will change for users/operators or engineering work.
Then explain the core technical approach and its important constraint.>

### PR Roadmap
- **PR-<OUTCOME> (<descriptive name>)** — <independently mergeable result, its phases, prerequisites, and the acceptance gate it must pass>
- **PR-<NEXT> (<descriptive name>)** — <result, dependency on the earlier scope, rolling-detail status>
- **Programme acceptance** — <cross-capability scenarios that run after the prerequisite scopes merge>

### Key Decisions & Trade-offs
- <Choice and practical consequence, followed by the technical rationale>

### Potential Risks & Hardest Unknowns
<Short evidence-grounded paragraph naming the hardest unresolved area or edge case.>

### Recommended Next Action

Run /senior-cto-feedback in a fresh session if you want an adversarial Senior CTO architectural audit of the plan triad first, or
Run /implement-tasks to start the first Ready PR scope after its readiness check.
```

Do not say implementation started. If re-baselining, summarize what materially changed in the planning artifacts.
