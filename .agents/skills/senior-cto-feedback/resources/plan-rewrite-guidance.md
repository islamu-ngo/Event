<!-- ABOUTME: Direct triad update guidance for Senior CTO reviews of implementation-plan workstreams. -->
<!-- ABOUTME: Converts architectural critique into executable plan.md, context.md, and tasks.md updates without creating review files. -->
# Plan Rewrite Guidance

This guidance governs how the Senior CTO review directly refines, tightens, and updates the workstream triad (`plan.md`, `context.md`, `tasks.md`).

Senior CTO review **never** writes `*-cto-review.md` files. Instead, 100% of the CTO's review brain and architectural rigor is applied directly as in-place edits to the existing `plan.md`, `context.md`, and `tasks.md` so future agents can implement from them directly without friction or artifact clutter. No prior approval is required before applying these edits.

## Rewrite Principles

A better implementation plan should be:

- smaller,
- sequenced,
- testable,
- migration-aware,
- contract-aware,
- security-aware,
- self-hosting-aware,
- explicit about what breaks,
- explicit about what gets deleted,
- explicit about which exact files each phase owns and commits,
- explicit about each phase's exact default commit message and rare override conditions,
- explicit about what future agents must keep updating.

## Rewrite The Whole Workstream

Do not rewrite only the strategy and leave the other two files stale.

When improving a workstream:

1. tighten `...-plan.md` to reflect the real architecture and sequence;
2. update `...-context.md` so the current status, next step, and risks match the rewritten plan;
3. update `...-tasks.md` so each phase and verification step maps to the rewritten plan;
4. remove or rename tasks that no longer match the recommended direction;
5. compare the rewrite against I-VSD refresh triggers and synchronize review state;
6. close every phase with an immediate, phase-owned `conventional-commit` task after verification, including a concrete planning-authored default message; never retain a final-only catch-all commit or implementation-time message placeholder.

## Zero-Loss Information Preservation (Where Review Data Lives)

Eliminating separate `*-cto-review.md` files must **never** result in lost review intelligence. Chat output is ephemeral, whereas the triad (`plan.md`, `context.md`, `tasks.md`) is durable and persistent across sessions.

Every piece of architectural analysis, risk profiling, adversarial stress-testing, and scoring must be permanently recorded into its canonical location across the triad:

| Review Dimension / Element | Destination in the Triad | How & Why It Is Preserved |
|---|---|---|
| **3D Evaluation Scorecard**<br>(Completeness, Correctness, Coherence) | `plan.md` §0 Planning Metadata & `context.md` Review State | Records the baseline audit scores, gate status, and alignment date permanently in metadata so future sessions know the exact architectural evaluation. |
| **Source-Free Research & Seams Evidence** | `plan.md` §2 Source-Grounded Current State (Evidence Log, Seams) | Codebase reality, verified types, callers/callees, extension seams, and AST evidence live directly in §2.1–§2.9 of the plan. |
| **Socratic Stress-Testing Challenges**<br>(Scenarios, Questions, Edge Cases) | `plan.md` §3 Proposed Future State & §5 Architecture Decisions | Converts Socratic challenges directly into explicit RFC 2119 requirements (WHEN/THEN behavior rules) and concrete architectural invariants. |
| **"The Worst Break" Catastrophic Failure Mode** | `plan.md` §7.1 Testing Strategy / §9 Security & `tasks.md` Phase Red Tasks | Documents the catastrophic production failure mode in the testing strategy, and immediately translates it into failing Invariant-Breaker specification tests in Phase Red before implementation. |
| **Ranked Top Risks & Minimum Acceptable Fixes**<br>(Blocker, Critical, Major) | `plan.md` §13/§14.2 Risk Register & `context.md` Known Risks | Preserves ranked risks with concrete mitigations, severity tiers, and verification triggers in the risk register rather than an isolated review document. |
| **Breaking Deletions & Legacy Elimination** | `plan.md` §12 Migration & Compatibility Plan & `context.md` Key Decisions | Explicitly records obsolete code, endpoints, tables, and adapter shims marked for outright deletion under greenfield development principles. |
| **Test-First Sequences & Atomic Commits** | `tasks.md` Phase Checklist & Planned Commit Contracts | Turns architectural advice into an executable, verifiable sequence of Red -> Green -> Refactor tasks with path-limited Conventional Commits. |

## I-VSD Invalidation After Rewrites

Use the refresh triggers in `.agents/skills/i-vsd/resources/integration-contract.md`.

- If the rewrite changes provider authority, affected stakeholders, user defaults/rights, data/AI/telemetry, moderation, monetization, portability, deployment responsibility, an escalation gate, or an `IVSD-*` task mapping, mark the report `stale`.
- Update `plan.md` Section 0 Metadata to show `CTO Review: Applied & Aligned (YYYY-MM-DD)` and record that I-VSD revalidation is required before implementation.
- If the rewrite is wording, formatting, status, evidence-location, or architecture-detail clarification with no provider-responsibility change, preserve the current report and record why no refresh trigger fired.

