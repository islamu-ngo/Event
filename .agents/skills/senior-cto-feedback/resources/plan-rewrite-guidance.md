<!-- ABOUTME: Direct triad update guidance for Senior CTO reviews of implementation-plan workstreams. -->
<!-- ABOUTME: Converts architectural critique into executable plan.md, context.md, and tasks.md updates without creating review files. -->
# Plan Rewrite Guidance

This guidance governs how the Senior CTO review directly refines, tightens, and updates the workstream triad (`plan.md`, `context.md`, `tasks.md`).

Senior CTO review **never** writes `*-cto-review.md` files. Instead, 100% of the CTO's review brain and architectural rigor is applied directly as in-place edits to the existing `plan.md`, `context.md`, and `tasks.md` so future agents can implement from them directly without friction or artifact clutter. No prior approval is required before applying these edits.

## Rewrite Principles

Edit the triad at the exact path requested by the user. Do not create worktrees, create or switch branches, relocate planning artifacts, or execute implementation during review. Planned commit packets describe later execution only.

A better implementation plan should be:

- reviewable in bounded implementation increments,
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

Preserve the complete requested scope in the same canonical plan/context/tasks triad, organized as programme -> PR scope -> phase -> task under the [PR scope contract](../../implementation-plan/resources/pr-scope-contract.md). Each PR scope has a stable ID, an independently mergeable outcome, prerequisites, readiness conditions, owned paths, acceptance, its PR acceptance gate and rollback. Retain later UI, integration, packaging and qualification work in later scopes with rolling detail. Do not create or populate backlog files to satisfy a size threshold. Only explicit user direction can remove a requested outcome; completion still requires every retained outcome.

## Delegation-Safe Rewrites

Read the `tasks.md` PR registry before editing. Planned and Ready scopes, shared decisions and future dependencies may be rewritten directly. An Implementing or In review scope is a **delegated snapshot**: its live ledger belongs to the execution owner. Do not rewrite that section; record the proposed amendment, its reason and its effect on later scopes in `context.md` for the owner to reconcile, and leave the scope's registry fields unchanged. If a review finding invalidates active delegated work, report it as a blocker for that owner rather than editing around them. Never replace the canonical triad with an execution copy.

## Rewrite The Whole Workstream

Do not rewrite only the strategy and leave the other two files stale.

When improving a workstream:

1. tighten `...-plan.md` to reflect the real architecture and sequence;
2. update `...-context.md` so the current status, next step, and risks match the rewritten plan;
3. update `...-tasks.md` so each undelegated PR scope, phase and verification step maps to the rewritten plan and the registry reflects scope changes;
4. remove or rename tasks that no longer match the recommended direction;
5. compare the rewrite against I-VSD refresh triggers and synchronize review state;
6. close every phase with an immediate, phase-owned `conventional-commit` task after verification, including a concrete planning-authored default message; never retain a final-only catch-all commit or implementation-time message placeholder.

## Zero-Loss Information Preservation (Where Review Data Lives)

Eliminating separate `*-cto-review.md` files must **never** result in lost review intelligence. Chat output is ephemeral, whereas the triad (`plan.md`, `context.md`, `tasks.md`) is durable and persistent across sessions.

Every piece of architectural analysis, risk profiling, adversarial stress-testing, and scoring must be permanently recorded into its designated location across the triad:

| Review Dimension / Element | Destination in the Triad | How & Why It Is Preserved |
|---|---|---|
| **3D Evaluation Scorecard**<br>(Completeness, Correctness, Coherence) | `plan.md` §0 Planning Metadata & `context.md` Review State | Records the baseline audit scores, gate status, and alignment date permanently in metadata so future sessions know the exact architectural evaluation. |
| **Source-Free Research & Seams Evidence** | `plan.md` §2 Source-Grounded Current State (Evidence Log, Seams) | Codebase reality, verified types, callers/callees, extension seams, and AST evidence live directly in §2.1–§2.9 of the plan. |
| **Socratic Stress-Testing Challenges**<br>(Scenarios, Questions, Edge Cases) | `plan.md` §3 Proposed Future State & §5 Architecture Decisions | Converts Socratic challenges directly into explicit RFC 2119 requirements (WHEN/THEN behavior rules) and concrete architectural invariants. |
| **"The Worst Break" Catastrophic Failure Mode** | `plan.md` §7.1 Testing Strategy / §9 Security & `tasks.md` Phase Red Tasks | Documents the catastrophic production failure mode in the testing strategy, and immediately translates it into failing Invariant-Breaker specification tests in Phase Red before implementation. |
| **Ranked Top Risks & Minimum Acceptable Fixes**<br>(Blocker, Critical, Major) | `plan.md` §13/§14.2 Risk Register & `context.md` Known Risks | Preserves ranked risks with concrete mitigations, severity tiers, and verification triggers in the risk register rather than an isolated review document. |
| **Breaking Deletions & Legacy Elimination** | `plan.md` §12 Migration & Compatibility Plan & `context.md` Key Decisions | Explicitly records obsolete code, endpoints, tables, and adapter shims marked for outright deletion under greenfield development principles. |
| **PR Scope Boundaries & Readiness** | `plan.md` §6 scope definitions & `tasks.md` PR Registry | Stable scope IDs, independently mergeable outcomes, prerequisites and readiness conditions survive across sessions; delegated snapshots are left for their live owner. |
| **Test-First Sequences & Atomic Commits** | `tasks.md` Phase Checklist & Planned Commit Contracts | Turns architectural advice into an executable, verifiable sequence of Red -> Green -> Refactor tasks with path-limited Conventional Commits. |
| **Plane Tracking Plan** | `plan.md` §0 Plane Tracking Plan, `tasks.md` Plane Tracking checkboxes & `event/work/active-<task-slug>.yaml` | Right-sizes the items the plan owns (one per separately merged PR; no phase, test, or review items; at most 6 new items without approval), fixes Epic parent, labels, priority, and module, and sets state `Backlog` -> `Ready` on approval so plan approval and the Plane board agree. |

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

## 6. PR Scopes And Implementation Phases

## PR-FOUNDATION: <independently mergeable outcome>
Prerequisites / consumed contracts:
Readiness conditions:
Owned paths and exclusions:
Acceptance and PR acceptance gate:
Release / docs impact and rollback:

### Phase 1 — Foundation
Goal:
Files:
Phase-owned paths:
Phase gate:
Exit criteria:
Phase-close commit outcome: <benefit-led phase outcome>

## PR-CONTRACT: <outcome>
(same scope fields; phases nested beneath)

## PR-CLIENT: <outcome>
(same scope fields; rolling detail allowed until it is the next candidate)

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
## Programme Resume Pointer
## Key Files And Responsibilities
## Key Decisions
## Constraints And Rules To Remember
## Validation Baseline
## Current Known Risks / Unknowns
## Handoff Notes
```

Rewrite rules:

- `NEXT` and Quick Resume must name the active PR scope (an Implementing or In review scope or its pending finalization) or, failing that, the earliest Planned candidate and its readiness conditions; never a hard-coded first phase or first unchecked historical task.
- blockers must be current, not historical clutter.
- key decisions must include any breaking-change choice and operator impact.

## Required `tasks.md` Rewrite Shape

Ensure the tasks file is execution-grade:

```markdown
# <Workstream Name> — Task Checklist

Last Updated: YYYY-MM-DD Europe/Brussels

