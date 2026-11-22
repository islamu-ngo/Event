<!-- ABOUTME: Required structure for repository-grounded implementation plan files. -->
<!-- ABOUTME: Preserves evidence, architecture decisions, executable phases, risks, and implementation-agent duties. -->

# Plan Template

Use this structure for `dev/active/<task-name>/<task-name>-plan.md`.

## Header

```markdown
# <Human Title> — Implementation Plan

Last Updated: YYYY-MM-DD Europe/Brussels
```

## 0. Planning Metadata

Record:

- original request and task directory;
- planning status: Draft, User-reviewed, Approved, In implementation, or Re-baselined;
- **Change Classification:**
  - `Behavioral Delta` — Introduces `ADDED`, `MODIFIED`, or `REMOVED` capabilities and observable system behavior. Requires formal RFC 2119 requirements and `WHEN`/`THEN` scenarios in Section 3.
  - `Non-Behavioral Delta` (skip-specs equivalent) — Pure refactor, performance optimization, architectural migration, tooling, or documentation with zero externally observable behavioral changes. Requires structural/performance benchmarks in place of behavioral scenarios.
- matched intents, relevant skills, and relevant rules;
- primary layers touched;
- S/M/L/XL complexity with evidence-based rationale;
- **Programme shape:** the stable PR scope IDs in dependency order with a one-line outcome each, and why the split (or single scope) gives independently mergeable outcomes. Mutable delivery, reconciliation, cleanup and delegation state lives in the `tasks.md` registry, never here;
- **Plane Ticket Reference:** the S item or M parent: `EVENT-XXX`, or `pending: event/work/active-<task-slug>.yaml` until execution mints it;
- **Plane Tracking Plan** (sizing: [plan-tracking](../../plane-compose/resources/plan-tracking.md)): the plan shape and one row per item the plan owns, nothing more:

  ```markdown
  **Shape:** S (1 PR) | M (N PRs, one capability) | L (program, approved)  ·  **New items:** N (≤ 6 without approval)

  | Item | Role | Type | Labels | Parent Epic | Version Module | Priority | Pull request / phases |
  |---|---|---|---|---|---|---|---|
  | pending (active file) or EVENT-N (bound) | M parent | Feature | API, Web App | EVENT-6851 Event Lifecycle, Sessions & Publishing | v0.1.0 | high | Programme grouping only; no PR branch |
  | Sub-item (created at execution) | Sub-item | Feature | API | (M parent UUID) | v0.1.0 | high | PR-FOUNDATION: Phases 1–4 |
  | Sub-item (created at execution) | Sub-item | Feature | Web App | (M parent UUID) | v0.1.0 | high | PR-CLIENT: Phases 5–6 |
  ```

  This example is shape M. For shape S, use one Work Item under the capability Epic mapped to the single PR, without a grouping parent or Sub-items. Explain any reuse (bound existing item) or approval needed (more than 6 items, new Epic). Phases inside one PR, tests, docs, reviews, and verification gates never get rows.
- **I-VSD Document:** `[islamic-value-sensitive-design/workstreams/i-vsd-<task-name>.md](../../../islamic-value-sensitive-design/workstreams/i-vsd-<task-name>.md)`;
- **I-VSD Status / Disposition:** `current` plus `plan-aligned`, or the blocking state;
- **CTO Review:** `Not reviewed`, `Changes required`, `Approved`, or the linked review artifact;
- **User Approval:** `Awaiting approval` or `Approved`;
- **Grill-Me Intake:** a concise resolved-decisions summary, including recommendations accepted or rejected and any explicitly deferred branch.

## 1. Executive Summary

Lead with the concrete problem and what changes for users/operators or engineering work. Then explain the core approach and explicit non-goals, keeping detailed architecture in Section 5. Apply [reader-first writing](../../conventional-commit/resources/reader-first-writing.md); a workstream name is not an explanation.

## 2. Source-Grounded Current State Report

### 2.0 Pre-Flight Structural Context (Blast Radius)

Inject bounded Turn 1 knowledge-graph impact slice:

```yaml
# Injected Structural Context (Pre-Flight Blast Radius)
Target: <Namespace.ClassName.MethodName>
Callers (Upstream):
  - <Controller.Action> (Route: <route_template>)
  - <Blazor.Component.Handler>
Callees (Downstream):
  - <Repository.Method>
  - <Outbox.EnqueueAsync> (Event: <DomainEventName>)
Impacted Flows:
  - Flow: <BusinessFlowName> (Criticality: <Tier>)
Test Coverage:
  - <PathToUnitTests>
  - <PathToIntegrationTests>
```

### 2.1 Evidence Log

Use a table with `Claim`, `Evidence`, `Confidence`, and `Notes`.

### 2.2 Existing Implementation

Describe verified behavior by owning layer.

### 2.3 Existing Tests And Verification Coverage

Name verified test projects/files, protected behavior, and explicit gaps.

### 2.4 Existing Documentation And Contracts

List relevant docs, API/OpenAPI contracts, generated clients, configuration, policies, schemas, and runbooks.

### 2.5 Current Pain Points / Improvement Areas

Tie concrete correctness, security, UX, accessibility, performance, maintenance, duplication, and test gaps to evidence.

### 2.6 Unknowns After Investigation (Strict Deferrable Open Questions Rule)

For each unknown, record what was searched and the implementation task that will resolve it.

> [!IMPORTANT]
> **Open Questions Rule**: Open questions in this section are **strictly for genuinely deferrable details** that will not alter scope, architectural patterns, API contracts, or task sequencing. If an unknown would shift what gets built, how layers interact, or the task breakdown, it **MUST be resolved before writing tasks** (via repository research or Socratic `/grill-me` intake). Do not bake an unstated assumption into the task list.

## 3. Proposed Future State: Behavioral Contract & Scenarios

Describe the target externally observable behavior contract. Separate **Behavior (What the system does)** from **Code (How it is built)**:
- Do **NOT** mention internal class names, repository methods, database columns, or libraries here (those belong in Section 5 Architecture).
- Use **RFC 2119 normative keywords** (`SHALL`, `MUST`, `SHOULD`, `MAY`).
- Every requirement MUST have at least one testable **`WHEN` / `THEN` Scenario**.

```markdown
### Requirement: <Capability / Feature Name>
The system SHALL <observable behavior, invariant, or contract promise>.

#### Scenario: <Scenario Name (Happy Path / Boundary / Error)>
- **GIVEN** <initial state or preconditions>
- **WHEN** <triggering action, command, or event occurs>
- **THEN** <observable outcome, state transition, or RFC 7807 ProblemDetails returned>

#### Scenario: <Adversarial / Catastrophic "Worst Break" Scenario>
- **GIVEN** <concurrent requests, expired capability token, or tenant boundary challenge>
- **WHEN** <adversarial race condition or unauthorized actor attempts operation>
- **THEN** <fail-closed rejection, atomic rollback, and zero data leakage>
```

*(For `Non-Behavioral Delta` changes, summarize the structural/performance invariants that must remain strictly invariant during the refactor).*

## 4. Non-Negotiable Constraints

Reference the matched contract and list only task-relevant repository, security, tenant, architecture, API, UI, compatibility, and documentation constraints.

## 5. Architecture And Design Decisions

For each decision include:

- **Decision**
- **Why**
- **Alternatives considered**
- **Consequences**
- **Files/layers affected**

When the decision is a major technology selection, external library, or competing architectural design, run `robin-neutral` steelmanning first. Record the strongest viable alternatives, their trade-offs, and why each rejected approach lost; do not mix this technical comparison into the I-VSD analysis.

## 6. PR Scopes And Implementation Phases

