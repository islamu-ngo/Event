<!-- ABOUTME: I-VSD planning report for CI failure prevention and Unicode search simplification. -->
<!-- ABOUTME: Evaluates developer tooling integrity, verification stewardship, and universal text determinism across database providers. -->

# CI Failure Prevention & Unicode Search Simplification — I-VSD Planning Report

Last Updated: 2026-09-06 Europe/Brussels

## Review Metadata
- Mode: planning
- Subject: Developer tooling integrity, pre-push verification gates, self-healing CI guidance, and Unicode text determinism across multi-database environments
- Workstream: prevent-ci-failures-and-unicode-simplification
- Report kind: planning report
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-09-06
- Reviewed input revision: `224a479a4e962686d2cc5ee78a067d304a1c2bab`
- Supersedes: none

## Scope

This report evaluates provider-controlled engineering and architecture decisions in eliminating recurring CI/CD workflow failures and retiring over-engineered Unicode text encoding. In scope:
1. Unified pre-push verification gates and git hooks preventing out-of-sync configuration templates, drifted ratchets, and broken documentation links from entering upstream branches.
2. Self-healing CI guidance providing explicit copy-paste remediation commands when drift is detected.
3. Dual-documentation path synchronization across workflows.
4. Replacing the 7x-expanded ASCII hex-token encoder (`UnicodeScalarKeyV1.cs`), 14,000-character database columns, and 1.1M-scalar ICU golden hash test with a clean, strongly typed pre-normalized binary UTF-8 search key (`UnicodeSearchKey`) with cross-provider binary collation.

Out of scope: product feature changes, external identity providers, runtime payment processing, and fiqh/scholarly rulings.

## Claim Boundary

Claims describe the repository at Git revision `224a479a4e962686d2cc5ee78a067d304a1c2bab` as executed on a Linux x86_64 host. Test analysis and collation findings reflect verified EF Core 9 / .NET 10 behaviors across PostgreSQL (Npgsql), SQLite (Microsoft.Data.Sqlite), SQL Server, and MySQL/MariaDB (Pomelo). No claim is made about untested or unapproved third-party database plugins.

## Findings

| ID | Lifecycle | Severity | Claim type | Principle / Domain | Stakeholder | Provider-controlled decision | Evidence | Validation | Mitigation |
|---|---|---|---|---|---|---|---|---|---|
| `IVSD-F001` | open | High | Risk | Amanah (trust), stewardship | Contributors, maintainers, operators | Whether code and configuration generators may drift unverified until failing late in CI pipelines | 38 failed CI runs across 9 workflows in September 2026; template drift in `.env.example` broke `ratchets.yml` | Repository-verified | `IVSD-M001` |
| `IVSD-F002` | open | High | Fragility | Sidq (truthfulness), portability | Contributors, self-hosters | Whether CI test stability may be coupled to host operating system libicu versions via whole-scalar corpus golden hashes | `UnicodeScalarKeyV1Tests.UnicodeScalarCorpusMatchesVersionOneGoldenDigest` failed in CI because Ubuntu runner ICU 74 (Unicode 15.1) differed from developer ICU 78+ (Unicode 16) | Repository-verified | `IVSD-M002` |
| `IVSD-F003` | open | Medium | Waste | Ihsan (excellence), resource stewardship | Self-hosting operators, database administrators | Whether internationalized address search requires 7x column bloat (`VARCHAR(14,000)`) and custom ASCII tokenization | `LocationPiiConfiguration.cs:36` and `LocationConfiguration.cs:23` allocate 14,000 chars per row; `% 7 = 0` check constraints leak token width | Repository-verified | `IVSD-M003` |
| `IVSD-F004` | open | High | Harm | Universal dignity, non-discrimination | Global attendees, multi-lingual mosques/tenants | Whether non-Latin (Arabic, Cyrillic), accented (French, German), or composite (NFC/NFD) text may suffer silent search misses across self-hosted database engines | SQLite `lower()` only handles ASCII A-Z; PostgreSQL defaults to case-sensitive; decomposed NFD keyboards on iOS produce combining accents unindexed by raw LINQ | Repository-verified | `IVSD-M004` |
| `IVSD-F005` | open | Medium | Risk | Clarity, maintainer dignity | Contributors, documentation consumers | Whether internal documentation relocation (`docs/` to `docs/internal/`) may leave broken links and brittle CI paths | `validate-api-contract-skip-inventory.cs` and `docs-lint.yml` failed when documentation paths moved without workflow synchronization | Repository-verified | `IVSD-M005` |