## Status Summary
## PR Registry
## Implementation Maintenance Rules
## PR-<OUTCOME>: ... (readiness; live or delegated snapshot)
### Phase 1: ...
### Phase 2: ...
### Phase N Verification — targeted phase gate (Tier 4: documentation checks)
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
- During implementation, stage exact phase-owned paths using `git add -- <paths>` and execute the declarative commit contract. Preserve unrelated work.
- Load `conventional-commit` only when a permitted material divergence override replaces the default contract.
### PR-<OUTCOME> Acceptance Gate — before review handoff
## Retained Obligations By PR Scope
```

Rewrite rules:

- every PR scope and major plan phase should appear in tasks, grouped under its scope ID, with every scope in the registry;
- every risky boundary should have observable acceptance criteria in its owning implementation task;
- each phase should name its targeted phase gate, and each PR its acceptance gate, per the planner's [verification gates](../../implementation-plan/resources/quality-gates.md#verification-gates);
- each phase should list exact phase-owned paths and place its commit task(s) immediately after verification;
- each phase commit should contain declarative metadata: type, scope, title, description, changelog treatment, trailers, and commit paths;
- if a phase is large (touching dozens or hundreds of files) or spans multiple separable concerns, mandate an ordered sequence of atomic commit contracts rather than one monolithic umbrella commit;
- implementation commits stage and commit ONLY plan-owned changes; the reviewer does not execute these commands;
- the implementing agent executes that self-sufficient contract without reloading `conventional-commit`;
- overrides should be rare and are the only execution path that loads `conventional-commit`;
- phase-attributable failures block commit and must be resolved before phase completion;
- runtime, browser, container or provider-matrix checks appear only where a matched intent, path rule or the PR's changed surface requires them, in that PR's acceptance gate; remove such checks nothing requires, and never remove a required one;
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

## Multi-PR Delivery Guidance

A multiphase plan is not automatically a single PR, and a phase is never a PR. When right-sizing finds separable, independently mergeable outcomes, give each a stable PR scope ID inside the same programme triad, with explicit prerequisites and readiness conditions, and update the Plane Tracking Plan to one item per PR. Keep together changes whose separate merge would leave contradictory or unusable intermediate states, and record why.

The following are possible PR scopes inside one programme, not separate
workstreams or instructions to remove later scope. Use boundaries appropriate
to the target and keep source/caller/test/docs slices coherent:


1. PR-FOUNDATION — data/foundation
   - domain entities,
   - EF configuration,
   - migrations,
   - repository changes,
   - persistence tests.
2. PR-CONTRACT — application/API contract
   - commands/queries,
   - validators,
   - controllers,
   - authorization,
   - ProblemDetails,
   - OpenAPI,
   - API integration tests.
3. PR-CLIENT — client/BFF/UI
   - generated client update,
   - Blazor services,
   - components/pages,
   - BFF endpoints,
   - component/BFF tests.
4. PR-OPERATIONS — operations/docs/internal/cleanup
   - configuration docs,
   - self-hosting docs,
   - operations docs,
   - cleanup obsolete compatibility paths,
   - delete obsolete tests.

These are implementation risk boundaries, not permission for a final umbrella commit or review-time implementation. Each scope passes its own acceptance gate before merge (for example, PR-FOUNDATION runs its applicable provider matrix and migrations), and each phase inside it closes with its own verified, phase-owned Conventional Commit.

## Plane Tracking Plan Audit

Audit plan §0 **Plane Tracking Plan** and `event/work/active-<task-slug>.yaml` against [plan-tracking](../../plane-compose/resources/plan-tracking.md) and [taxonomy §4.1](../../../../docs/internal/project-management/work-item-taxonomy-and-granularity-report.md#41-tracking-budget-per-implementation-plan-plan--plane-mapping).

- **Fail flooding:** an item per phase, test, doc, review, verification gate or commit; more than `6` new items or a new Epic without the user's approval.
- **Fail under-tracking:** several separately merged PRs under one item without Sub-items, two providers or capabilities in one item, or substantial work with no item.
- **Check:** an existing item was searched for and reused where one fits; every new item has `parent:` = the right capability Epic UUID, Surface + Domain labels (not the retiring `Docs` label), `priority` as functional criticality, a Version Module, `state: Backlog`, no `assignees:`, and `id:` omitted while unpushed; Sub-items are listed in the plan, not yet in YAML; each PR scope names its item; no commit or PR logs sit in `description:`.
- **Right-sizing changes:** when the number of merged PRs changes, update the Tracking Plan to match (an S plan that becomes multi-PR turns into a parent plus Sub-items).
- Rewrite a wrong Tracking Plan in place. On `Applied & Aligned`, set the active item to `state: Ready` in YAML (local only; execution pushes it). This marks plan readiness for tracking only; individual PR scopes still become Ready only through their own readiness check.

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
| “Update UI” | “Update UI after the API contract and generated client are stable.” |