Group every phase under the stable PR scope that merges it, following the [PR scope contract](pr-scope-contract.md#required-scope-definition). A one-PR programme still uses one scope heading. Detail the next candidate scope fully; later scopes keep rolling detail but never lose their required outcomes, scenarios, dependencies, acceptance or I-VSD IDs.

```markdown
## PR-<OUTCOME>: <Descriptive outcome>
- **Independently usable outcome:** what a reviewer can merge and use on its own
- **Requirements / scenarios satisfied:** Section 3 IDs
- **Predecessors and contracts:** scope IDs; merged contracts consumed or changed
- **Readiness conditions:** what must hold before this scope can become Ready (merged and reconciled predecessors, refreshed contracts, resolved blockers)
- **Owned paths and exclusions:**
- **I-VSD IDs:**
- **Acceptance:** observable criteria this PR alone must satisfy before merge
- **PR acceptance gate:** checks this PR's changed surface and matched intents require (see Section 7)
- **Release / docs impact:**
- **Rollback / recovery:**
- **Downstream obligations:** contracts later scopes rely on
- **Plane item:** the Plane Tracking Plan row for this PR; phases never get their own items

### Phase N: <Name>
- **Goal:**
- **Depends on:**
- **Relevant files:** existing/new status included
- **Phase-owned paths:** exact files this phase may stage; update this list when legitimate phase work discovers or generates another file
- **Related skills/rules:**
- **Acceptance criteria:** observable architectural and contract outcomes (bullet list, not execution checkboxes)
- **Phase gate (run once after all tasks):** the targeted check from Section 7, or the Tier 4 documentation checks
- **Phase-close commit outcome:** one benefit-led sentence from which `tasks.md` defines the exact default Conventional Commit message
- **Rollback / failure handling:**
```

A scope that merely renames a large PR fails readiness when smaller independently mergeable outcomes exist. Do not move requested work to backlog to fit a size.

> [!IMPORTANT]
> **Single Responsibility Rule (`plan.md` vs `tasks.md`)**:
> - `plan.md` defines **architectural phases, goals, component files, phase-level acceptance criteria, verification commands, and rollback strategy**.
> - Do **NOT** include granular `#### Task N.M:` execution blocks, task descriptions/effort, or `[ ]` / `[x]` checkboxes in `plan.md`.
> - All granular task breakdowns (`Task N.M`), Red/Green/Refactor task sequences, and actionable execution checklists belong **strictly in `tasks.md`**.
> - Ephemeral session progress, shared working-tree dirty scopes, test pass counts, and handoffs belong **strictly in `context.md`**.

#### Behavioral Slice Rule: Invariant-First Slicing (in `tasks.md`)
To prevent **Post-Hoc Test Tautology ("The Ugly Mirror")** on critical paths while avoiding excessive test toil on standard orchestration, structure tasks in `tasks.md` with appropriate rigor:

1. **High-Criticality Slices (Money, Concurrency Races, Aggregate State Machines, Security Boundaries):**
   - **Task N.1 (Red Phase): Author Invariant & Contract Specification Tests for Scenarios <X.Y>**
     - Author failing tests against domain invariants, checked arithmetic, aggregate state transitions, fail-closed error responses (RFC 7807), or tenant isolation *before* implementing production logic.
   - **Task N.2 (Green Phase): Implement Handlers, Entities & Domain Logic**
     - Author production C# code strictly to satisfy the invariant contracts.
2. **Standard Slices (CQRS Orchestration, API Endpoints, UI Components):**
   - Implement the slice directly and verify against public contracts (integration route responses, HAL affordances, domain value objects).
   - Prohibit tautological mock-mirroring (`NSubstitute.Received(1)` on internal repositories/caches), framework cancellation tests, or raw C#/CSS text scraping.

3. **Task N.Next (Refactor & Wire Up): Clean Architecture & DI Registration**
   - Refactor for performance, memory allocation, and zero-PII logging (`StarRedactor`/`HmacRedactor`), and wire DI service registrations.

#### Atomic Task Verification Rule (in `tasks.md`)
Every task checkbox in `tasks.md` MUST include its explicit verification assertion:
```markdown
- [ ] Task 1.1: Author failing invariant tests for Scenario 1.A (RSVP hold expiration) and verify test fails as red anchor
- [ ] Task 1.2: Implement RsvpAggregate hold expiration logic and verify unit tests pass via `--treenode-filter`
```

Do not create standalone manual-QA, documentation-review, reporting, dev-doc maintenance, or redundant verification tasks. Keep required Ring 1 slices and Red invariant-breaker checks; run the full phase gate once when phase implementation is complete.

#### Per-Phase Closing Rule: Verify, Then Commit (in `tasks.md`)
Every implementation phase MUST end with a **Phase N Commit** task immediately after its phase-verification tasks. The approved task ledger is standing authorization for the same implementing agent to execute the commit; do not defer commit composition to a new session or require another user invocation.

##### Atomic Commit Slicing for Large Phases (Rule 1 & Rule 13)
When a phase is large (touching dozens or hundreds of files) or spans multiple separable architectural concerns (domain models & invariant tests, persistence & migrations, application CQRS handlers, API endpoints, Blazor UI, documentation), planning MUST NOT create a single monolithic umbrella commit.
Instead, planning MUST sequence multiple atomic commit contracts (`#### Planned Commit Contract N.1`, `#### Planned Commit Contract N.2`, etc.) adhering to `conventional-commit`:
- **Smallest Releasable Slice**: Each commit represents the smallest complete, independently reviewable and verifiable outcome.
- **Indivisible Exception Only (Rule 14)**: A commit touching dozens or hundreds of files is permitted ONLY when the change is mechanically indivisible across the repository (e.g. repository-wide symbol renames or generated client/schema regenerations that cannot compile independently). “Same feature” or “same phase” is never sufficient.

##### Task-Branch Scope Isolation (Plan-Owned Changes Only)
Every commit contract MUST strictly stage and commit ONLY its own changes directly related to the implementation plan on the selected PR's task branch (`feat/EVENT-XXX-<task>`):
- **Explicit Path Staging**: Staging MUST explicitly list exact, phase-owned file paths (`git add -- <file1> <file2>`). Blind staging (`git add .`, `git add -A`, `git add -u`) is strictly forbidden.
- **Declarative Contract**: Planning defines the semantic commit metadata, leaving the native git execution to the harness agent.
- **Post-Commit Verification**: Account for every remaining working-tree change on the PR branch before proceeding.

While writing or updating the workstream, the planning agent MUST load `conventional-commit` and place fully resolved declarative contracts in every phase's `tasks.md` section (sequencing multiple contracts when the phase is large):

```markdown
#### Planned Commit Contract [or Planned Commit Contract N.1 for multi-commit phases]
- **Type & Scope:** `type(scope)`
- **Title:** `concrete changed behavior or engineering outcome`
- **Description:** Exact problem and reader-visible consequence, followed by the relevant technical mechanism, data/control flow, and constraints.
- **Changelog treatment:** Public feature/fix | Change fragment `CHG-YYYY-NNNN` | `Changelog: skip`
- **Required trailers:** Exact terminal trailer lines, or `None`
- **Commit paths:** Exact ordered list of wholly phase-owned files for this commit.
- **Message override:** Not overridden
```

Final workstream artifacts MUST contain concrete values, never the template placeholders above. Exact commit packets remain execution metadata in `tasks.md`; `plan.md` carries only the phase's benefit-led commit outcome.

The phase-close task MUST:

1. Treat the approved contract(s) as self-sufficient. If the planned contract remains truthful, use it directly and do not load `conventional-commit`.
2. Stage exact phase-owned paths using `git add -- <paths>` and execute the commit on the selected PR's task branch. If multiple atomic commits are planned for the phase, execute each in sequence. Never substitute blind/broad staging.
3. Treat phase-attributable build/test failures as blockers. Resolve all failures before committing.
4. Use the planned default title, description, changelog treatment, and trailers unchanged when they remain truthful. The implementing agent MUST NOT rewrite them for style or preference.
5. Account for remaining working-tree changes on the PR branch before marking the phase complete and proceeding.

An override is exceptional and allowed only when explicit user feedback changed the phase outcome, the phase had to split into multiple atomic commits, the implemented outcome materially differs from the approved design, the breaking/change-fragment classification changed, or the planned message became factually false. Only then does implementation load [`conventional-commit`](../../conventional-commit/SKILL.md). Before committing, set `Message override: Yes`, record `Reason`, and add an `Actual commit contracts` list repeating the declarative schema above. Apply normal plan update triggers when owned state changes.

Create any required changelog fragment, generated artifact, schema, runbook, or documentation in the phase that owns that outcome, before that phase's verification and commit. There is no final-phase-only catch-all commit.

Plan **Knowledge Graduation** inside the PR that makes the decision or discovers the lesson: durable architectural decisions go to `docs/internal/adr/` and non-obvious lessons to `dev/_journal/domains/`, committed with that PR so they survive removal of its execution copy. Only optional follow-ups the user explicitly deferred go to `dev/backlog/<slug>.md`; required outcomes stay in later PR scopes of this triad. The canonical programme and shared Plane file retire only under the [contract's completion rule](pr-scope-contract.md#completion-and-retention), never because one PR merged.

## 7. Testing Strategy

Keep this section short and high-leverage (strict **Quality over Quantity**):
1. **Invariant Anchors**: Detail which test project (e.g., `Event.Domain.UnitTests`, `Event.Application.UnitTests`, `Event.API.IntegrationTests`, `Event.Persistence.IntegrationTests`) hosts the invariant and contract tests.
2. **High-Leverage Adversarial Scenarios**: Prioritize high-value invariant tests (concurrency races, state machine exhaustiveness, real DB transaction boundaries, zero-PII log sinks, tenant isolation) over shallow mock-heavy boilerplate tests.
3. **Proportional Gates**: Apply the [verification gates](quality-gates.md#verification-gates) and the authoritative [verification policy](../../../../docs/internal/OPERATIONS.md#verification-policy) and [testing rings](../../../../docs/internal/TESTING.md):
   - **Phase gate**: one targeted Release build and at most one fastest relevant project test on one provider, sequential per the execution profile. A broad shared-tree failure proven unrelated is an external blocker, not phase-attributable.
   - **PR acceptance gate**: before merge, every check the PR's changed surface and matched intents require — for example the applicable provider matrix and migrations for a persistence PR, security invariant-breakers, integration or UI verification a path rule mandates. A later PR never owns the first validation of already merged changes.
   - **Programme acceptance**: cross-capability scenarios run once their prerequisite scopes have merged.
   - **Tier 4**: documentation and agent-context PRs run metadata, link, schema and diff checks, never .NET builds or tests.
   - **User overrides**: an explicit programme-specific verification override is recorded in this section and context, applies only to this programme, and every PR handoff discloses the checks it left unrun.

Record additional intent-mandated projects as contract requirements and assign each to the gate of the PR that changes its surface; do not create artificial test-only phases or add runtime checks no rule or changed surface requires.

## 8. Documentation, Configuration, And Operations Impact

Name the exact docs, schemas, generated artifacts, settings, environment variables, Aspire/Compose files, deployment material, and runbooks to update or state why none apply.

### 8.1 Release, Changelog, And Phase Commit Strategy (Procedural Contribution)

Every implementation plan MUST classify its procedural changelog approach across the 3-tier release model. Planning pre-authors each phase's exact message metadata, commit paths, and executable Git command packet in `tasks.md`. When a phase touches dozens or hundreds of files across multiple concerns, planning sequences an ordered series of atomic commits instead of an oversized umbrella commit. Every phase then closes with that approved self-sufficient contract; release artifacts are created in the owning phase rather than deferred to one catch-all commit:

1. **Tier 1 — Standard Feature or Fix (Conventional Commits):**
   - Use public capability scopes from `eng/release/policy/scope-registry.yaml` (e.g. `feat(event): ...`, `fix(auth): ...`).
   - The release engine automatically aggregates these into `What's Changed` at release time.
2. **Tier 2 — High-Impact / Breaking / Migration / Security / Operator Impact (Change Fragment):**
   - The owning phase MUST include a task creating an append-only change fragment under `docs/internal/releases/changes/CHG-YYYY-NNNN.yaml`.
   - The task acceptance criteria must enforce valid YAML structure, `ReleaseInputPolicy` validation, and terminal commit footer `Change-Id: CHG-YYYY-NNNN` (plus `BREAKING CHANGE:` where applicable).
3. **Tier 3 — Internal Architecture / DevOps / Refactoring (Explicit Skip):**
   - The plan must specify terminal trailers: `Changelog: skip` and `Changelog-Reason: <clear-reason>` to prevent internal plumbing noise from leaking into public release notes.

## 9. Islamic Value-Sensitive Design (I-VSD) & Moral Boundaries

Link the mapped `[I-VSD report](../../../islamic-value-sensitive-design/workstreams/i-vsd-<task-name>.md)` and record its reviewed-input revision, status, and disposition. Map every material report ID:

| I-VSD ID | Finding / mitigation status | Scenario and task mapping | Disposition |
|---|---|---|---|
| `IVSD-F001` / `IVSD-M001` | Open / accepted | Scenario 3.1; Task 2.1 | Implement |

Each ID maps to a named scenario/task, explicit non-applicability with rationale, or named escalation gate. Trace applicable provider-controlled decisions from principle and affected stakeholder to risk, mitigation, evidence, and uncertainty. State scholarly escalation needs and non-applicable domains explicitly. The same report path, revision, status, and disposition must appear in task-owned context and tasks.

## 10. Security, Authorization, Privacy, And Abuse Considerations

Cover trust boundaries, authentication, server-side authorization, tenant isolation, HAL affordances, rate limiting, idempotency, auditability, privacy, sensitive-data handling, and abuse controls where relevant.

## 11. Multi-Tenancy, Federation, Localization, Accessibility, And Product Considerations

Mark every concern Applicable, Not Applicable, or Needs Investigation and explain the classification.

## 12. Observability And Operations

Plan bounded logs, metrics, traces, health/readiness, troubleshooting, operator-visible failure modes, and recovery where relevant.

## 13. Migration And Compatibility Plan

Cover database/schema/data migration, seed changes, generated contracts, deployment order, rollback/reset, and breaking-change notes. Do not add compatibility shims unless explicitly approved.

## 14. Risk Register

Use a table with `Risk`, `Likelihood`, `Impact`, `Mitigation`, `Detection Signal`, and `Owner/Task`.

## 15. Success Metrics And Definition Of Done

Define observable functional success per PR scope and for the whole programme. Each phase closes with its targeted gate and phase-owned Conventional Commit; each PR is done only after its acceptance gate, merge and acceptance reconciliation; the programme is done only when every retained outcome is satisfied or explicitly removed by the user and final programme acceptance is resolved.

## 16. Implementation Agent Contract — KEEP DEV DOCS CURRENT

Require future implementation agents to:

1. At implementation start or resume, read `context.md` first, select the active PR scope from the `tasks.md` registry under the [PR scope contract](pr-scope-contract.md#selection-and-readiness), then read that scope's live ledger and only the plan heading it names.
2. During an uninterrupted session, do not reread unchanged plan/tasks; reopen only an invalidated exact section.
3. Within the selected scope, start from its highest-priority open task unless the user overrides it; never infer scope or topology from old checked tasks.
4. Treat `tasks.md` as the hot execution ledger: batch task checkbox completions at logical phase milestones or phase verification gates rather than churning status on every minor edit.
5. Keep implementation-task, phase-verification, and phase-commit checkboxes separate; the phase is complete only after verification is resolved and its phase-owned commit succeeds.
6. Update the task status summary, phase task list, and deferred work whenever task state changes.
7. Update `context.md` only when pausing, transferring across sessions, or handling unexpected blockers; do not churn it for trivial progress.
8. Update the plan only when scope, architecture, phase order, acceptance criteria, risks, or validation strategy changes.
9. Record failed validation with the known cause and next recovery action in tasks; resolve all phase failures before committing.
10. Stage only phase-owned paths modified for this plan on the selected PR's task branch.
11. Run phase verification only after all phase tasks, using the planned targeted gate; do not repeat successful commands. Run the PR acceptance gate before review handoff.
12. Immediately after verification passes, execute the planned declarative commit contract directly on the PR branch without reloading `conventional-commit`.
13. Load `conventional-commit` only when a permitted material divergence means the planned contract will not be used, recording the reason and replacement contract.
14. Stage exact phase-owned paths and account for remaining working-tree changes before completing the phase.
15. Stop at the selected PR's review handoff after reconciling delivery into the canonical scope section; never continue into the next PR in the same branch.
16. Before pausing, compaction, or session transfer, reconcile affected tasks and add a concise dated handoff in `context.md`.
17. Never report completion when repository reality and the task ledger disagree.

Require every implementation summary to teach:

- what changed and why;
- architecture/design patterns, libraries, infrastructure, protocols, and project abstractions used;
- important files, classes, handlers, services, and components with their responsibilities;
- data/control flow;
- relevant repository conventions and reliability/security practices;
- verification performed, remaining work, next work, and dev-doc update status.

## 17. Progress Reporting Contract

Require this response shape after each implementation slice:

```text
PR scope: stable ID, descriptive outcome, delivery state
Implemented: developer teaching summary
Verified: exact evidence
Remaining: incomplete or deferred work
Next: recommended next slice
Docs updated: yes/no with reason
```

For completed implementation work, `Docs updated` must confirm that `tasks.md` was reconciled. Report context and plan separately as updated or unchanged because no trigger occurred.

## 18. Potential Risks & Unknowns

End with a candid, specific critique of the part most likely to fail, expand, or require a decision.
