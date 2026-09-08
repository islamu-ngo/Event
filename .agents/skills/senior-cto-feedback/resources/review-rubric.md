<!-- ABOUTME: Scoring and decision rubric for Senior CTO reviews of /dev-docs implementation-plan workstreams. -->
<!-- ABOUTME: Focuses on architecture integrity, self-hosting, security, sequencing, and directly updating the workstream triad. -->
# Senior CTO Review Rubric

Use this rubric to evaluate implementation plans for enterprise-grade self-hostable software and directly refine the active workstream triad (`plan.md`, `context.md`, `tasks.md`).

Score only when useful. Prefer practical judgment over mechanical scoring.

The CTO does not write a separate review markdown file. Instead, this rubric guides direct, in-place refinements to the triad and structures the high-signal chat summary.

## 0. The 3-Dimensional Evaluation Scorecard

Evaluate every plan across three primary dimensions:

| Dimension | Core Question | Primary Failure Indicators |
|---|---|---|
| **Completeness** | Are all declared capabilities, I-VSD mitigations, requirements, and execution tasks present without gaps? | Missing edge cases, skipped I-VSD mitigations, missing rollback plan, undeclared task dependencies. |
| **Correctness** | Do invariant test scenarios cover boundary conditions, concurrency races, and negative failure paths? | Tautological "Ugly Mirror" mock tests, framework boilerplate tests, missing negative scenarios, unverified database queries, missing tenant predicates. |
| **Coherence** | Does the design adhere to Clean Architecture, HAL link affordances, tenant isolation, and transactional outbox patterns? | Layer pollution (DTOs in Domain, logic in Controllers), UI-local authorization, backward-compatibility shims, unstated open assumptions. |

## 1. Strategic Fit

| Score | Meaning |
|---|---|
| 5 | Clearly advances product/platform strategy with limited accidental complexity and clean breaking changes |
| 3 | Useful but scope, sequencing, ownership, or operator value is unclear |
| 1 | Adds complexity, backward-compatibility baggage, or test bloat without platform value |

Questions:

- Does this solve a real product/platform problem?
- Does it cleanly break and replace obsolete contracts instead of adding backward-compatibility shims?
- Is this the right layer of the system for the capability?
- Does it reduce future complexity or create a new permanent burden?
- Is the plan aligned with self-hostable and enterprise expectations?
- Is the workstream narrow enough to execute without turning into an unreviewable mega-PR?

## 2. Islamic Value-Sensitive Design

| Score | Meaning |
|---|---|
| 5 | A dated, linked I-VSD report traces provider-controlled decisions from principles and stakeholders to evidence, mitigations, implementation tasks, uncertainty, and escalation |
| 3 | The report exists, but traceability, evidence limits, or implementation ownership is incomplete |
| 1 | The I-VSD deliverable is missing, unlinked, or reduced to unsupported moral claims |

Check:

- `plan.md`, `context.md`, and `tasks.md` link the same `islamic-value-sensitive-design/i-vsd-*.md`.
- The report distinguishes provider responsibility from religious rulings or certification.
- Applicable principles, stakeholders, risks, mitigations, and evidence are traceable to plan tasks.
- Missing evidence and Sunni scholarly escalation needs are explicit.
- Approval is blocked when the report or material traceability is missing.

## 3. Socratic Stress-Testing & "The Worst Break" Adversarial Check

| Score | Meaning |
|---|---|
| 5 | Material claims survived evidence-grounded challenge, every unresolved fork has a decision owner, and "The Worst Break" failure mode has a dedicated Invariant-Breaker test |
| 3 | Major risks were challenged, but some thresholds, failure modes, or edge cases remain vague |
| 1 | The plan relies on optimistic assumptions and generic assurance instead of adversarial validation |

Check:

- **"The Worst Break" Check**: Has the plan identified the single most catastrophic failure mode (e.g. money double-capture, tenant data leakage, outbox message loss) and authored a dedicated failing test for it in Phase Red?
- Rollback and recovery claims identify concrete failure points.
- Tenant boundaries and authorization paths fail closed.
- Performance claims state measurable thresholds and representative cardinality.
- Operator actions and diagnostics are unambiguous.
- Edge cases and external dependency failures have explicit outcomes.
- Remaining material decisions were resolved through `grill-me` or block approval.

## 4. Architecture Integrity

| Score | Meaning |
|---|---|
| 5 | Clean boundaries, correct ownership, simple contracts |
| 3 | Mostly correct, but some orchestration or responsibility leakage |
| 1 | Mixed layers, fat components/controllers, hidden coupling |

Check:

- Domain owns domain invariants.
- Application/CQRS owns use-case orchestration.
- Persistence owns data access, not DTO mapping.
- API is transport and contract boundary, not business logic owner.
- Blazor/BFF does not become a security authority.
- UI uses server-provided affordances where applicable.
- Generated clients and OpenAPI stay stable and intentional.