## Recommendations

- `IVSD-M001`: Create a single, deterministic pre-push verification script (`eng/scripts/verify-ratchets.sh`) and wire it into a repository git hook (`.githooks/pre-push`). In CI, fail with clear self-healing guidance that outputs the exact copy-paste command required to synchronize generated assets.
- `IVSD-M002`: Retire the 1.1M-scalar golden digest test. Anchor Unicode determinism with focused invariant tests asserting canonical equivalence (NFC vs NFD), case folding invariance, and multi-script stability without binding to host ICU versions.
- `IVSD-M003`: Replace `UnicodeScalarKeyV1.cs` and 14,000-character columns with `UnicodeSearchKey` storing pre-normalized UTF-8 in standard `VARCHAR(300)` columns. Remove arbitrary `% 7 = 0` constraints.
- `IVSD-M004`: Enforce C# normalization (`text.Normalize(NormalizationForm.FormC).ToUpperInvariant()`) on both write and query paths, backed by explicit ordinal/binary collation in EF Core across all 5 supported database engines (PostgreSQL: `C`, SQLite: `BINARY`, SQL Server: `Latin1_General_100_BIN2`, MySQL: `utf8mb4_bin`).
- `IVSD-M005`: Scoped relative documentation link checks in `docs-lint.yml` to public documentation surfaces, and synchronize CI path arguments across workflows whenever docs are reorganized.

Rejected alternatives:
- Preserving `UnicodeScalarKeyV1.cs` with an open-ended list of ICU golden hashes. Rejected: Fragile, brittle, ties repository health to third-party C libraries, and preserves 7x database waste.
- Relying on database-native full-text search extensions (e.g. `pg_trgm`, SQLite FTS5). Rejected: Violates universal 5-database self-hosting portability and requires complex platform-specific installation.

## Stakeholders

- **Self-Hosting Operators**: Benefit from vastly reduced database storage footprint, standard relational indexes, and zero platform-specific extension dependencies.
- **Global Muslim Community & International Tenants**: Benefit from robust, equitable search across Arabic, accented Latin, and diverse language scripts without silent misses or decomposed keyboard bugs.
- **Contributors & AI Agents**: Benefit from fast pre-push validation, self-healing CI guidance, and the removal of opaque ICU digest CI blockers.

## I-VSD Principles And Domains

- **Amanah (Trust & Stewardship)**: Verification lanes must guard genuine business invariants rather than testing operating system library builds. Storage resources must be stewarded responsibly rather than inflated by 7x.
- **Sidq (Truthfulness)**: The system must provide honest, predictable search matching regardless of which database engine a self-hoster deploys.
- **Ihsan (Excellence)**: Developer tooling should guide contributors toward remediation through self-healing feedback rather than opaque pipeline failures.
- **Universal Dignity & Inclusivity**: Mosques, charities, and attendees globally use diverse scripts and devices; search normalization must treat Arabic, Turkish, European accents, and emojis with equal first-class determinism.

Non-applicable domains: Financial transaction settlement, content moderation, user tracking, or advertising.

## Validation Gaps

- MySQL 8 collation behavior was verified against documentation and EF Core Pomelo provider specs; runtime verification in this plan will be verified against SQLite and PostgreSQL in automated test suites.

## Escalation Needed

No scholarly fiqh escalation is required. Engineering decisions adhere strictly to core Islamic ethics of stewardship, truthfulness, resource efficiency, and universal access.

