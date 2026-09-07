<!-- ABOUTME: I-VSD planning report for agentic testing acceleration and progressive verification. -->
<!-- ABOUTME: Evaluates computational stewardship (Amanah), harm alleviation (Raf' al-Haraj), and failure truthfulness (Sidq). -->

# Agentic Testing Acceleration & Progressive Verification — I-VSD Planning Report

Last Updated: 2026-09-06 Europe/Brussels

## Review Metadata
- Mode: planning
- Subject: AI agent productivity, progressive test ring architecture, shared-database isolation, and technical debt quarantine
- Workstream: agentic-testing-acceleration
- Report kind: planning report
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-09-06
- Reviewed input revision: `06e4e6878dd575d50e9c4116be7e1b7bad932804`
- Supersedes: none

## Scope

This report evaluates provider-controlled engineering decisions governing how AI agents and human developers verify software changes in the ISLAMU Event repository. In scope:
1. Eliminating agent "yak-shaving" loops where feature implementation agents absorb and spend hours debugging pre-existing test suite rot and container infrastructure.
2. Establishing a 3-Ring Progressive Verification model (Ring 1 fast in-memory sliced tests, Ring 2 single-provider phase exit, Ring 3 multi-database matrix at plan exit).
3. Re-balancing the test pyramid so 90%+ of domain and computational invariants execute as pure in-memory C# unit tests without I/O or container overhead.
4. Schema-per-test-run isolation to eliminate shared-database concurrency collisions, port deadlocks, and parallel build interference.
5. Codifying these practices across `AGENTS.md`, `.agents/CONTEXT_ENGINEERING.md`, `docs/internal/TESTING.md`, `docs/internal/AGENTIC_CONTEXT_ENGINEERING.md`, and execution skills.

Out of scope: deleting valid business invariant tests, weakening security/tenant isolation perimeters, modifying core runtime product contracts, or fiqh/scholarly rulings.

## Claim Boundary

Claims describe the repository at Git revision `06e4e6878dd575d50e9c4116be7e1b7bad932804` as executed on a Linux x86_64 host. Analysis reflects verified TUnit, EF Core 9 / .NET 10, PostgreSQL container, and multi-harness agent behavior.

## Findings

| ID | Lifecycle | Severity | Claim type | Principle / Domain | Stakeholder | Provider-controlled decision | Evidence | Validation | Mitigation |
|---|---|---|---|---|---|---|---|---|---|
| `IVSD-F001` | open | High | Waste | Amanah (stewardship), Israf (waste prevention) | Developers, AI agents, platform sponsors | Whether autonomous agents may run full 5-database container integration suites repeatedly on micro-edits | Observed 50% test / 15% container vs 20% code effort split; single tasks consuming full-day agent sessions | Measured runtime analysis | `IVSD-M001` |
| `IVSD-F002` | open | High | Harm | Raf' al-Haraj (hardship removal), developer dignity | Developers, maintainers | Whether an agent tasked with a localized feature must absorb and repair pre-existing persistence suite rot | Agent spent hours debugging unrelated transaction fixtures and container race conditions during Unicode search task | Verified session log | `IVSD-M002` |
| `IVSD-F003` | open | High | Deception / Confusion | Sidq (truthfulness), Adalah (justice) | AI agents, developers | Whether shared database state and container collisions may falsely flag working code as defective | Parallel test runs colliding on shared port/schema, triggering false-negative panic in autonomous agents | Repository test metrics | `IVSD-M003` |
| `IVSD-F004` | open | Medium | Waste | Ihsan (excellence), resource stewardship | Self-hosting operators, contributors | Whether computational/normalization invariants require heavy database roundtrips instead of fast memory checks | Unicode case-folding and FormC normalization validated via live container queries rather than in-memory tests | EF Core integration logs | `IVSD-M004` |

## Recommendations

- `IVSD-M001`: Adopt the **3-Ring Progressive Verification Model**: Ring 1 (< 2s, `--treenode-filter`, in-memory C#), Ring 2 (< 15s, single canonical DB engine at phase exit), Ring 3 (multi-database matrix strictly at Plan Exit Gate or CI).
- `IVSD-M002`: Enforce the **Yak-Shaving Quarantine Rule**: when an unrelated pre-existing test fails during a feature workstream, the agent must isolate it, record it under `context.md` as external debt, prove its own slice is green, and quarantine the defect for separate remediation.
- `IVSD-M003`: Implement **Schema-Per-Run Isolation** in database integration test fixtures (`search_path = test_<run_id>` / isolated memory DBs), guaranteeing zero cross-test interference and zero port deadlocks.
- `IVSD-M004`: Re-balance the test pyramid by mandating that pure algorithmic, normalization, arithmetic, and state-machine invariants reside in `Event.Domain.UnitTests` as zero-I/O tests, reserving persistence integration tests strictly for EF Core mapping and SQL provider syntax.

## Refresh Triggers
1. Introduction of new test frameworks or test runner migrations.
2. Major database provider additions or removals.
3. Observed regression in agent task implementation velocity.