## 5. Security and Trust Boundaries

| Score | Meaning |
|---|---|
| 5 | Trust boundaries are explicit, enforced server-side, and tested |
| 3 | Basic security exists but edge cases are under-specified |
| 1 | UI/client/config is trusted incorrectly or authz is vague |

Check:

- No privileged behavior depends on browser-controlled headers or client-only checks.
- Unsafe BFF endpoints use antiforgery where applicable.
- Authorization is enforced server-side.
- Machine/API key callers have scoped authority when relevant.
- Secrets never enter UI or logs.
- Fail-closed behavior is explicit for authz and policy calls.
- Admin/operator actions are separated from tenant/admin actions.

## 6. Multi-Tenancy and Isolation

| Score | Meaning |
|---|---|
| 5 | Tenant resolution, data filters, admin overrides, and cross-tenant operations are explicit |
| 3 | Tenant behavior is mentioned but not fully tested |
| 1 | Tenant isolation is assumed, not designed |

Check:

- Tenant context source is clear.
- Query filters or explicit tenant predicates are preserved.
- Any filter bypass is named, scoped, and tested.
- Instance-admin operations cannot casually leak tenant data.
- Single-tenant and multi-tenant mode behavior is defined.
- Tenant-scoped configuration has governance/lock behavior where needed.

## 7. Data Model and Migration Quality

| Score | Meaning |
|---|---|
| 5 | Data ownership, constraints, indexes, migrations, and rollback/reset paths are clear |
| 3 | Entity shape is plausible but migration/index/constraint story is incomplete |
| 1 | Data is modeled ad hoc or persistence impact is hand-waved |

Check:

- New tables have clear ownership and lifecycle.
- Indexes match expected query patterns.
- Constraints protect invariants.
- Seed IDs and lookup IDs are stable where relevant.
- Soft delete/audit/tenant markers are correct.
- Migration order and data migration are explicit.
- Breaking data changes have a reset, migration, or operator runbook.

## 8. API and Contract Quality

| Score | Meaning |
|---|---|
| 5 | Contracts are canonical, named, generated, tested, and documented |
| 3 | API works but naming/versioning/client regeneration is incomplete |
| 1 | Contracts are improvised or duplicate paths/semantics |

Check:

- Single canonical route shape.
- Stable route names and operation IDs.
- Clear request/response DTOs.
- HAL/HATEOAS behavior considered where relevant.
- RFC7807 ProblemDetails used for errors.
- OpenAPI export/client generation is sequenced.
- Breaking contract changes are explicit.

## 9. Self-Hosting and Operations

| Score | Meaning |
|---|---|
| 5 | Operators can deploy, configure, observe, recover, and upgrade |
| 3 | Basic config exists but docs/internal/health/recovery are incomplete |
| 1 | Works only for local dev or SaaS assumptions |

Check:

- Environment variables and defaults are documented.
- Docker Compose/deployment impact is known.
- Health checks cover new dependencies.
- Logs/metrics/traces expose failure modes.
- Upgrade path is documented.
- Failure mode is safe and understandable.
- External dependencies are optional or clearly required.
- Single-server and constrained-resource scenarios are considered.

## 10. Testability, Test-First Invariants, and Anti-Tautology

| Score | Meaning |
|---|---|
| 5 | Strict Test-First Invariant Specification: Failing contract/invariant tests are sequenced *before* implementation code (Red Phase), preventing post-hoc test tautology; high-leverage concurrency, state transition, and real DB tests are prioritized over shallow mocks |
| 3 | Tests exist and match the risk profile, but task sequencing leaves code-before-test ambiguity |
| 1 | Tests are grouped into a post-hoc phase, written after implementation, or rely on shallow mock-heavy tests that mirror bugs ("The Ugly Mirror") |

Check:

- Behavioral tasks follow Test-First Invariant order (Task N.1: Failing Invariant/Contract Tests $\rightarrow$ Task N.2: Implementation).
- Tests are specified against public contracts (MediatR requests, API endpoints, ProblemDetails RFC 7807, database state invariants) rather than private implementation details.
- High-leverage tests are prioritized (concurrency races, state machines, row locking, zero-PII log sinks) over low-value getter/setter mocks.
- Unit tests cover domain/application logic; integration tests cover persistence/API behavior.
- Architecture tests enforce conventions; BFF tests cover cookie/token/header behavior.
- Each phase uses one Release build and at most one fastest relevant non-browser project test, with no app-running or manual/browser verification lane.
- Obsolete compatibility tests are deleted when breaking changes are accepted.

## 11. Sequencing, Delivery Safety, and the 4-Point "Right-Sizing" Rule

