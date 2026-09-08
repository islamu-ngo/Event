<!-- ABOUTME: Completion and self-review gates for implementation-plan workstreams. -->
<!-- ABOUTME: Prevents unverified claims, vague tasks, artifact drift, and accidental implementation claims. -->

# Quality Gates

## Intake Gate (I-VSD & Grill-Me)

- `islamic-value-sensitive-design/i-vsd-<task-name>.md` exists, contains `Last Updated: YYYY-MM-DD`, and is linked from the task-owned plan, context, and tasks files.
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

## Contract Gate

- Every relevant implementation intent was captured.
- **Change Classification** is explicitly declared (`Behavioral Delta` vs `Non-Behavioral Delta`).
- Intent docs, skills, rules, scope, tests, docs impact, acceptance criteria, and forbidden moves are reflected in the plan.
- The **Release, Changelog, And Phase Commit Strategy** is classified. Every phase has planning-authored declarative commit metadata (type, scope, title, description, changelog treatment, trailers, and commit paths) validated against `conventional-commit`; no placeholder remains. For large phases (touching dozens or hundreds of files across multiple concerns), planning sequences multiple atomic commit contracts rather than one monolithic umbrella commit.
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
- **Clean Architecture Slicing & 3-Ring Progressive Verification**:
  - **Ring 1 (Inner Loop, < 2s)**: Subtasks specify fast in-memory TUnit slicing (`--treenode-filter "/*/*/*<TestClass>/*"`) targeting `Event.Domain.UnitTests` or `Event.Application.UnitTests`. Zero Docker containers or network I/O in the inner loop. Pure domain invariants (normalization, validation, state machines) belong in Domain unit tests (< 50ms).
  - **Ring 2 (Phase Exit Gate, < 15s)**: Every intermediate phase ends with exactly one Release build (`dotnet build -c Release -v q`) and at most one fastest relevant project test against ONE canonical provider (e.g. SQLite in-memory or single PostgreSQL container). **Planning multi-database provider matrix runs or container sweeps during intermediate phases is strictly forbidden.**
  - **Ring 3 (Plan Exit Gate)**: The full 5-database provider matrix (PostgreSQL, SQLite, SQL Server, MySQL), EF Core migrations, and `Event.Architecture.Tests` are planned strictly at the final plan exit before PR creation.
- **Yak-Shaving Quarantine Rule**: The plan must forbid agents from absorbing, debugging, or fixing pre-existing test rot or broken fixtures outside the task scope. When unrelated tests fail, agents must reproduce on clean base, log under `*-context.md` / `dev/backlog/`, quarantine the failure, and proceed with the assigned deliverable.
- Tests are specified against public contracts (MediatR requests, HTTP routes, ProblemDetails RFC 7807, database states) rather than private implementation details.
- Every phase then ends with a self-sufficient declarative commit contract. If a phase touches dozens or hundreds of files or multiple separable layers, it sequences multiple atomic commit contracts following `conventional-commit`. The commit stages and commits strictly phase-owned files on the task branch (`feat/<task-name>`), verifying clean status before completing the phase. Normal execution does not reload `conventional-commit`.
- Message overrides are limited to explicit user-driven outcome changes, atomic phase splits, material implementation divergence, changed breaking/change-fragment classification, or a planned message that became factually false. Stylistic preference is never sufficient.
- Only an allowed override loads `conventional-commit`; every resulting actual contract repeats the declarative schema before any commit executes.
- Phase-attributable failures block the commit and must be resolved before phase completion.
- The plan contains no app startup, Playwright, browser automation, Chrome DevTools MCP, visual QA, E2E, Docker/Aspire startup, live-service smoke, or manual runtime walkthrough.

## Continuity Gate

- The stable task name and `Last Updated: YYYY-MM-DD Europe/Brussels` appear in all three files.
- Plan, context, and tasks agree on status, current priority, next action, blockers, task ids, decisions, risks, and verification.
- Plan, context, and tasks agree on I-VSD path, status/disposition, CTO-review status, and user-approval status.
- **Dev-Doc Triad Single Responsibility**: `plan.md` defines architectural phase exit criteria without embedding granular task execution checklists, checkboxes (`- [ ]`), or session handoffs; `tasks.md` is the sole hot execution ledger; `context.md` is the ephemeral session memory and handoff log.
- Context puts resume state and blockers near the top.
- Tasks are checkable and mirror the plan's phases.
- The implementation-agent contract makes `tasks.md` the hot ledger, batches task updates at phase milestones, and separates implementation completion, verification disposition, and the phase commit.
- Every phase records exact phase-owned paths, a concrete planned declarative commit contract, and post-verification commit checkboxes; completed phases confirm clean status on the feature branch.
- Context and plan update triggers are narrow enough to prevent documentation churn, while a dated handoff remains mandatory before pause or transfer.
- Resume guidance reads tasks first and only relevant plan sections, avoiding repeated full-workstream rereads.

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

If this workflow skill or other agent-context infrastructure changed, run:

```bash
dotnet test --project tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --verbosity quiet
git diff --check -- .agents/skills .agents/contract tests/Event.Architecture.Tests
```

Run the planned implementation test suite only during implementation, not while producing the plan. Record known baseline failures honestly in context and tasks.

## Final Response (Self-Contained Executive Plan Brief)

The final response presenting the plan MUST be a **Self-Contained Executive Plan Brief** so the user can understand, review, and approve the plan directly in chat without being forced to open `dev/active/<task-name>/`:

```text
Created/updated implementation plan for `<task-name>`:
- dev/active/<task-name>/<task-name>-plan.md
- dev/active/<task-name>/<task-name>-context.md
- dev/active/<task-name>/<task-name>-tasks.md

### Executive Summary & Architectural Approach
<2-3 sentences explaining what will change, the core architectural design pattern, and the primary business/platform benefit.>

### Phase Roadmap
- **Phase 1: <Descriptive Name>** — <What is built, layers touched, and invariants verified>
- **Phase 2: <Descriptive Name>** — <What is built, layers touched, and invariants verified>
- ...

### Key Decisions & Trade-offs
- <Bullet points on major technical or architectural choices made during intake/planning>

### Potential Risks & Hardest Unknowns
<Short evidence-grounded paragraph naming the hardest unresolved area or edge case.>

### Recommended Next Step & Approval Request
<Explicit call for approval or specific direction to proceed with Phase 1, with options if applicable.>
```

Do not say implementation started. If re-baselining, summarize what materially changed in the planning artifacts.
