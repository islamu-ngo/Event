<!-- ABOUTME: I-VSD planning review for the repository-wide test-suite rationalization. -->
<!-- ABOUTME: Protects stakeholder safety and engineering stewardship while removing low-value tests. -->

# Test Suite Rationalization — I-VSD Planning Review

Last Updated: 2026-08-29

## Review Metadata
- Mode: planning
- Subject: Repository-wide test-suite rationalization
- Workstream: test-suite-rationalization
- Report kind: provider-responsibility planning review
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-08-29
- Reviewed input revision: sha256:72b7d1f34068ec891d112ee7a29302c993e55833d8ae33dbd4d33ea449410373
- Supersedes: none

## Scope
This review covers the provider-controlled decision to reduce and restructure every existing test project, its CI routing, and its agent/developer governance. It covers effects on users, event organizers, instance operators, contributors, maintainers, and downstream communities that rely on security, privacy, accessibility, financial, and concurrency guarantees.

## Claim Boundary
This is a provider-responsibility design assessment, not a fatwa, Sharia certification, legal opinion, or proof that the resulting software is ethically complete. It evaluates whether the planned engineering process preserves duties of safety, trustworthiness, accessibility, stewardship, and accountability while reducing waste.

## Findings

### IVSD-F001 — Safety-critical coverage can be lost through quota-driven deletion
- Lifecycle: open
- Severity: high
- Claim type: provider-controlled engineering risk
- Principle/domain: amanah (trust), prevention of harm, software assurance
- Stakeholders: attendees, organizers, tenant operators, administrators
- Provider-controlled decision: which tests are deleted, replaced, consolidated, or retained
- Evidence: verified concurrency, tenant-isolation, authorization, HAL, BFF, provider, and domain-invariant anchors coexist with large low-value cohorts
- Validation level: repository evidence
- Linked mitigation: IVSD-M001
- Owner/next validation: implementation phases for each owning test project
- Escalation boundary: implementation must stop if a deletion has no invariant/contract disposition

### IVSD-F002 — Test excess consumes contributor attention and infrastructure without proportional assurance
- Lifecycle: accepted
- Severity: medium
- Claim type: stewardship and maintainability
- Principle/domain: avoidance of waste, proportionality, sustainable maintenance
- Stakeholders: contributors, maintainers, self-hosting operators, future users
- Provider-controlled decision: mutation gates, duplicated wrappers, mock-heavy tests, runtime-lane frequency
- Evidence: 10 top-level mutation wrapper directories, 1 nested mutation target, 1,397 `Received` and 1,432 `DidNotReceive` call sites in Application tests, and repeated endpoint permutations
- Validation level: repository search and representative source review
- Linked mitigation: IVSD-M002
- Owner/next validation: governance/topology, Application, API, and CI phases
- Escalation boundary: none before planning approval

### IVSD-F003 — Accessibility and authorization confidence can be weakened by deleting source scrapers without semantic replacements
- Lifecycle: open
- Severity: high
- Claim type: inclusion and security boundary risk
- Principle/domain: justice, accessibility, fail-closed authorization
- Stakeholders: disabled users, RTL-language users, attendees, organizers, tenant operators
- Provider-controlled decision: replacement seams for CSS, markup, HAL, and policy assertions
- Evidence: 33 Blazor test files read source files, while 106 files exercise HAL-link concepts and several bUnit tests already assert rendered semantics
- Validation level: repository search and representative source review
- Linked mitigation: IVSD-M003
- Owner/next validation: Architecture, API, and Blazor Client phases
- Escalation boundary: accessibility or HAL/security coverage may not be deleted until a semantic contract passes

### IVSD-F004 — Timing-dependent tests can normalize nondeterministic engineering practice
- Lifecycle: open
- Severity: medium
- Claim type: reliability and truthful evidence
- Principle/domain: honesty in evidence, operational reliability
- Stakeholders: contributors, operators, users affected by escaped regressions
- Provider-controlled decision: clocks, waits, event subscriptions, bounded timeouts
- Evidence: 479 test files reference `UtcNow`, 56 reference `Task.Delay`, 53 reference `WaitForState`, and `EventCardTests` derives fixtures from the current year
- Validation level: repository search; patterns require semantic classification
- Linked mitigation: IVSD-M004
- Owner/next validation: every project phase, with focused cleanup in Blazor and asynchronous integration cohorts
- Escalation boundary: a timing construct remains only when elapsed time is the behavior under test

## Recommendations

### IVSD-M001 — Require an invariant-disposition ledger
Accepted recommendation: every deleted or consolidated cohort maps to an existing stronger test, a replacement public-contract test, or an explicit obsolete-behavior deletion. Security, privacy, tenant, money, state-machine, concurrency, HAL, and provider-contract tests default to retain until replacement evidence passes.

### IVSD-M002 — Apply quality-over-quantity by project role
Accepted recommendation: remove ephemeral mutation wrappers and interaction-mirroring tests; consolidate repetitive validators and endpoint permutations; isolate release, benchmark, and runtime-provider lanes; retain domain, database-race, trust-boundary, and external-protocol contracts.