## Evidence Reviewed

- `src/Explore.Domain/ValueObjects/UnicodeScalarKeyV1.cs`
- `src/Explore.Domain/Location.cs`, `src/Explore.Domain/LocationPii.cs`
- `src/Explore.Persistence/Configurations/Entities/LocationConfiguration.cs`, `LocationPiiConfiguration.cs`
- `src/Explore.Persistence/Schema/PortableRelationalModelPolicy.cs`
- `src/Explore.Persistence/Queries/LocalAddressSuggestionQuery.cs`
- `tests/Event.Domain.UnitTests/UnicodeScalarKeyV1Tests.cs`
- `tests/Event.Persistence.IntegrationTests/Repositories/LocalAddressSuggestionQueryTests.cs`
- `.github/workflows/_build-test.yml`, `docs-lint.yml`, `openapi-contract.yml`, `test.yml`
- `eng/setup-assistant/GenerateSetupAssistantRatchets.cs`, `EnvironmentCatalogueGenerator/Program.cs`, `SetupCliCommandSchemaGenerator/Program.cs`
- `.ci/scripts/validate-api-contract-skip-inventory.cs`

## Context Inventory

- `eng/scripts/`: Currently does not exist; will host `verify-ratchets.sh` and `install-git-hooks.sh`.
- `.githooks/`: Will host version-controlled `pre-push` hook.
- Migrations: Greenfield pre-release state (`20260904200209_Init.cs`) enables clean regeneration under Rule 11.

## Planning Handoff

- Workstream: prevent-ci-failures-and-unicode-simplification
- Status: current / plan-aligned
- Reviewed input revision: `224a479a4e962686d2cc5ee78a067d304a1c2bab`
- Plan: [prevent-ci-failures-and-unicode-simplification-plan.md](../dev/active/prevent-ci-failures-and-unicode-simplification/prevent-ci-failures-and-unicode-simplification-plan.md)
- Tasks: [prevent-ci-failures-and-unicode-simplification-tasks.md](../dev/active/prevent-ci-failures-and-unicode-simplification/prevent-ci-failures-and-unicode-simplification-tasks.md)
- Context: [prevent-ci-failures-and-unicode-simplification-context.md](../dev/active/prevent-ci-failures-and-unicode-simplification/prevent-ci-failures-and-unicode-simplification-context.md)

### Scenario And Task Mapping

| Finding / Mitigation | Scenarios in Plan Section 3 | Tasks in the Execution Ledger | Disposition |
|---|---|---|---|
| `IVSD-F001` / `IVSD-M001` | Scenario 3.1, Scenario 3.2 | Task 1.1–1.3, Task 2.1–2.3 | Implement unified pre-push script, git hook, and self-healing CI feedback |
| `IVSD-F002` / `IVSD-M002` | Scenario 3.3, Scenario 3.4 | Task 6.1–6.3 | Retire 1.1M-scalar ICU test and implement focused invariant tests |
| `IVSD-F003` / `IVSD-M003` | Scenario 3.5, Scenario 3.6 | Task 3.1–3.3, Task 4.1–4.3 | Replace `UnicodeScalarKeyV1` with `UnicodeSearchKey` (VARCHAR(300)) and drop % 7 constraints |
| `IVSD-F004` / `IVSD-M004` | Scenario 3.5, Scenario 3.7 | Task 4.1–4.3, Task 5.1–5.3 | Enforce C# pre-normalization and cross-provider binary collation in EF Core |
| `IVSD-F005` / `IVSD-M005` | Scenario 3.8 | Task 2.2, Task 2.3 | Synchronize dual-doc paths across CI workflows and docs lint |

## Review Lifecycle

| Date | Previous Status | New Status | Event / Trigger | Rationale |
|---|---|---|---|---|
| 2026-09-06 | none | current / plan-aligned | Workstream planning initialization | Comprehensive I-VSD planning report grounded in repository evidence and architecture analysis |