| Score | Meaning |
|---|---|
| 5 | Work is split into reviewable, independently verifiable slices; satisfies all 4 right-sizing checks |
| 3 | Sequence is plausible but risks large PRs or late discovery |
| 1 | Big-bang plan with mixed concerns, oversized scope ("and also"), and no rollback |

Check:

- **The 4-Point "Right-Sizing" Rule** (Mandate **"Split before approval"** if 2+ match):
  1. *Multi-Intent Scope*: The proposal or goals read like a list of distinct capabilities joined by "and also".
  2. *Excessive Task Capacity*: The plan contains more than 8-10 major actionable tasks, making single-PR review exhausting.
  3. *Big-Bang Layer Mixing*: Data migration, domain logic, API contract churn, and UI enablement are combined into a single phase instead of decoupled slices.
  4. *Independent Shipping Value*: The backend application/API slice could safely ship and be verified before any UI enablement.
- Migration and model changes happen before UI reliance.
- Contract stabilization happens before client/UI churn.
- Security/tenant tests land before feature expansion.
- Docs update with behavior changes.
- Each PR has clear exit criteria.
- Rollback/reset path exists for self-hosters.
- Every phase declares exact phase-owned paths and closes with its verification disposition followed immediately by task(s) containing concrete planned declarative Conventional Commit contract(s).
- If a phase is large (touching dozens or hundreds of files) or spans multiple separable concerns, it MUST provide an ordered sequence of atomic commit contracts following `conventional-commit` rules 1 and 13; reject monolithic umbrella commits unless provably indivisible under rule 14.
- Every planned default title uses an allowed capability/engineering scope and benefit-led subject; its exact description explains the phase motivation and data/control flow; its changelog treatment and trailers satisfy `conventional-commit`.
- Every contract contains exact commit paths, declarative type/scope/title/description, and trailers. No phase leaves placeholders, generic “complete phase” wording, or message composition to the implementation agent.
- Planning and CTO review load `conventional-commit`; the approved task embeds everything normal execution needs. The implementation agent must not reload the skill when using the truthful default.
- The approved tasks file authorizes the implementing agent to commit on the task branch in the same session; no final commit-only session or repeat user invocation is required.
- The implementation agent must use the planned contract unchanged while truthful. Only an allowed override loads `conventional-commit`; triggers are explicit user-driven outcome changes, atomic phase splits, material implementation divergence, changed breaking/change-fragment classification, or factual invalidity.
- Any override must be recorded before commit with the reason and an updated declarative contract; stylistic preference is rejected.
- Task branch staging is explicit-path only (`git add -- <paths>`). The plan forbids blind staging (`git add .`, `git add -A`).
- Phase-attributable failures block the commit and must be resolved before phase completion.
- The phase executes the commit on the task branch and confirms clean git status before completion; atomicity requires multiple atomic commits for large phases rather than an umbrella phase commit.

## 12. Dev-Docs Quality

| Score | Meaning |
|---|---|
| 5 | `plan.md` and `tasks.md` (and `context.md` if present) are consistent, current, and implementation-ready |
| 3 | Useful artifacts exist, but one file is stale, vague, or inconsistent |
| 1 | The workstream is not resumable by another agent without rediscovery |

Check:

- `plan.md` distinguishes verified evidence from assumptions and defines high-level architectural phase exit criteria without embedding granular task execution checklists, `- [ ]` checkboxes, or session handoffs.
- `context.md` (when present) has current progress, next step, blockers, validation baseline, and dated handoffs.
- `tasks.md` maps cleanly to phases and contains the hot execution ledger (Red/Green task sequence, phase-owned paths, verification disposition, concrete planned declarative commit contracts, tightly governed overrides, and immediate commit checkboxes).
- Status across all artifacts agrees.
- Another implementation agent could resume without re-asking the user for core context.
- Knowledge Graduation: Out-of-scope, follow-up, or deferred items are explicitly identified for promotion to `dev/backlog/<slug>.md` (and durable architecture to `docs/internal/adr/`), rather than being left as dead text in an ephemeral plan.

## CTO Decisions & Direct Triad Actions

The CTO decides the path forward and applies it directly to the triad without creating review files:

- **Approved as Refined** — The plan's architecture is sound or has been directly refined to be execution-ready; update metadata to `Applied & Aligned` in `plan.md`, synchronize `context.md`, and ensure `tasks.md` has complete Red/Green sequences and atomic commit contracts.
- **Split Applied** — Scope exceeded right-sizing limits (2+ conditions met); directly restructure the active triad to scope strictly to the primary slice, and graduate deferred scope to `dev/backlog/<slug>.md`.
- **Scope Pruned & Re-Aligned** — Direction was viable but contained bloat or backward-compatibility shims; directly excise the legacy shims from `plan.md` and `tasks.md`.
- **Reject / Defer** — Fundamental architectural flaw or missing foundational prerequisite; update `context.md` with explicit blockers and explain the rationale in the chat summary.