### IVSD-M003 — Replace text scrapers with executable semantics
Accepted recommendation: use assembly/type rules for Clean Architecture, runtime endpoint/HAL metadata and HTTP contracts for API behavior, bUnit semantic queries for UI behavior, and machine-readable schema validators for generated artifacts. Do not preserve prose, CSS-token, class-name, or historical task baselines.

### IVSD-M004 — Make asynchronous and temporal evidence deterministic
Accepted recommendation: use fixed `TimeProvider` values, subscribe before triggering asynchronous behavior, and await exact completion/state signals with bounded failure timeouts. Do not use fixed sleeps or current-wall-clock fixtures unless time itself is the contract.

Rejected alternative: deleting a fixed percentage or the report’s estimated file count. It optimizes a quota rather than assurance and could remove high-value tests.

## Stakeholders
- End users and attendees: protected from regressions in privacy, accessibility, payment, admission, and event-state behavior.
- Organizers and tenant operators: depend on tenant isolation, authorization, concurrency, and recovery guarantees.
- Contributors and maintainers: need fast, deterministic, comprehensible feedback.
- Self-hosting operators: need explicit provider/runtime lanes without hidden infrastructure prerequisites.
- Project steward: owns release, licensing, governance, and acceptance of residual risk.

## I-VSD Principles And Domains
- Amanah/trust: test evidence must truthfully protect shipped behavior.
- Prevention of harm: critical boundaries remain adversarially tested.
- Justice and accessibility: semantic accessibility and RTL behavior cannot be traded for velocity.
- Avoidance of waste: redundant infrastructure, mutation wrappers, and mock mirrors should be removed.
- Accountability: deletions and replacements remain traceable to named invariants and phase evidence.

## Common Overlooked Failures And Outcomes
- A generic endpoint matrix can silently omit one controller or HTTP verb.
- A source-scraper deletion can remove the only guard for a critical rule.
- A consolidated validator table can share the same faulty expected-value logic as production.
- Runtime tests can leak into the fast lane and make local feedback unreliable.
- Current active workstreams can continue referencing deleted mutation projects.
- A passing build can hide a test project that executes zero tests.

## Validation Gaps
- Search counts identify candidates, not whether each timing or source-read use is harmful.
- Current wall-clock duration and flake-rate baselines were not executed during planning.
- The knowledge-graph MCP required by repository guidance was unavailable in this session; structural evidence used solution, CI, project, and source inspection instead.

## Escalation Needed
No scholarly escalation is required. Technical escalation is required before deleting any safety-critical test cohort that lacks an accepted invariant-disposition mapping.

## Evidence Reviewed
- Evidence packet digest: `sha256:72b7d1f34068ec891d112ee7a29302c993e55833d8ae33dbd4d33ea449410373`
- Canonical governance: `AGENTS.md`, `.agents/CONTEXT_ENGINEERING.md`, `.agents/rules/tests.md`, `docs/QUICK_REFERENCE.md`, `docs/GOVERNANCE.md`, `docs/OPERATIONS.md`, `docs/TESTING.md`
- Topology: `Explore.slnx`, `.github/workflows/_build-test.yml`, `.github/workflows/test.yml`
- Representative pathology and invariant files named in the implementation plan evidence log
- Repository inventory/search outputs captured in the planning session

## Missing Evidence
- Baseline duration, flake, and nonzero-test counts for each surviving project
- Final per-file keep/replace/delete classifications
- CI evidence after mutation-wrapper and routing removal

## Context Inventory
- Product behavior changes: none intended
- Provider decisions: test taxonomy, project topology, CI lanes, deletion/replacement policy
- Data/privacy changes: none
- Security boundary changes: none intended; preservation is mandatory
- External dependency changes: none planned
- Affected operations: developer feedback, CI runtime lanes, release verification

## Planning Handoff
- Workstream: test-suite-rationalization
- Status: current
- Reviewed input revision: sha256:72b7d1f34068ec891d112ee7a29302c993e55833d8ae33dbd4d33ea449410373
- Findings and mitigations: IVSD-F001 -> IVSD-M001; IVSD-F002 -> IVSD-M002; IVSD-F003 -> IVSD-M003; IVSD-F004 -> IVSD-M004
- Required plan mappings: F001/M001 -> invariant-disposition contract and every project phase; F002/M002 -> governance/topology, Application, API, CI; F003/M003 -> Architecture/API/Blazor; F004/M004 -> deterministic-test tasks
- Escalations required before: implementation of any unmapped critical-test deletion
- Refresh triggers: material change to deletion policy, security/privacy coverage, CI lane ownership, accessibility semantics, or project topology

## Review Lifecycle
| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-08-29 | none | draft | Implementation-plan intake | Evidence packet digest above |
| 2026-08-29 | draft | current | Completed triad mapped every material finding and mitigation | `dev/active/test-suite-rationalization/` plan/tasks/context |
