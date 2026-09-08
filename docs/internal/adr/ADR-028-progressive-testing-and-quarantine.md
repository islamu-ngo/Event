<!-- ABOUTME: Defines the canonical 3-Ring Progressive Verification Model and the Yak-Shaving Quarantine Rule. -->
<!-- ABOUTME: Protects engineering velocity by eliminating premature multi-container test loops and test-rot absorption. -->

# ADR-028: Progressive 3-Ring Verification Model and Yak-Shaving Quarantine Rule

- **Status:** Accepted
- **Date:** 2026-09-06
- **Deciders:** Architecture, Platform Engineering, Quality & Test Governance

## Context

During AI agent and developer implementation workflows, engineering velocity was severely degraded by two structural bottlenecks:
1. **Premature Heavy Test Execution:** Agents executed 5-database provider matrices, multi-container integration suites, and full 90,000 LOC test projects on minor atomic changes or intermediate phase transitions. This resulted in up to 50% of wall-clock time spent compiling test suites and diagnosing multi-container timeouts or container deadlocks rather than implementing product requirements.
2. **Opportunistic Test-Rot Absorption (Yak-Shaving):** Running broad test suites surfaced pre-existing failures or unrelated test debt from concurrent or uncommitted workstreams. Agents routinely derailed their primary task to debug and repair unrelated broken tests across unrelated modules, multiplying scope and context cost.

A deterministic, layered verification standard was required to decouple inner-loop code confidence from slow infrastructure verification while strictly guarding repository boundaries.

## Decision

### 1. The 3-Ring Progressive Verification Model

All code verification in the repository is strictly stratified into three progressive rings:

- **Ring 1: Inner Loop (Milliseconds to < 2 seconds)**
  - Scope: Pure in-memory unit tests (`Event.Domain.UnitTests`, `Event.Application.UnitTests`) and targeted TUnit tree-node slices (`--treenode-filter "/*/*/*<TargetClass>/*"`).
  - Invariants: Pure algorithmic logic, Unicode FormC normalization, case folding, state-machine transitions, request validators, and metadata assertions execute in memory with zero container dependencies.
  - Requirement: Run during active development loops and Red/Green/Refactor task transitions.

- **Ring 2: Phase Exit Gate (< 15 seconds)**
  - Scope: Sliced single-provider integration tests against the primary database (PostgreSQL container) or in-memory SQLite (`SqliteConnection("DataSource=:memory:")`).
  - Invariants: EF Core query translation, database constraints, schema-per-run isolation, and repository behaviors.
  - Prohibition: Never run multi-database matrix suites (MySQL, SQL Server, SQLite, and PostgreSQL together) or unrelated test projects at intermediate phase gates.

- **Ring 3: Plan Exit & Pull Request Gate (Exit Milestone / CI)**
  - Scope: Full solution Release build (`dotnet build --configuration Release --verbosity quiet`), architecture invariant tests (`Event.Architecture.Tests`), and comprehensive multi-provider matrix runs.
  - Execution: Reserved exclusively for the final workstream exit gate before pull request submission and automated CI execution.

### 2. The Yak-Shaving Quarantine Rule

When an agent or developer encounters a test failure during verification:
1. **Immediate Task-Ownership Assessment:** Determine whether the failure touches files, contracts, or features within the active intent's `paths_in_scope`.
2. **If Related:** Resolve the failure within the task's Red/Green/Refactor loop.
3. **If Unrelated (Pre-Existing Debt or Concurrent Churn):**
   - **DO NOT attempt to fix it.** Opportunistic fixes are strictly forbidden.
   - **Log the failure** in the active task context (`*-context.md`) under a dedicated Quarantine section, documenting the failing test symbol, root cause summary, and reason for exclusion.
   - **Immediately narrow the execution filter** using `--treenode-filter` to target only task-owned test classes.
   - **Proceed with planned scope.**

### 3. Dynamic Schema-Per-Run Isolation

All persistence integration tests executing against shared containerized PostgreSQL instances must use dynamic schema-per-run isolation (`test_<run_id>`) via `PostgresSearchPathInterceptor` rather than dropping or sharing tables in public schemas. SQLite integration tests must use isolated in-memory connection URIs (`SqliteTestDatabaseFactory.CreateIsolatedMemoryConnectionString()`) to guarantee zero inter-test interference and deadlock freedom.

## Consequences

- **Velocity:** Inner-loop feedback drops from tens of seconds or minutes to milliseconds, eliminating the 50% test diagnosis time sink.
- **Predictability:** Workstreams remain bounded to their declared intents without cascading into multi-hour rabbit holes repairing pre-existing test rot.
- **Determinism:** Tests run in parallel without table collisions or container deadlocks.
- **Rigor Preserved:** Full matrix verification and architecture tests remain strictly enforced at Ring 3 before PR merge.