## Recommended Plan Shape

This shape should align with `.agents/skills/implementation-plan/SKILL.md` and its resources.

```markdown
# <Workstream Name> — Implementation Plan

Last Updated: YYYY-MM-DD Europe/Brussels

## 0. Planning Metadata
- Request
- Task directory
- Planning status
- Matched intents or fallback contract
- Relevant skills
- Relevant rules
- Primary layers touched
- Estimated complexity

## 1. Executive Summary
[What is being changed, why it matters, and what is out of scope.]

## 2. Source-Grounded Current State Report
### 2.1 Evidence Log
### 2.2 Existing Implementation
### 2.3 Existing Tests And Verification Coverage
### 2.4 Existing Documentation And Contracts
### 2.5 Current Pain Points / Improvement Areas
### 2.6 Unknowns After Investigation

## 3. Proposed Future State

## 4. Non-Negotiable Constraints

## 5. Architecture And Design Decisions

## 6. Implementation Phases

### Phase 1 — Foundation
Goal:
Files:
Phase-owned paths:
Tests:
Exit criteria:
Phase-close commit outcome: <benefit-led phase outcome>

### Phase 2 — Contract and Application
Goal:
Files:
Phase-owned paths:
Tests:
Exit criteria:
Phase-close commit outcome:

### Phase 3 — UI/BFF
Goal:
Files:
Phase-owned paths:
Tests:
Exit criteria:
Phase-close commit outcome:

### Phase 4 — Operations, Docs, and Hardening
Goal:
Files:
Phase-owned paths:
Tests:
Exit criteria:
Phase-close commit outcome:

## 7. Testing Strategy
## 8. Documentation, Configuration, And Operations Impact
## 9. Security, Authorization, Privacy, And Abuse Considerations
## 10. Multi-Tenancy, Federation, Localization, Accessibility, And Product Considerations
## 11. Observability And Operations
## 12. Migration And Compatibility Plan
## 13. Risk Register
## 14. Success Metrics And Definition Of Done
## 15. Implementation Agent Contract — KEEP DEV DOCS CURRENT
## 16. Progress Reporting Contract
## 17. Potential Risks & Unknowns
```

## Required `context.md` Rewrite Shape

Ensure the context file remains implementation-resumable:

```markdown
# <Workstream Name> — Context

Last Updated: YYYY-MM-DD Europe/Brussels

## SESSION PROGRESS (YYYY-MM-DD Europe/Brussels)
### ✅ COMPLETED
### 🟡 IN PROGRESS
### ⏭️ NEXT
### ⚠️ BLOCKERS

## Quick Resume
## Key Files And Responsibilities
## Key Decisions
## Constraints And Rules To Remember
## Validation Baseline
## Current Known Risks / Unknowns
## Handoff Notes
```

Rewrite rules:

- `NEXT` must match the first realistic implementation slice from the revised plan.
- blockers must be current, not historical clutter.
- key decisions must include any breaking-change choice and operator impact.

## Required `tasks.md` Rewrite Shape

Ensure the tasks file is execution-grade:

```markdown
# <Workstream Name> — Task Checklist

Last Updated: YYYY-MM-DD Europe/Brussels

## Status Summary
## Implementation Maintenance Rules
## Phase 1: ...
## Phase 2: ...
### Phase N Verification — one Release build and at most one project test
### Phase N Commit(s) — immediately after verification
*(Note: If Phase N is large or touches dozens/hundreds of files across multiple concerns, sequence multiple atomic commit contracts instead of one monolithic umbrella commit).*
#### Planned Commit Contract [or Contract 1 of N for multi-commit phases]
- **Type & Scope:** `type(scope)`
- **Title:** `benefit-led phase outcome`
- **Description:** Exact phase motivation and data/control-flow description.
- **Changelog treatment:** Public feature/fix | Change fragment `CHG-YYYY-NNNN` | `Changelog: skip`
- **Required trailers:** Exact terminal trailer lines, or `None`
- **Commit paths:** Exact ordered list of wholly phase-owned files for this commit.
- **Message override:** Not overridden
<!-- Repeat Planned Commit Contract block for Contract 2, 3, etc. if phase is large -->
#### Commit Tasks
- Stage exact phase-owned paths using `git add -- <paths>` and execute commit using the declarative contract on `feat/<task-name>`. Confirm clean git status before proceeding.
- Load `conventional-commit` only when a permitted material divergence override replaces the default contract.
## Remaining / Deferred Work
```

Rewrite rules:

- every major plan phase should appear in tasks;
- every risky boundary should have observable acceptance criteria in its owning implementation task;
- each phase should name exactly one Release build and at most one fastest relevant non-browser project test at the end;
- each phase should list exact phase-owned paths and place its commit task(s) immediately after verification;
- each phase commit should contain declarative metadata: type, scope, title, description, changelog treatment, trailers, and commit paths;
- if a phase is large (touching dozens or hundreds of files) or spans multiple separable concerns, mandate an ordered sequence of atomic commit contracts rather than one monolithic umbrella commit;
- commits stage and commit ONLY changes directly belonging to the implementation plan on the dedicated task branch (`feat/<task-name>`);
- the implementing agent executes that self-sufficient contract without reloading `conventional-commit`;
- overrides should be rare and are the only execution path that loads `conventional-commit`;
- phase-attributable failures block commit and must be resolved before phase completion;
- no task should start the app/browser or use Playwright, Chrome DevTools MCP, E2E, Aspire/Docker startup, live-service smoke, or a manual runtime walkthrough;
- delete stale tasks created for a direction you are now rejecting.

## Breaking Change Rewrite Pattern

When breaking changes are allowed, replace vague compatibility language with this:

```markdown
## Compatibility Position

This workstream intentionally removes the old `<old behavior>` path.

Reason:
- Preserving it would keep duplicate semantics in `<files/components>`.
- The project is pre-v1 / in active development.
- The new contract is simpler and easier to test.

Impact:
- Existing `<clients/config/data>` must change.
- Generated client must be regenerated.
- Self-hosters must run the migration and update `<env/config>`.

Migration:
- `<migration or reset path>`

Docs:
- Update `<doc files>`.
```

## PR Split Guidance

Prefer splitting by risk boundary:

1. PR 1 — data/foundation
   - domain entities,
   - EF configuration,
   - migrations,
   - repository changes,
   - persistence tests.
2. PR 2 — application/API contract
   - commands/queries,
   - validators,
   - controllers,
   - authorization,
   - ProblemDetails,
   - OpenAPI,
   - API integration tests.
3. PR 3 — client/BFF/UI
   - generated client update,
   - Blazor services,
   - components/pages,
   - BFF endpoints,
   - component/BFF tests.
4. PR 4 — operations/docs/internal/cleanup
   - configuration docs,
   - self-hosting docs,
   - operations docs,
   - cleanup obsolete compatibility paths,
   - delete obsolete tests.

These are risk boundaries, not permission for a final umbrella commit. If represented as phases in one workstream, each boundary closes on its task branch/worktree with its own verified, phase-owned Conventional Commit.

## Test-First Invariant Rewrite Pattern (for `tasks.md`)

When rewriting a flawed workstream where tasks put tests after implementation (creating post-hoc test tautology risk), convert the tasks in **`tasks.md`** into strict **Test-First Invariant Specification order** (while keeping `plan.md` focused on architectural phases and exit criteria):

```markdown
### ❌ Before (Flawed Code-First Sequence in tasks.md):
- Task 2.1: Implement CreateOrderCommandHandler and Order entity
- Task 2.2: Add unit tests for CreateOrderCommandHandler

### ✅ After (Test-First Invariant Sequence in tasks.md):
- [ ] **2.1 (Red Phase): Author CreateOrder Invariant & Contract Specification Tests**
  - **Layer:** Application / Tests
  - **Files:** `tests/Event.Application.UnitTests/Orders/CreateOrderCommandTests.cs` (new)
  - **Description:** Author failing specification tests asserting domain invariants (positive integer currency, state machine initialization, capacity check) and fail-closed error responses (ProblemDetails RFC 7807) before writing handler logic.
  - **Acceptance:**
    - [ ] Stub type/handler compiles cleanly, and tests fail at runtime with expected invariant/assertion failure.
    - [ ] Concurrency and invalid-input invariant test cases covered.
- [ ] **2.2 (Green Phase): Implement Order Aggregate & CreateOrderCommandHandler**
  - **Layer:** Domain / Application
  - **Files:** `src/Explore.Domain/Entities/Order.cs` (new), `src/Explore.Application/Features/Orders/Commands/CreateOrderCommandHandler.cs` (new)
  - **Description:** Author production code strictly to satisfy the invariant test specifications.
- [ ] **2.3 (Refactor & Wire Up): Clean Architecture & DI Registration**
  - **Layer:** Application
  - **Description:** Clean up memory allocations, ensure zero-PII logging via StarRedactor, and register handlers.
```

## Anti-Patterns To Remove From Plans

Replace these phrases:

| Weak phrase | Strong replacement |
|---|---|
| “Maintain backward compatibility for now” | “Delete the old path unless a named self-hoster migration requires it.” |
| “Add tests at the end / after code” | “Author failing Invariant & Contract Specification Tests first (Red Phase), then implement handlers (Green Phase).” |
| “Add tests” | “Add these specific tests for these risks in these projects.” |
| “Make tenant-aware” | “Resolve tenant from X, enforce through Y, test wrong-tenant Z.” |
| “Add config” | “Add env var, default, validation, docs, and failure behavior.” |
| “Add background worker” | “Add idempotent worker with retry, dead-letter, metrics, and recovery.” |
| “Update UI” | “Update UI after canonical API contract and generated client are stable.” |
